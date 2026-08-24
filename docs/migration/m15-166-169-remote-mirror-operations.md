# M15 #166-#169 Remote Mirror 运维闭环

## 对应 ROADMAP

- Milestone 15 / #166：持久化 job pipeline。
- Milestone 15 / #167：provider webhook 与仓库 owner 运维视图。
- Milestone 15 / #168：GitHub App、GitLab/Gitee OAuth/PAT 凭据生命周期与 provider 事件。
- Milestone 15 / #169：故障、并发、恢复、三 provider fixture 和双向 mirror go/no-go。

## 变更点

- 新增每个 mirror 唯一的 `RemoteMirrorJobs` durable work item。初始同步、周期 Pull、post-receive Push、provider webhook、手动同步和恢复触发通过 generation 合并，不因进程重启丢失。
- Quartz 只负责每 5 秒唤醒；worker 通过 optimistic concurrency lease 认领任务，同一 mirror 串行，实例并发默认 2。租约过期在下一次唤醒转为 `Recovery`，进程 graceful shutdown 会释放未完成租约；已请求取消的 lease 在 release/expiry 后保持取消态。
- 唤醒时会对没有 job 的初始 mirror，以及已保存 pending ref 但 job 仍为 completed 的 Push mirror 做恢复扫描，封闭数据库提交与 job 入队之间的进程退出窗口。
- 可重试网络、timeout、rate-limit、远端暂时不可用和进程启动失败使用有上限的指数退避与 jitter；认证、授权、仓库删除、配置错误和 divergence 保留分类错误并停止自动重试。
- `/{namespace}/{repository}/settings/mirrors` 只允许 repository owner，提供注册、状态、暂停/恢复、立即同步、取消、重试和删除。所有执行入口仍写 durable queue，不在 MVC request 内访问远端 Git。
- `/remote-events/{connectionId}/{provider}` 支持 GitHub `X-Hub-Signature-256`、GitLab `X-Gitlab-Token`、Gitee `X-Gitee-Token`，限制 1 MiB、使用 API write rate limit，并按 `(ConnectionId, DeliveryId)` 去重。
- webhook secret 只保存 `env:`/`config:` reference。收据只保存 delivery ID、事件类型、SHA-256 payload hash 和接收时间，不保存签名、token 或 payload。
- provider event 通过 stable repository ID 更新 rename profile。远程 delete 会禁用 mirror、取消 pending job 并记录 `remote_repository_not_found`；Pull webhook 只做低延迟入队，周期 schedule 继续负责漏事件对账。
- GitHub connector 接受有明确到期时间的 App installation token；GitLab/Gitee 支持 OAuth access token 和 PAT。连接页显示到期元数据、支持原位轮换，GitHub 403 rate-limit header 与 429 都分类为 `rate_limited` 并携带 retry time。
- 同一 local repository、connection、stable remote repository 只允许一个方向；数据库唯一索引和注册校验共同拒绝同时创建 Pull 与 Push，M15 双向 mirror 的 go/no-go 结论为不开放。

## Schema 与配置

SQLite、SQL Server、SonnetDB 新增：

- `M15RemoteMirrorJobs`：`RemoteMirrorJobs`、generation、state、lease、attempt、available time、cancel、分类错误和并发 token；同时收紧 target 唯一索引以禁止双向 mirror。
- `M15RemoteProviderLifecycle`：连接 credential expiry/webhook secret reference，以及 `RemoteProviderEvents` 去重收据。

`GitCandy:Remotes:Jobs` 默认值：

```json
{
  "MaxConcurrentJobs": 2,
  "DispatchBatchSize": 10,
  "MaxAttempts": 5,
  "LeaseDuration": "00:35:00",
  "InitialRetryDelay": "00:00:15",
  "MaximumRetryDelay": "00:15:00",
  "RetryJitterRatio": 0.2
}
```

`LeaseDuration` 应大于单次 Git `OperationTimeout`。提高并发前必须同时评估 CPU、磁盘、网络和 Git HTTP/SSH 延迟。

## 安全与兼容性

- 公开 repository、Git HTTP、SSH 和 LFS URL 不变；新增的是 owner 设置页和 provider callback。
- provider token 仍只通过 Data Protection vault 和一次性 credential helper 使用，不进入 EF、URL、参数、日志或 MVC 输出。
- webhook callback 不接受 cookie/PAT/Git Basic 代替 provider signature；无 secret reference 返回 not found，reference 无法解析返回 503，验签失败返回 401。
- M15 只同步 commits、branches 和 tags。LFS、Issues、PR/MR、Wiki、Release、CI 和 Packages 不隐式同步。

## 迁移、回滚与恢复

1. 升级前停止写入并一致备份数据库、repositories、Data Protection keys 和生产 secret 配置。
2. 检查同一 `(RepositoryId, ConnectionId, RemoteRepositoryId)` 是否已有 Pull/Push 两条记录；新唯一索引不含 `Direction`，必须先选择保留一个方向，否则 migration 会明确失败而不猜测数据取舍。
3. 审阅 SQL Server idempotent migration SQL，再先以默认并发 2 启动单实例。
4. 监控 failed/leased job、远端限流和 Git HTTP/SSH 延迟；确认 callback secret reference 在运行账户下可解析。
5. 二进制回滚前停止 scheduler 和 push 写入，恢复升级前数据库快照与同一时间点 repositories/key ring。仅 down migration 会删除 pending/retry/receipt 状态。
6. crash 后无需操作 Quartz 表；下一次 durable queue 扫描会恢复到期 lease 和未衔接 job 的 pending mirror/ref。人工只对分类为 permanent 的 job 执行修复后 retry。

## 验收

- `RemoteMirrorJobQueueTests`：generation 合并、双 worker 单租约、过期恢复、提交后漏入队恢复扫描、取消态保持和退避窗口。
- `RemoteMirrorJobDispatcherTests`：并发 wakeup 下实例并发上限。
- `RemoteProviderEventTests`：三 provider 验签、rename、去重、delete、取消，以及业务动作失败时不提前消费 delivery receipt。
- `RemoteRepositoryProviderTests` / `RemoteConnectionServiceTests`：App、OAuth/PAT、rate limit、expiry 和轮换。
- `RemoteMirrorSchemaTests`：双向 mirror 数据库拒绝；`GitCandySqlServerMigrationTests` 和 SonnetDB smoke 验证 provider schema。
- 现有 `RemoteMirrorServiceTests` 保留 ref filter、tag divergence、force、prune、1024 ref batch、凭据撤销和 pending generation 回归。

2026-08-24 在 Windows / .NET SDK 10.0.301 上执行结果：

- `dotnet build GitCandy.slnx --configuration Release --no-restore`：成功，0 warning、0 error；帮助站点与前端资产同时完成生成。
- `GitCandy.Data.Tests` 排除已知 SonnetDB smoke 后 115/115 通过；其中 SQLite migration/read/write 和 SQL Server migration SQL 通过。
- M15 Remote 专项 29/29 通过；帮助文档与 Git Smart HTTP/SSH clone、fetch、push 专项 10/10 通过。
- `GitCandy.Tests` 全量 174/176 通过；两个失败来自 M13 已存在但缺少 `Views/Release/Index.cshtml`、`Views/Release/Create.cshtml`，与 M15 controller、route 和 mirror 代码无调用关系。
- SonnetDB 专项 migration 已应用，但当前用户选择的 `external/SonnetDB` `a7fae42` 移除了旧 client-side integer value generator，而历史 GitCandy SonnetDB initial migration 没有为 `Repositories.Id` 写入 `AUTO_INCREMENT`；因此 repository insert smoke 仍失败为 `Repositories.Id` 不允许 NULL。修复需要单独完成 SonnetDB provider/历史 schema 兼容任务，不能在 M15 中回退用户子模块或改写生产历史 migration。
- 普通 restore 当前还会因 `AngleSharp` `NU1902`，以及 SonnetDB `10.0.10` 与根目录 central package `10.0.9` 的 `NU1109` 失败；依赖审计与 .NET servicing 版本统一应作为独立依赖升级处理。
- 隔离 SQLite host 已走通注册、remote account、凭据轮换 action、Pull mirror 注册和 durable job 分类失败展示；桌面 1440px 与移动 390x844 Edge 检查无页面横向溢出、控件遮挡或 console error/warning，mirror 表格保留自身可滚动操作区。

真实 GitHub App installation、GitLab/Gitee OAuth application、webhook 注册、scope、限流窗口、仓库删除和网络策略必须在每个部署环境使用非测试凭据 smoke；仓库 fixture 不包含生产 secret，不能替代该部署验收。
