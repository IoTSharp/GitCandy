# M0 #003 Git HTTP 行为清单

记录日期：2026-07-09

## 验收结论

- 已记录旧 ASP.NET MVC 5 Git Smart HTTP 行为基线，覆盖 `clone`、`fetch`、`push`、认证失败、权限不足、仓库不存在和 service 不支持行为。
- 本清单作为后续 ASP.NET Core Git Smart HTTP 迁移的行为保护网。迁移实现可以重建 controller、认证 scheme 和 Git backend，但公开 Git URL、streaming 响应、认证挑战、权限语义、content type 和 no-cache header 必须能对照本文件说明差异。
- 本任务只做静态行为梳理，没有启动旧 MVC5 站点执行真实 HTTP `git clone/fetch/push`。可重复自动化脚本由 M0 #008 继续补齐。
- SSH clone/fetch/push 行为不在本文件展开，由 M0 #004 覆盖。

## 行为来源

主要读取的旧实现：

- `GitCandy/App_Start/RouteConfig.cs`
- `GitCandy/Controllers/GitController.cs`
- `GitCandy/Filters/SmartGitAttribute.cs`
- `GitCandy/Filters/SmartAuthorizeAttribute.cs`
- `GitCandy/Controllers/CandyControllerBase.cs`
- `GitCandy/Git/GitService.cs`
- `GitCandy/Data/RepositoryService.cs`
- `GitCandy/Data/MembershipService.cs`
- `GitCandy/Configuration/UserConfiguration.cs`
- `GitCandy/Configuration/GitCoreResloverAttribute.cs`
- `GitCandy/Web.config`
- `docs/migration/m0-001-test-data-and-sample-repositories.md`

## 路由和入口

### 公开 Git URL

| URL 模式 | Controller/Action | 备注 |
| --- | --- | --- |
| `/git/{project}.git/{*verb}` | `Git/Smart` | 兼容标准 Git remote URL，例如 `/git/public-demo.git/info/refs?service=git-upload-pack`。 |
| `/git/{project}/{*verb}` | `Git/Smart` | 兼容不带 `.git` 后缀的 URL，例如 `/git/public-demo/info/refs?service=git-upload-pack`。 |

路由结果：

- `{project}` 不包含 `.git` 后缀，传入旧 action 的仓库名是 `public-demo` 这一类逻辑仓库名。
- `{*verb}` 用于区分 `info/refs`、`git-upload-pack` 和 `git-receive-pack`。
- `service` 是 query string 参数，不是 route segment。Git 客户端 discovery 请求使用 `info/refs?service=git-upload-pack` 或 `info/refs?service=git-receive-pack`。
- 旧路由没有 method constraint；GET、POST 或其他 HTTP method 都会先进入相同 MVC action/filter，再由 Git 客户端协议自然决定是否可用。

### Action 分派

`GitController.Smart(project, service, verb)` 的分派规则：

| `verb` | 行为 |
| --- | --- |
| `info/refs` | 调用 `InfoRefs(project, service)`，输出 advertisement。 |
| `git-upload-pack` | 调用 `ExecutePack(project, "git-upload-pack")`，用于 clone/fetch pack negotiation。 |
| `git-receive-pack` | 调用 `ExecutePack(project, "git-receive-pack")`，用于 push pack negotiation。 |
| 其他值 | 302 重定向到 `Repository/Tree/{project}`。 |

HTTP 不支持 `git-upload-archive`。普通 archive 下载走 Web 仓库浏览页的 `/Repository/Archive/{name}/{*path}`，不是 Git Smart HTTP service。

## 请求限制和 IIS 行为

旧 `Web.config` 中与 Git HTTP 相关的配置：

| 配置 | 值 | 迁移关注点 |
| --- | --- | --- |
| `httpRuntime requestPathInvalidCharacters` | 空字符串 | 旧站点允许更宽松的路径字符；迁移时要显式处理 URL escaping 和 path traversal。 |
| `httpRuntime maxRequestLength` | `4194304` KB | ASP.NET 层约 4 GB；新 Kestrel/IIS 限制需要单独配置。 |
| `httpRuntime executionTimeout` | `1800` 秒 | 长 clone/push 允许 30 分钟；迁移时需要 timeout 与取消策略。 |
| `requestFiltering allowDoubleEscaping` | `true` | Git ref/path 中的编码字符不能被 IIS 提前拒绝。 |
| `requestLimits maxAllowedContentLength` | `134217728` bytes | IIS request filtering 层约 128 MB，与 `httpRuntime` 限制不同。 |
| `fileExtensions` / `hiddenSegments` | `clear` | 旧站点放开扩展名和隐藏段过滤，避免 Git 路径被 IIS 拦截。 |
| `modules runAllManagedModulesForAllRequests` | `true` | 所有 Git URL 都进入 MVC 管线。 |

## 认证和授权

### 认证来源顺序

`SmartGitAttribute` 的认证顺序：

1. 读取 session key `GitCandyGitAuthorize` 中缓存的用户名。
2. 如果当前请求带旧 Web 登录 cookie 并解析出 `Token`，使用 `Token.Username`。
3. 如果存在 `Authorization` header，把 header 从第 7 个字符开始当成 Base64，按 ASCII 解码为 `username:password`，再调用 `MembershipService.Login(username, password)`。
4. 登录成功后只保存用户名，不为 Basic Auth 创建旧 `_gc_auth` token。

重要兼容点：

- Basic Auth 用户名可以是旧用户名或邮箱，因为 `MembershipService.Login` 按 `Name == id || Email == id` 查找用户。
- 如果 `UserConfiguration.Current.IsPublicServer=false` 且没有解析出用户名，任何 Git HTTP 请求都会在仓库权限判断前返回 401 Basic challenge；公开仓库匿名 clone/fetch 只在公有站点配置下成立。
- 旧实现没有显式校验 `Authorization` scheme 是否为 `Basic`，也没有保护 malformed Base64 或缺少冒号的 header；这类请求可能变成未处理异常。ASP.NET Core 迁移时可以改进健壮性，但需要在兼容性说明中标明。
- Git 客户端通常不会带浏览器 cookie；迁移目标必须使用独立 Git Basic/PAT authentication scheme，不能依赖 Web Identity cookie。

### 授权规则

授权先看 query string 中的 `service`：

| `service` | 权限判断 |
| --- | --- |
| 空值 | 允许进入 action，通常会由 action 处理为跳转或错误。 |
| `git-upload-pack` | 调用 `RepositoryService.CanReadRepository(project, username)`。 |
| `git-receive-pack` | 调用 `RepositoryService.CanWriteRepository(project, username)`。 |
| 其他值 | 不授权。 |

读权限语义：

- 仓库 `AllowAnonymousRead=true` 时匿名可读。
- 用户直接仓库角色 `AllowRead=true` 时可读。
- 用户所在团队的仓库角色 `AllowRead=true` 时可读。
- 系统管理员对已存在的仓库可读。

写权限语义：

- 仓库 `AllowAnonymousRead=true` 且 `AllowAnonymousWrite=true` 时匿名可写。
- 用户直接仓库角色 `AllowRead=true` 且 `AllowWrite=true` 时可写。
- 用户所在团队的仓库角色 `AllowRead=true` 且 `AllowWrite=true` 时可写。
- 系统管理员对已存在的仓库可写。

`IsPrivate` 本身不直接参与 Git HTTP 权限查询；匿名能否读写由 `AllowAnonymousRead` 和 `AllowAnonymousWrite` 决定。M0 样例数据把 `public-demo` 设为匿名可读、匿名不可写，把 `private-demo` 设为匿名不可读、匿名不可写。

### 授权失败结果

`SmartGitAttribute.HandleUnauthorizedRequest` 的结果：

| 场景 | 旧结果 |
| --- | --- |
| 没有旧 Web `Token`，包括匿名 Git 客户端、Basic 密码错误、Basic 用户无权限 | 清空响应，返回 401，并添加 `WWW-Authenticate: Basic realm="GitCandy"`。 |
| 有旧 Web `Token` 但没有 Git 权限 | 抛出 `UnauthorizedAccessException`。 |

这和普通 Web 页面 `SmartAuthorizeAttribute` 不同：Git HTTP 不会 302 到 `/Account/Login`。

## Git Smart HTTP 响应行为

### `info/refs`

授权通过后，`InfoRefs(project, service)`：

- 设置 `Response.Charset = ""`。
- 设置 `Content-Type: application/x-{service}-advertisement`。
- 设置 no-cache headers。
- 先写 packet-line `# service={service}\n`，再写 flush packet `0000`。
- 调用 Git helper：`{service.Substring(4)} --stateless-rpc --advertise-refs "{repositoryPath}"`。
- `service=git-upload-pack` 时实际 helper service 是 `upload-pack`。
- `service=git-receive-pack` 时实际 helper service 是 `receive-pack`。

no-cache headers：

| Header | 值 |
| --- | --- |
| `Expires` | `Fri, 01 Jan 1980 00:00:00 GMT` |
| `Pragma` | `no-cache` |
| `Cache-Control` | `no-cache, max-age=0, must-revalidate` |

异常结果：

| 异常 | 旧结果 |
| --- | --- |
| `RepositoryNotFoundException` | 抛出 404 `HttpException`。 |
| 其他异常 | 抛出 500 `HttpException`。 |

注意：按当前代码路径，仓库是否存在通常已在授权阶段通过数据库权限查询决定。没有数据库记录的仓库更常见的 Git 客户端结果是 401 Basic challenge，而不是 action 内部的 404。

### `git-upload-pack` 和 `git-receive-pack`

授权通过后，`ExecutePack(project, service)`：

- 设置 `Response.Charset = ""`。
- 设置 `Content-Type: application/x-{service}-result`。
- 设置同一组 no-cache headers。
- 调用 Git helper：`{service.Substring(4)} --stateless-rpc "{repositoryPath}"`。
- 请求 body 通过 stdin 传给 Git helper，helper stdout 直接复制到 response body。

请求 body：

- `Content-Encoding: gzip` 时，用 `GZipStream` 解压 `Request.GetBufferlessInputStream(true)`。
- 其他情况直接读取 `Request.GetBufferlessInputStream(true)`。
- 旧实现使用同步 stream copy，不完整读入 pack 文件。

迁移风险：

- 旧实现内部使用 `ProcessStartInfo(fileName, argsString)` 和字符串拼接参数，并把 repository path 拼进命令行参数字符串。M6 迁移必须收敛到 `IGitTransportBackend`，并使用 `ProcessStartInfo.ArgumentList` 或等价结构化参数。
- 旧实现没有检查 Git helper exit code，也没有把 stderr 写入可诊断日志。
- 旧实现没有显式 `CancellationToken`、限流或 graceful shutdown。

## M0 样例数据 Git HTTP 场景

以下场景基于 M0 #001 样例用户和仓库，并假设旧站点按默认推荐值以 `IsPublicServer=true` 运行。`alice` 是两个样例仓库 owner；`bob` 通过 `core` 团队获得 `private-demo` 读写权限；`carol` 没有 `private-demo` 权限。

| 编号 | 场景 | 请求/命令 | 旧行为期望 |
| --- | --- | --- | --- |
| GITHTTP-001 | 匿名 clone 公开仓库 | `git clone http://localhost:<port>/git/public-demo.git` | discovery 使用 `git-upload-pack`，返回 200 advertisement；pack POST 返回 200 result；clone 成功。 |
| GITHTTP-002 | 匿名 fetch 公开仓库 | 已 clone 后执行 `git fetch --all --tags` | `git-upload-pack` 读权限通过，fetch 成功。 |
| GITHTTP-003 | 匿名 push 公开仓库默认失败 | 对 `public-demo` 执行匿名 `git push` | `git-receive-pack` 写权限失败，返回 401，并带 `WWW-Authenticate: Basic realm="GitCandy"`。 |
| GITHTTP-004 | owner push 公开仓库成功 | 使用 `alice` Basic 凭据 push `public-demo` | `git-receive-pack` 写权限通过，push 成功。 |
| GITHTTP-005 | 匿名 clone 私有仓库失败 | `git clone http://localhost:<port>/git/private-demo.git` | `git-upload-pack` 读权限失败，返回 401 Basic challenge。 |
| GITHTTP-006 | 团队成员 clone 私有仓库成功 | 使用 `bob` Basic 凭据 clone `private-demo` | 团队仓库读权限通过，clone 成功。 |
| GITHTTP-007 | 团队成员 push 私有仓库成功 | 使用 `bob` Basic 凭据 push `private-demo` | 团队仓库写权限通过，push 成功。 |
| GITHTTP-008 | 无权限用户访问私有仓库失败 | 使用 `carol` Basic 凭据 clone/fetch/push `private-demo` | Basic 用户无对应权限，返回 401 Basic challenge。 |
| GITHTTP-009 | 密码错误 | 使用存在用户但错误密码 clone/fetch/push | 登录失败后按匿名处理；私有仓库或写操作返回 401 Basic challenge。 |
| GITHTTP-010 | 仓库不存在 | `git clone http://localhost:<port>/git/missing-demo.git` | 授权阶段找不到可读仓库记录，匿名或 Basic Git 客户端通常收到 401 Basic challenge。 |
| GITHTTP-011 | service 不支持 | `GET /git/public-demo.git/info/refs?service=git-upload-archive` | `service` 不是 upload-pack/receive-pack，授权失败，返回 401 Basic challenge。 |
| GITHTTP-012 | 缺少 service 的 discovery | `GET /git/public-demo.git/info/refs` | 授权允许进入 action，但 `service.Substring(4)` 触发异常，旧行为为 500。迁移时可改成更明确的 400/403，但必须说明差异。 |
| GITHTTP-013 | 非 Git verb | `GET /git/public-demo.git/anything` 且无 `service` | 授权允许进入 action，302 到 `/Repository/Tree/public-demo`。 |
| GITHTTP-014 | 不带 `.git` 后缀 clone | `git clone http://localhost:<port>/git/public-demo` | 命中 `git/{project}/{*verb}`，应与 `.git` 后缀 URL 行为一致。 |

## 手动验证命令草案

M0 #008 会把这些命令收敛为可重复脚本。手动执行时先启动旧 MVC5 GitCandy 站点，并准备 M0 #001 样例数据和仓库。

```powershell
$base = "http://localhost:<port>"
$work = ".\artifacts\migration\m0-003"
New-Item -ItemType Directory -Force $work | Out-Null

git clone "$base/git/public-demo.git" "$work\public-anonymous"
git -C "$work\public-anonymous" fetch --all --tags

curl.exe -i "$base/git/public-demo.git/info/refs?service=git-upload-pack"
curl.exe -i "$base/git/private-demo.git/info/refs?service=git-upload-pack"
curl.exe -i "$base/git/public-demo.git/info/refs?service=git-upload-archive"
curl.exe -i "$base/git/public-demo.git/info/refs"
```

带凭据场景后续脚本应避免把密码写入仓库文件。可使用本机环境变量：

```powershell
$alice = $env:GITCANDY_M0_ALICE_PASSWORD
$bob = $env:GITCANDY_M0_BOB_PASSWORD
$carol = $env:GITCANDY_M0_CAROL_PASSWORD

git clone "http://bob:$bob@localhost:<port>/git/private-demo.git" "$work\private-bob"
git clone "http://carol:$carol@localhost:<port>/git/private-demo.git" "$work\private-carol-denied"
```

调试 Git 客户端协议时可临时启用：

```powershell
$env:GIT_CURL_VERBOSE = "1"
$env:GIT_TRACE_PACKET = "1"
```

## ASP.NET Core 迁移验收要求

M6 Git Smart HTTP 迁移时至少要保留或明确说明以下行为：

- `/git/{project}.git/{*verb}` 和 `/git/{project}/{*verb}` 两套 URL 都可用。
- `info/refs?service=git-upload-pack` 返回 `application/x-git-upload-pack-advertisement`。
- `info/refs?service=git-receive-pack` 返回 `application/x-git-receive-pack-advertisement`。
- `git-upload-pack` POST 返回 `application/x-git-upload-pack-result`。
- `git-receive-pack` POST 返回 `application/x-git-receive-pack-result`。
- 成功响应包含旧 no-cache headers 或等价禁缓存策略。
- 未认证或 Basic 认证失败的 Git 客户端收到 401，并带 `WWW-Authenticate: Basic realm="GitCandy"` 或迁移后明确约定的 Git Basic realm。
- Web Identity cookie 不能成为 Git 客户端认证的必要条件。
- 私有仓库匿名 clone/fetch 失败，公开仓库匿名 clone/fetch 成功。
- 默认公开仓库匿名 push 失败，具备写权限的 owner/team/admin push 成功。
- 仓库不存在、权限不足和 service 不支持的 Git 客户端错误行为有明确测试覆盖。
- pack 请求/响应必须流式转发，不能把 pack 文件完整读入内存。
- gzip request body 仍能解压。
- Git helper 调用必须通过受控 `IGitTransportBackend` 或等价单一抽象进入，不在 controller 或业务服务中散落 `Process.Start`。
- 外部 Git helper 参数必须结构化传递，不能把用户输入拼进 shell command。
- 仓库路径必须归一化并验证位于配置的 repository 根目录内。

## 兼容性影响

本任务只新增迁移文档并更新路线图状态，不改变：

- 公开路由和 Git URL。
- 数据库 schema、索引、默认值或 seed 行为。
- Identity cookie、Basic Auth 或权限语义。
- 配置键、环境变量或部署方式。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。
