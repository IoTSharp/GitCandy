# M14 #151-#159 团队治理与企业身份联邦验收

日期：2026-07-14

## 对应 ROADMAP

- Milestone 14 / `#151-#159`：统一团队授权与 UI、企业连接、Microsoft Entra ID、SCIM 2.0、企业微信、飞书、钉钉、目录对账、同步停用和安全总验收。
- `#150` 的四级角色 schema、旧角色回填和基础权限矩阵见 [M14 #150 记录](m14-150-team-governance-roles.md)。

## 变更点

### 团队治理

- `ITeamAuthorizationService` 按具体 `TeamPermission` 统一判断团队成员、仓库、改名、删除和企业连接操作，Controller 仍执行服务端复核。
- 成员变更支持最多 100 项的原子批处理，并写入团队治理审计；UI 可管理 `TeamOwner`、`Leader`、`DeputyLeader` 和 `Member` 四级角色。
- 任何人工操作、SCIM 或目录对账都不能移除、降级或停用最后一位 TeamOwner，也必须保留至少一位非企业托管的本地 break-glass TeamOwner。

### 企业连接与登录

- 企业连接保存 provider、稳定组织 ID、公开 endpoint/client 配置、能力开关、同步游标和诊断状态。数据库只保存 `env:NAME` 或 `config:Configuration:Path` 引用，不保存或回显 secret 值。
- Microsoft Entra ID 登录使用独立动态流程，不修改全局 OIDC scheme；state 由 Data Protection 保护，配合 HttpOnly correlation cookie、PKCE、nonce、discovery/JWKS、issuer/audience/tenant 校验。
- Entra 身份只按稳定 `oid`，在缺失时按 `sub` 绑定；邮箱冲突不会自动 link。JIT 同时要求应用允许注册和连接非敏感配置 `{"allowJit":true}`。
- 企业微信按 `userid`，飞书优先按租户级 `user_id`，钉钉优先按 `unionId` 建立稳定映射。provider access token 只在单次操作内存在，每次从当前 secret reference 重新兑换。

### SCIM、目录同步与停用

- `/scim/v2/{connectionId}` 使用独立 SCIM Bearer authentication scheme，支持 Users/Groups create、query、PATCH、filter、分页、`active` 生命周期和幂等 `externalId`。bearer 明文只在轮换时显示一次，数据库仅保存 SHA-256 hash 与非敏感 prefix。
- Quartz 每 15 分钟对已启用的目录连接执行有界同步：单连接最多 200 页、50,000 用户，持久化游标，重启可恢复，一个连接失败不会阻止其他连接。
- 只有从空游标开始且完整结束的扫描才停用缺失用户并覆盖 group membership；部分游标恢复不会用不完整集合改写 group。
- 停用会锁定 Identity、刷新 security stamp、移除团队成员关系、撤销 PAT，并删除 SSH key/fingerprint claim。最后 owner 和本地 break-glass owner 保持有效，连接状态标为 degraded 供管理员处理。

### Provider event 安全

- `/enterprise-events/{connectionId}/{provider}` 只接受飞书和钉钉事件，使用独立 `WebhookSecretReference`，校验 provider 签名与 5 分钟时间窗。
- 请求限制为 1 MiB，并使用写入 rate limit；事件 ID 和 payload hash 持久化去重。HTTP 请求只记录收据，实际目录读取继续由 Quartz 执行。
- 命名 HttpClient 的 framework logging 固定为 `Warning`，避免企业微信 token endpoint query 中必需的 `corpsecret` 被 URI 日志记录。

## 数据库影响

SQLite、SQL Server 和 SonnetDB 均增加以下 additive migrations：

- `M14TeamAuthorization`
- `M14EnterpriseConnections`
- `M14ScimProvisioning`
- `M14ProviderEventDeduplication`
- `M14EnterpriseSecurityAcceptance`

它们在 `M14TeamGovernanceRoles` 之后增加团队审计、企业连接、外部身份、SCIM credential/group、provider event 和 webhook secret reference。没有删除旧表或自动导入旧 MVC5 账号；三个 provider 均确认最后 migration 与当前 EF model 一致。

## 测试说明

在 Windows / .NET SDK 10.0.301 上使用隔离 artifacts 路径执行：

```powershell
dotnet build GitCandy.slnx --configuration Release -p:UseArtifactsOutput=true -p:ArtifactsPath=.artifacts/m14-verified
dotnet test GitCandy.slnx --configuration Release --no-build --no-restore -p:UseArtifactsOutput=true -p:ArtifactsPath=.artifacts/m14-verified
```

结果：Release 构建为 0 warning、0 error；`GitCandy.Data.Tests` 91/91、`GitCandy.Tests` 139/139，合计 230/230 通过，无失败或跳过。Entra state/JWKS/nonce、SCIM/deprovision/reconciliation、国内 provider 与签名/重放安全均有专项 fixture 覆盖。

另以隔离 SQLite、Data Protection keys、repository/cache 路径和 `http://127.0.0.1:4311` 启动最终 Release artifact，真实浏览器完成账号登录、团队创建、四级角色下拉、最后 TeamOwner 降级拒绝、no-op 审计、企业连接创建、disabled 状态、secret reference 和 SCIM bearer 一次显示验证。1440x900 与 390x844 页面均可用，浏览器控制台为 0 error、0 warning；截图保存在忽略提交的 `output/playwright/` 验收目录。

未使用真实 Entra、企业微信、飞书或钉钉 tenant credential。真实 consent、scope、callback URL、事件订阅和企业网络连通性属于部署环境 smoke，不能由本地 fixture 替代。本切片不改变 Git Smart HTTP/SSH backend，因此未重复执行 clone/fetch/push。

## 兼容、部署与回滚

- 公开新增路由为 `/EnterpriseLogin/Callback`、`/scim/v2/{connectionId}` 和 `/enterprise-events/{connectionId}/{provider}`；既有 Web OIDC、Identity cookie、Git Basic 和 SSH scheme 保持独立。
- 升级前必须一致备份数据库和 Data Protection keys。部署后先保持连接 disabled，配置 secret store、上游 callback/SCIM/event URL，再逐个测试并启用。
- 功能回滚优先禁用连接的 login/provisioning/event 配置并保留映射和审计。若旧二进制不理解 M14 schema，停止服务并恢复升级前数据库快照，不要让旧版本连接新 schema。
