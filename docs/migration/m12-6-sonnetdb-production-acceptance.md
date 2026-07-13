# M12.6 SonnetDB 生产部署验收

## 验收结论

2026-07-13 在 `gitcandy.com` 完成 `#139L` 真实生产部署、Web、Git Smart HTTP、内置 SSH、Git LFS、一致备份恢复和镜像回滚演练。GitCandy 继续使用单进程 host，复用现有 Caddy 与内部 SonnetDB；SonnetDB 和容器本地 HTTP 端口未暴露公网。

本文档和生产日志不记录 SonnetDB token、用户明文密码、Authorization header、SSH private key 或生产连接串。生产验收 fixture 使用独立私有账号和仓库，与现有用户数据隔离。

## 部署基线

| 项目 | 生产值 |
| --- | --- |
| DNS | `gitcandy.com` CNAME 到 `sonnet.vip`，最终解析 `192.220.46.211` |
| Web | Caddy `80/443` -> `gitcandy:8080`，`flush_interval -1` |
| SSH | GitCandy 内置 SSH 公网 TCP `2222` |
| GitCandy image | `iotsharp/gitcandy:sonnet-vip-fb03a12-sonnet14aa3e9`，digest `sha256:1dd98268ee45bf8d53064c3af00105a74abe62060a27ab650a87595458fedef9` |
| SonnetDB image | `iotsharp/sonnetdb:gitcandy-435010e-efsql`，digest `sha256:f27ef7acd8863a11bb759a440324f8d80be4b5c035335909fdc447b626a5ff64` |
| 持久目录 | `/opt/gitcandy/data`，包含 repositories、`cache/lfs`、SSH host key 和 Data Protection keys |
| 数据库 | 内部 SonnetDB `gitcandy` database；宿主机和公网均不映射 `5080` |
| 资源限制 | GitCandy 384 MiB limit、128 MiB reservation、read-only root filesystem、`unless-stopped` |

最终资源快照中 GitCandy 使用 112.2 MiB/384 MiB、23 PIDs，SonnetDB 使用 48.58 MiB、33 PIDs。该快照只证明验收时资源限制生效，不代表容量或压力测试结论。

## 外部行为验收

| 场景 | 结果与证据 |
| --- | --- |
| HTTP/TLS | `http://gitcandy.com/` 返回 308 到 HTTPS；`https://gitcandy.com/health/ready` 返回 200，TLS certificate verification result 为 0，HSTS 生效 |
| Proxy origin | 登录后的规范 clone URL 为 `https://gitcandy.com/accept139l/prod-acceptance.git`，未出现容器 host、内部地址或 HTTP scheme |
| Identity cookie | `.GitCandy.Identity` 为 `Secure`、`HttpOnly`、`SameSite=Lax`；恢复后既有浏览器会话继续有效，证明 Data Protection key ring 被恢复 |
| Web | 完成注册、自动登录、私有仓库创建、SSH public key 登记和恢复后的仓库页面访问 |
| 私有边界 | 匿名请求私有仓库 `info/refs` 返回 401，并带 `WWW-Authenticate: Basic realm="GitCandy"` |
| Smart HTTP | HTTPS clone/push/fetch 通过；advertisement 返回 `application/x-git-upload-pack-advertisement` 和 no-cache headers |
| 内置 SSH | RSA-3072 key 在公网 `2222` 完成 clone/fetch/push；host key 指纹在恢复前后均为 `SHA256:vMGn/gsOtTOCwSIDBNL6Osbl7YxfRUscdn2cVV3JqMo` |
| Transport 一致性 | SSH push 的 commit `082f570b7b35cb061661db6140ebe61093197879` 可由 HTTPS fetch；最终两条 transport 均读取 `467555a1b9d9ab0d12dfec372441ae6614f51ade` |
| Git LFS | OID `85c7e99d05adbb199ec12558ce1cc6fcd6851ab715437c802eb89b1d7ba254e0` 上传成功；全新 HTTPS clone 下载并还原 75-byte payload |
| 端口边界 | 公网 `2222` 可达；SonnetDB `5080` 和容器本地 HTTP `18080` 不可达 |
| 日志与页面 | GitCandy/Caddy 日志中的真实 token 命中数为 0；GitCandy 日志中的内部 endpoint 命中数为 0；匿名页面未出现 SonnetDB、容器地址、`/opt` 路径或 token |

生产没有启用 OpenID Connect provider，因此本次不发起真实 provider callback。Forwarded Headers 的外部 scheme/host 已由 Secure Identity cookie、HTTPS 绝对 clone URL 和恢复后的登录会话共同验证；未来启用 OIDC 时仍需用实际 provider 单独验证 `https://gitcandy.com/signin-oidc`。

## 一致备份与恢复

恢复点为 `/opt/backups/gitcandy-m12.6-20260713T031948Z`，目录权限 `0700`，敏感文件权限 `0600`。备份前依次停止 GitCandy 和 SonnetDB，阻止 Web/SSH/LFS/数据库写入；恢复集包含：

- SonnetDB `gitcandy` database 和 SonnetDB server 配置；
- repositories 与真实 Git refs/objects；
- `cache/lfs` 中的真实 LFS object；
- SSH host key 和 Data Protection keys；
- GitCandy production `.env`、Compose、Caddy fragment；
- GitCandy/SonnetDB image tag、digest、repository HEAD、LFS OID 和逐文件 SHA-256 manifest。

维护窗口从 `2026-07-13T03:19:48Z` 到 `03:20:02Z`，共 14 秒。演练不是只解包到临时目录：活动 GitCandy data 与 `gitcandy` database 先移动到诊断目录，再由恢复集重建原路径；启动前逐文件 SHA-256 全部通过。恢复后完成 readiness、Web cookie、HTTP/SSH fetch、LFS 全新 clone、repository HEAD、LFS size 和 host key fingerprint 复核。

恢复目录共 142 个文件、约 2.3 MiB。这是当前空载 fixture 的实际大小，不应用于生产容量外推。普通 cache 可重建，但 `cache/lfs` 不可重建，必须继续进入每个恢复点。

## 镜像回滚

2026-07-13T03:21:50Z 启动服务器保留的旧候选 `gitcandy:191b2ae`（digest `sha256:fd2741b0e2466bb4f7dde5e54bc8bdbcf8a0fc1d7374678e5ea91f5a718bb405`），候选容器和 readiness 均通过。随后再次停止 GitCandy/SonnetDB，恢复同一数据和配置快照，并把 Compose 固定回生产镜像 `sha256:1dd98268...`。`03:22:12Z` SonnetDB 与 GitCandy 均恢复 healthy，最终 HTTP/SSH HEAD、LFS 和 host key 再次通过。

该演练证明生产回滚必须同时恢复固定 image、数据库、repositories/LFS、host key、Data Protection keys 和配置，不能只修改 image tag 后让旧程序继续写入较新的状态。

## 已知边界

- 本次内置 SSH 验收使用 RSA-3072。当前服务端拒绝本机 Ed25519 key，并且 OpenSSH 10 提示当前 key exchange 没有 post-quantum hybrid algorithm；需要在后续独立 SSH 兼容性任务中扩展算法矩阵，不影响本次 RSA 支持路径结论。
- 浏览器验收没有 console error；Lucide 对 `tag` 和 `git-pull-request` 各产生一个缺图标 warning。该现有静态资源问题不影响 Web/Git/恢复结论，后续 UI 任务应补齐图标 bundle。
- 本次执行真实协议 smoke 和恢复/回滚，没有执行大 pack、并发 push、长稳或压力测试；这些保护网仍由发布测试和后续容量验收持续执行。
- 验收 fixture `accept139l/prod-acceptance` 保持私有，仅用于后续生产协议 smoke；其明文凭据和 private key 不在仓库、文档或服务器配置中记录。

## 本地回归

验收文档收口后在 Windows / .NET SDK 10.0.301 执行：

```powershell
dotnet build GitCandy.slnx --configuration Release
dotnet test GitCandy.slnx --configuration Release --no-build
```

Release build 为 0 warning、0 error；`GitCandy.Data.Tests` 61/61、`GitCandy.Tests` 102/102，合计 163/163 通过，无跳过或失败。该任务不新增 schema、公开路由或配置键；生产 fixture、恢复点和运行镜像均保持固定版本，可按本文记录回滚。

## 对应 ROADMAP

- Milestone 12.6 / `#139J-#139L`：SonnetDB provider、migration/兼容保护网和 `gitcandy.com` 真实生产部署全部闭环。
- 本记录完成后，M12.6 整体退出活动路线图并进入完成历史。
