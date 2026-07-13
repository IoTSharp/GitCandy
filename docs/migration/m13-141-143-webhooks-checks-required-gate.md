# M13 #141/#142/#143 Webhook、Check 与 Required Gate

日期：2026-07-13

## 对应 ROADMAP

- `#141`：versioned webhook delivery。
- `#142`：commit status 与 check API。
- `#143` required-check 切片：把 exact-SHA check 状态接回既有 `IGitPushGate`。
- CODEOWNERS 与 required approvals 仍属于后续 `#144`，本切片不伪造 review owner 状态。

## Webhook 契约

- owner 在 `/{namespace}/{repository}/settings/webhooks` 创建、暂停、诊断和 replay subscription；canonical route 不退回全局 legacy repository name 解析。
- signing secret 使用 256-bit 随机值，只在创建响应显示一次；数据库保存 ASP.NET Core Data Protection ciphertext，不记录或回显 secret。
- `IntegrationEvents` 保存 JSON envelope v1；成功 push 与成功 Web merge 只写持久化 outbox，delivery 失败不会回滚已提交的 Git ref 或 PR merge。
- POST request 包含 `X-GitCandy-Event`、`X-GitCandy-Delivery`、`X-GitCandy-Webhook-Version: 1` 和 `X-GitCandy-Signature-256: sha256=...`。
- Quartz job 使用 delivery lease、request timeout 和有界退避；默认最多 6 次。人工 replay 创建新 delivery ID，并保留 `ReplayOfDeliveryId`。
- 默认仅允许公网 HTTPS。保存 URL 和 socket `ConnectCallback` 都重新解析并拒绝 loopback、link-local、私网、保留/文档地址和混合 DNS 结果；redirect 与 proxy 默认关闭，响应体只做有界 drain 且不持久化。

## Status 与 Check API

API 使用 `Authorization: Bearer <PAT>`，不接受 Identity cookie 代替机器凭据：

```text
POST /api/v1/repositories/{namespace}/{repository}/commits/{sha}/statuses
POST /api/v1/repositories/{namespace}/{repository}/commits/{sha}/checks
GET  /api/v1/repositories/{namespace}/{repository}/commits/{sha}/checks
```

- GET 要求 `api:read` 与 repository read；POST 要求 `api:write` 与 repository write。
- status 以 `(repository, SHA, status, context)` 幂等 upsert；check 以 `(repository, SHA, check, name)` 幂等 upsert。
- write endpoint 按 PAT credential ID 固定窗口限流；超限返回 429。
- SHA 必须是仓库中真实 commit；未知 SHA 返回 404。target/details URL 复用 webhook 出站边界，非法或被阻止的 URL 返回 422。
- API response 使用稳定的 `status|check` kind 和小写状态字符串，不直接暴露 EF entity。

## Required Check Gate

- branch rule 通过独立 `BranchProtectionRequiredChecks` 子表保存最多 20 个受控 context，不对 SonnetDB 执行 alter-column。
- push 与 Web merge 只查询本次目标 SHA 的最新同名 status/check；旧 head 的 success 不会满足新 head。
- required context 缺失、pending、running、failure、error 或 cancelled 都拒绝；当前仅 `success` 放行。
- force/delete、最低 push/merge 权限和管理员显式 bypass 的既有顺序不变；拒绝/bypass 继续写 `GovernanceAuditEvents`。

## Schema、迁移与回滚

SQLite、SQL Server 和 SonnetDB 的 additive `WebhookAndChecks` migration 新增：

- `IntegrationEvents`
- `WebhookSubscriptions`
- `WebhookDeliveries`
- `CommitChecks`
- `BranchProtectionRequiredChecks`

升级前必须一致备份数据库与 Data Protection key ring。migration down 会永久删除 webhook subscription、delivery/outbox、status/check 和 required context；生产回滚优先恢复升级前备份。Git repository filesystem、公开 Git URL、Identity schema 和现有 branch rule 行不改写。

## 验证入口

- `CredentialGovernanceServiceTests`：secret ciphertext、versioned outbox、lease/retry/replay、SHA/context upsert、missing/pending/success gate 与旧 head 隔离。
- `WebhookIntegrationTests`：默认 SSRF 阻断、HMAC header/payload，以及允许测试 loopback 时的真实 Kestrel socket delivery。
- `GitSmartHttpIntegrationTests`：PAT scope、status/check API、blocked target、未知 SHA、429、真实 receive-pack required-check 放行/拒绝和 push integration event。
- `MvcPageSmokeTests`：canonical webhook/branch rule owner UI、antiforgery、one-time secret 和 required context 表单。
- `GitCandySqlServerMigrationTests` 与 `GitCandySonnetDbSmokeTests`：SQL 生成和真实 SonnetDB migration/read-write。

## 最终验证

- `dotnet build GitCandy.slnx --no-restore`：通过，0 warning / 0 error。
- `dotnet test tests/GitCandy.Data.Tests/GitCandy.Data.Tests.csproj --no-build --no-restore`：73/73 通过。
- `dotnet test tests/GitCandy.Tests/GitCandy.Tests.csproj --no-build --no-restore`：115/115 通过。
- SQLite、SQL Server、SonnetDB migration 均无 pending model changes。
- 真实 PAT API、真实 receive-pack required-check gate、真实 Kestrel webhook receiver 与 HMAC 验签均通过。
- Playwright 浏览器验收创建了 `release/*` 规则并显示 `ci/build, security/scan`；webhook 与 branch-rules 页面均为 0 error / 0 warning，创建后的 webhook secret 不在列表页回显。
