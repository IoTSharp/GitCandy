# M13 #145-#149 通知、审计、Release、Search 与外部 CI 验收

日期：2026-07-13

## 对应 ROADMAP

- `#145`：在 M12.7 统一 inbox 上扩展 PR/review/check/release、偏好、邮件/webhook outbox 和失败诊断。
- `#146`：保持 Activity/Feed 与不可变安全审计分离，提供 repository owner 查询入口。
- `#147`：tag-backed Release、Markdown、受限附件、权限、路径/大小边界和孤儿清理。
- `#148`：repository/Issue/PR/commit/code 搜索，先过滤 repository 权限再查询内容。
- `#149`：真实外部 CI fixture 完成 webhook -> check -> required gate，并覆盖 retry、撤销和私有数据。

## 通知与外部投递

- `Notifications` 仍是唯一 inbox；新增 `Issue/PullRequest/Review/Check/Release` 稳定事件类型，不建立第二套收件箱。
- `/notifications/preferences` 按事件配置 email 或 webhook。站内通知始终保留；webhook secret 使用 Data Protection 加密保存且不回显。
- `NotificationDeliveries` 使用 pending/in-progress/succeeded/failed、lease、attempt、bounded backoff 和诊断错误码。SMTP 未配置、HTTP 失败、timeout 和 secret 解密失败均可诊断。
- claim delivery 时重新检查 administrator/user/team repository read 权限；撤权后的工作项失败为 `permission_revoked`，不会发送原 payload。
- PR reviewer request、review/merge/close/reopen timeline、当前 PR head 的 check 更新和已发布 Release 通过现有 workspace 投影进入 inbox；Issue 行为保持不变。

## 审计

- Feed 继续使用 `ActivityEvents`；安全证据继续写独立 `GovernanceAuditEvents` / `CredentialAuditEvents`，普通用户没有修改或删除入口。
- owner 可访问 `/{namespace}/{repository}/settings/audit`，统一查看规则、bypass、gate reject、force/delete decision、webhook、check、PAT/deploy key 和 Release asset 事件。
- check 更新写 action/context/state，不记录 authorization header、PAT、webhook secret 或物理路径；Release audit 只记录 tag、文件名、长度和 SHA-256。

## Release 与附件

- `/{namespace}/{repository}/releases` 只允许为仓库中真实 tag 创建 Release；annotated/lightweight tag 最终解析到 commit SHA。
- Markdown 复用 Issue/PR 的禁用 raw HTML 与 allow-list sanitizer。draft 仅 repository writer 可见。
- 附件写入 `CachePath/release-assets/{repositoryId}/{releaseId}/{assetId}`；asset ID 为服务器生成的 32 位 hex，上传文件名不参与路径解析。
- 写入全程流式，超过单文件/Release 总量立即失败；成功文件包含 SHA-256、长度、content type 和下载计数，下载支持 range。
- 数据库提交失败会立即删除文件；中断产生的 temp/orphan 由每小时 job 按 `OrphanRetention` 清理。

## Search

- `/search` 支持 repository、Issue、Pull Request、commit 和 code scope。
- Data 查询先构造当前用户可读 repository 集合；Issue/PR join 以及交给 Git 层的候选均来自该集合，匿名查询只含 public + anonymous-read。
- Git 搜索最多扫描 20 个已授权仓库、每仓库 200 个 commit、默认分支 500 个文件，单文件最多 256 KiB；binary 跳过，结果最多 100 条。
- 搜索不进入 Git HTTP/SSH transport 热路径，不运行 shell，不建立第二套索引或泄漏不存在/无权限的私有仓库。

## 外部 CI 总验收

真实测试链路为：

1. Kestrel CI fixture 接收 `push` envelope v1 并校验 `X-GitCandy-Signature-256`。
2. 第一次返回 503；持久化 delivery 重新变为 due 后第二次投递成功。
3. CI 使用仅 `api:write` 的 PAT 对 webhook 中 `ci-candidate` 精确 SHA 回写 `ci/external=success`。
4. `main` 的 required check 只接受该 SHA，真实 `git push` 放行。
5. PAT 撤销后 API 返回 401；新 commit 没有 check，真实 receive-pack 以 missing required check 拒绝且 main 不更新。
6. 既有 `CredentialGovernanceServiceTests` 同时覆盖 required review/CODEOWNERS/stale head、delivery lease/replay 和 gate audit；新增协作测试覆盖私有 search 与通知撤权。

## Schema、迁移与回滚

SQLite、SQL Server、SonnetDB 的 additive `M13CollaborationExtensions` migration 新增：

- `Notifications.EventType`，旧数据回填 `Issue`；
- `NotificationPreferences`、`NotificationDeliveries`；
- `Releases`、`ReleaseAssets`。

升级前必须一致备份数据库和 `CachePath/release-assets`。migration down 会删除偏好、delivery 诊断、Release metadata 和 asset metadata，但不会删除物理附件；生产回滚必须恢复升级前数据库/cache 备份并使用上一版本应用。Identity schema、公开 Git URL、repository path 和 Git wire protocol 不变。

## 验证入口

- `CollaborationExtensionServiceTests`：review 通知外部投递、撤权 fail-closed、审计、private search、Release/asset/audit。
- `ReleaseAssetStoreTests`：cache root、流式大小边界、读取与 orphan cleanup。
- `RepositoryBrowserServiceTests.GitContentSearch_*`：真实 Git commit/code 搜索。
- `GitSmartHttpIntegrationTests.ExternalCi_*`：真实 webhook retry、HMAC、PAT check、required gate 和撤销。
- `CredentialGovernanceServiceTests`、`CodeOwnersTests`、`PullRequestGitRepositoryTests`：required review/check、CODEOWNERS、并发与审计回归。
- `GitCandySqlServerMigrationTests`、`GitCandySonnetDbSmokeTests`：provider schema 与 migration 验证。

## 最终验证

- `dotnet build GitCandy.slnx --configuration Release --artifacts-path .artifacts/build-m13-release`：通过，0 warning / 0 error。
- `GitCandy.Data.Tests`：77/77 通过。
- `GitCandy.Tests`：124/124 通过。
- 合计 201/201 通过，无跳过、无失败。
- SQLite、SQL Server、SonnetDB `migrations has-pending-model-changes`：三者均无 pending model changes。
- Playwright 在 1440x900 与 390x844 验证 `/search`，并验证匿名访问 `/notifications/preferences` 跳转登录；console 0 error / 0 warning，截图保存在 `output/playwright/m13-search-*.png`。
- 默认 Debug/Release 输出由工作区既有 GitCandy 进程占用，因此使用隔离 artifacts path；未停止或改写用户进程。

## 兼容性与剩余风险

- 新表/列和 `CachePath/release-assets` 是部署可见变更，已更新 CHANGES 与 deployment；回滚方式见上节。
- PostgreSQL provider 仍没有仓库内 migration baseline，本切片不引入不可审查的全量初始 migration。
- 当前 code search 是请求内有界扫描，不是大规模索引；M16 hybrid search 会独立实现，不能让索引任务进入 Git transport 热路径。
