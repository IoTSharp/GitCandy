# M13 #140/#140A/#143 机器凭据与基础 push gate

日期：2026-07-13

## 对应 ROADMAP

- `#140`：Personal Access Token 与 API auth。
- `#140A`：Deploy key 与机器凭据。
- `#143` 基础切片：force/delete、allowed push/merge、管理员 bypass，以及 Git HTTP、内置 SSH、OpenSSH、Web merge/branch delete 的统一 gate。
- required checks/approvals 不在本切片伪造状态；它们分别等待 `#142` Commit Status/Check 和 `#144` required review/CODEOWNERS 后接回同一 gate。

## 变更点

### Personal Access Token

- token 使用 `gcpat_` 前缀和 256-bit 随机 secret；数据库只保存 SHA-256 hash 与可识别 prefix，明文仅在创建响应显示一次。
- scope 为可扩展字符串声明：`api:read`、`api:write`、`git:read`、`git:write`；write 自动包含同类 read。
- API 使用独立 Bearer scheme 和 `GitCandy.Api.Read/Write` policy；Git Smart HTTP/LFS 继续使用独立 Basic scheme，并在 endpoint 再复核 Git read/write scope。
- 支持名称、到期、撤销、last-used，以及不包含 secret 的 create/authenticate/revoke 审计。

### Deploy key

- `DeployKeys` 与 Identity 用户 `SshKeys` 分表，deploy key 不映射为用户、cookie 或密码身份。
- key 绑定单个稳定 repository ID，可配置只读或可写、到期和撤销；内置 SSH 与 OpenSSH 共用 `ISshAccessService`。
- `SshFingerprintClaims` 为新建用户 key/deploy key 提供事务性全局唯一占位；迁移前用户 key 不改写，由双表查询阻止重复，之后的并发创建由 fingerprint 主键裁决。
- 撤销的 deploy key 保留 fingerprint 占位，避免相同公钥以另一身份重新出现。
- owner 从 canonical `/{namespace}/{repository}/settings/deploy-keys` 管理 deploy key；表单保留 antiforgery 保护，返回链接不经过已退役的旧仓库详情路由。

### Push gate

- owner 可按 `main`、`release/*` 等受控 glob 配置 direct push/Web merge 最低权限、force/delete 和管理员显式 bypass。
- owner 从 canonical `/{namespace}/{repository}/settings/branch-rules` 管理规则；同名仓库不会退回全局 legacy name 解析。
- `IGitPushGate` 是 HTTP、SSH、Web merge 和 Web branch delete 的唯一策略入口；拒绝和 bypass 写入独立 `GovernanceAuditEvents`，不混入 Feed。
- `GitProcessTransportBackend` 为 receive-pack 注入受控 `pre-receive` bridge。Git 仍负责 pack negotiation、quarantine、hook 时序和流式 I/O；bridge 只读取 Git 提供的有界 ref update 行，并用结构化 `git merge-base --is-ancestor` 判断 non-fast-forward。
- hook 子命令不启动 Web/SSH/scheduler，不输出异常堆栈，不把 PAT、Authorization header 或数据库连接写入日志/参数。

## Schema 与数据影响

SQLite、SQL Server 和 SonnetDB 新增以下 additive 表：

- `PersonalAccessTokens`
- `DeployKeys`
- `SshFingerprintClaims`
- `CredentialAuditEvents`
- `BranchProtectionRules`
- `GovernanceAuditEvents`

现有 Identity、`SshKeys`、repository 和 Git filesystem layout 不改写。三个 provider 均持有独立 `MachineCredentialsAndPushGate` migration；SonnetDB 路径只使用其已支持的 additive DDL，不依赖 alter-column 或表重建。

升级前必须备份数据库。migration down 会删除上述新表，因此会永久删除 PAT、deploy key、分支规则及其审计；生产回滚应优先恢复升级前一致备份，而不是在凭据已投入使用后直接降级 schema。

## 协议与兼容性

- 公开 Web/Git/LFS/SSH URL、Basic challenge、Git content type、protocol v2 和 repository path 不变。
- 新增的 deploy key 和 branch rule 管理 URL 是 additive owner-only Web 路由；旧 `/Repository/...` action 继续保留兼容。
- Identity 密码继续可用于现有 Git Basic；PAT 作为 password 时要求 username 与 token owner 一致。
- 没有保护规则时 push 行为不变；有匹配规则时拒绝由 Git remote sideband 返回，ref 不更新。
- webhook、status/check API、required checks、required approvals 和 CODEOWNERS 尚未实现；后续必须扩展现有 gate，不建立第二套判定。

## 测试说明

- `CredentialGovernanceServiceTests`：PAT hash/scope/revoke/audit，deploy key repository/write/revoke/fingerprint，force/delete/push/merge gate。
- `GitBasicAuthenticationHandlerTests`：Identity Basic、Git PAT、Bearer PAT 和错误 username 隔离。
- `GitReceiveHookTests`：ref 输入边界与安全拒绝文本。
- `GitSmartHttpIntegrationTests`：真实 Git 客户端 PAT read/write、protected receive-pack、ref 原子保持和 24 MiB pack 流式 push。
- `SshGitIntegrationTests`：真实内置 SSH clone/fetch/push 回归。
- `GitCandySonnetDbSmokeTests`：真实 SonnetDB `MigrateAsync` 和读写回归。
- `MvcPageSmokeTests`：canonical owner 入口、deploy key/branch rule antiforgery 表单、规则保存和 canonical redirect。

最终验证：

- `dotnet build GitCandy.slnx --no-restore`：通过，0 warning / 0 error。
- `dotnet test GitCandy.slnx --no-build --no-restore`：通过，`GitCandy.Data.Tests` 71/71，`GitCandy.Tests` 110/110。
- Playwright 浏览器 smoke：创建临时仓库；PAT 一次显示；canonical deploy key 页面拒绝无效 OpenSSH key 且不返回 400；新增 `main` owner-only 规则并验证 force/delete/bypass 均为 enforced；最终控制台 0 error / 0 warning。
- `git diff --check`：通过，仅输出工作区既有 LF/CRLF 转换提示。
