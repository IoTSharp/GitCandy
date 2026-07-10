# M6 #060-#069 Git Smart HTTP 迁移

记录日期：2026-07-10

## 验收结论

- `/git/{project}.git/{*verb}` 和 `/git/{project}/{*verb}` 已从 M1 占位响应切换到 ASP.NET Core `GitController.Smart`。
- `info/refs`、`git-upload-pack` 和 `git-receive-pack` 使用 `Request.Body`、`Response.Body` 与 Git helper stdin/stdout 双向并发流式转发，不缓存完整 pack。
- 已建立 `IGitTransportBackend` 单一入口。新 ASP.NET Core 主线只有 `GitProcessTransportBackend` 可以启动 Git transport helper，并通过 `ProcessStartInfo.ArgumentList` 传递 service、flag 和仓库路径。
- 独立 `GitCandy.GitBasic` scheme 显式用于 Git HTTP；Web Identity cookie 不参与 Git endpoint 认证。
- 已验证 Git protocol v0/v1 service announcement 和 protocol v2 `Git-Protocol: version=2` advertisement。
- 已验证 gzip request body、no-cache headers、Basic challenge、请求大小、timeout、路径 traversal、物理仓库缺失和 service 不支持行为。
- 真实 Kestrel + SQLite + Git 2.55 客户端测试已通过 `.git`/无后缀 clone、protocol v2 fetch、Identity Basic Auth push 和 24 MiB 不可压缩文件 pack push。

## 对应 ROADMAP

| 编号 | 实现与证据 |
| --- | --- |
| #060 | 专用 ASP.NET Core `GitController.Smart` 替换 `CompatibilityController` 占位入口 |
| #061 | request/helper/response 使用异步 stream copy；helper stdin、stdout、stderr 和退出等待并发执行 |
| #062 | `Content-Encoding: gzip` 通过 `GZipStream` 边读边解压，`identity` 和无 encoding 直接透传 |
| #063 | 精确保留 advertisement/result content type、`Expires`、`Pragma`、`Cache-Control` 和 Basic challenge |
| #064 | 新增 `GitCandy:GitHttp` 请求体、30 分钟 timeout、stream buffer 和 helper 并发上限配置 |
| #065 | 仓库名拒绝 `/`、`\`、`.`、`..`；路径再次归一化并校验根目录，最终 symlink/junction target 不得逃逸 |
| #066 | `IGitTransportBackend` 支持 upload-pack、receive-pack、upload-archive，供 M7 SSH 继续复用 |
| #067 | 真实 Git 客户端 clone/fetch/push 测试通过 |
| #068 | 24 MiB 随机文件形成的较大 pack 通过同一 streaming endpoint push 成功 |
| #069 | 匿名/错误凭据 401、已认证无权限 403、仓库不存在 404、service 不支持 403 均有测试 |

## 协议行为

| 请求 | 权限 | 成功 content type |
| --- | --- | --- |
| `GET info/refs?service=git-upload-pack` | repository read | `application/x-git-upload-pack-advertisement` |
| `POST git-upload-pack` | repository read | `application/x-git-upload-pack-result` |
| `GET info/refs?service=git-receive-pack` | repository write | `application/x-git-receive-pack-advertisement` |
| `POST git-receive-pack` | repository write | `application/x-git-receive-pack-result` |

所有 Git 协议响应保留：

```text
Expires: Fri, 01 Jan 1980 00:00:00 GMT
Pragma: no-cache
Cache-Control: no-cache, max-age=0, must-revalidate
```

protocol v0/v1 discovery 先输出 `# service=...` packet-line 和 flush packet。upload-pack protocol v2 discovery 将受控的 `Git-Protocol: version=2` 映射为 helper 的 `GIT_PROTOCOL=version=2`，并按 Git `http-backend` 行为直接输出 `version 2` advertisement，不添加 v0 service announcement。未知 protocol header 不传给子进程。

POST 权限只由实际 URL verb 决定。旧实现按 query `service` 授权、按 verb 执行，存在 `git-receive-pack?service=git-upload-pack` 以读权限进入写 helper 的错配风险；迁移实现忽略 pack POST 上的 query service，从协议执行入口消除该问题。

## 认证和失败状态

| 场景 | 结果 |
| --- | --- |
| 匿名访问需要权限的仓库，或 Basic header/密码无效 | `401` + `WWW-Authenticate: Basic realm="GitCandy", charset="UTF-8"` |
| Basic 用户认证成功但没有仓库权限 | `403`，不触发 Web cookie redirect |
| EF metadata 或物理 repository 不存在 | `404` |
| `info/refs` 缺少 service | `400` |
| service 不支持 | `403` |
| HTTP method 与 discovery/pack verb 不匹配 | `405` + `Allow` |
| 请求体超过应用配置上限 | `413` |
| helper 在响应启动前超时 | `504`；响应已开始时中止连接，避免在 pack 中混入错误正文 |

相较旧 MVC5，已认证但无权限从通常的 401 明确为 403，仓库不存在从授权阶段常见的 401 明确为 404，缺少/不支持 service 从旧 500/401 明确为 400/403。这些差异不改变成功 clone/fetch/push wire protocol，并让 Git 客户端错误类别可测试。

## Backend 和路径边界

- `GitProcessTransportBackend` 在 `GitCorePath` 为空时从 `PATH` 查找 `git`/`git.exe`；配置非空时只从该目录选择 Git executable。
- helper 参数通过 `ArgumentList` 添加，不使用 shell、不构造命令行字符串。
- stdin 和 stdout 同时异步复制，避免 push 请求和 helper 响应互相等待；stderr 始终被 drain，但只保留最多 8 KiB 供诊断。
- stderr 日志会替换物理仓库绝对路径，不记录 Authorization header、密码或 token。
- 请求取消或 30 分钟 timeout 会终止整个 helper process tree，并等待受限的退出时间。
- 全局 `SemaphoreSlim` 把同时运行的 helper 限制在 `MaxConcurrentOperations`，排队等待同样受请求 timeout/cancellation 控制。
- 每次 helper 执行记录 actor、repository、service、advertisement 类型、结果和耗时；不记录凭据或协议 payload。
- 物理布局同时兼容 `{RepositoryPath}/{name}` 和 M0 fixture 使用的 `{RepositoryPath}/{name}.git`；两者同时存在时优先旧版无后缀目录。
- lexical full path 和已存在目录的最终 symlink/junction target 都必须位于 repository root 内。

## 配置和反向代理

默认配置：

```json
{
  "GitCandy": {
    "GitHttp": {
      "MaxRequestBodySize": 4294967296,
      "RequestTimeout": "00:30:00",
      "StreamBufferSize": 81920,
      "MaxConcurrentOperations": 16
    }
  }
}
```

endpoint 会在请求体尚未读取时设置可写的 `IHttpMaxRequestBodySizeFeature`。该设置无法放宽已经由 IIS、Nginx、Ingress 或其他上游拒绝的请求，部署时还需要同步配置：

- IIS/ANCM：`requestFiltering/requestLimits maxAllowedContentLength` 最大可设 `4294967295`，按实际仓库上限选择；需要兼容双重 escaping 时显式评估并设置 `allowDoubleEscaping="true"`；ANCM request timeout 至少覆盖 30 分钟。
- Nginx：按策略设置 `client_max_body_size`，Git 路由建议 `proxy_request_buffering off`、`proxy_buffering off`，并把 `proxy_read_timeout`/`proxy_send_timeout` 设置为至少 `1800s`。
- 任何上游放宽 escaping 后仍由 GitCandy 仓库名和 repository root 边界检查做最终拒绝，不能用代理配置替代应用校验。

## 验证记录

已运行：

```powershell
dotnet build .\src\GitCandy\GitCandy.csproj -p:UseArtifactsOutput=true -p:ArtifactsPath=.\artifacts\m6-build
dotnet test .\tests\GitCandy.Tests\GitCandy.Tests.csproj -p:UseArtifactsOutput=true -p:ArtifactsPath=.\artifacts\m6-test-build --filter "FullyQualifiedName~GitSmartHttp"
dotnet test .\GitCandy.slnx -p:UseArtifactsOutput=true -p:ArtifactsPath=.\artifacts\m6-full-test
dotnet build .\GitCandy.slnx --no-restore -p:UseArtifactsOutput=true -p:ArtifactsPath=.\artifacts\m6-full-test
```

结果：

- M6 定向测试 6/6 通过。
- 全解决方案测试 96/96 通过：`GitCandy.Data.Tests` 41 个，`GitCandy.Tests` 55 个。
- 全解决方案 build 通过，0 warnings / 0 errors。
- SQLite schema 创建和 Identity Basic 用户校验由真实 endpoint fixture 覆盖。
- `.git` 后缀 clone、无后缀 clone、fetch、Basic Auth push、24 MiB large-pack push 通过。
- Debug/Release 默认输出目录分别被用户已运行的 GitCandy 进程占用，因此验证使用 SDK 隔离 artifacts 输出；未停止或修改现有进程。

SSH clone/fetch/push 未运行：SSH listener 和 session handler 属于 M7，但 M6 backend 已保留非 stateless 和 upload-archive service 形态供 M7 接入。

## 兼容性、迁移和回滚

- 公开 Git URL、成功响应 content type、no-cache headers、Basic realm、repository root 配置和数据库 schema 均未改变。
- 新增可选配置节 `GitCandy:GitHttp`；未配置时使用上述默认值。
- Git helper 现在要求 `git` 可从 `PATH` 发现，或 `GitCandy:Application:GitCorePath` 指向包含 `git`/`git.exe` 的目录。
- 本切片不自动创建、删除或迁移 bare repository，不修改旧 SQL 脚本，不自动迁移生产数据库。
- 回滚时可恢复 M1 compatibility placeholder 路由并移除 `GitCandy:GitHttp` 配置；本切片没有 schema 或数据变更，无需数据库回滚。回滚后 ASP.NET Core host 将再次失去 clone/fetch/push 能力。
