# M12 #134 Reviewer 与 Review 状态

## 变更点

- Pull Request 的 author、可空 assignee 和 reviewer request 分开存储；assignee 不隐式获得 reviewer 状态。
- PR 作者或仓库 owner 可以请求、重新请求具备仓库读取权限的成员 review。
- 被请求的 reviewer 可以提交 `Commented`、`Approved` 或 `ChangesRequested` review；review Markdown 复用受限渲染和 HTML allow-list 清理。
- review 保存提交时的 current head SHA 和 reviewer request version。新提交不会改写 review 历史，页面会显式显示 stale 状态。
- 仓库 owner 可以填写原因 dismiss review；原决定、提交 SHA、dismiss 操作者和时间继续保留供审计。
- `GitCandy:Application:AllowAuthorApproval` 默认 `false`；`GitCandy:Application:DismissStalePullRequestApprovals` 默认 `true`。

## Schema 与数据影响

SQLite migration `20260711150529_PullRequestReviewStatus` 和 SQL Server migration `20260711150552_PullRequestReviewStatus`：

- 为 `PullRequests` 增加可空 `AssigneeUserId` 及 Identity 外键和索引。
- 新增 `PullRequestReviewers`，以 `(PullRequestId, ReviewerUserId)` 为主键，保存 request/re-request 操作者、时间和乐观并发版本。
- 新增 `PullRequestReviews`，保存 reviewer、decision、Markdown/HTML、head SHA、request version、dismiss 信息和乐观并发版本。
- 删除 PR 会级联删除 reviewer request 和 review；删除 assignee 用户会把 PR 置为 unassigned。reviewer/request/dismiss 审计用户使用限制删除或 no-action，避免静默破坏历史。
- 现有 Pull Request 不需要回填，升级后保持 unassigned 且无 reviewer/review 历史。

该变更不修改 Identity schema、公开 Git/Web URL、仓库存储布局或 Git HTTP/SSH wire protocol。

## 权限与策略

- 读取 review 内容继续经过仓库 read authorization，私有仓库返回 not found，不暴露 PR 是否存在。
- 只有 PR 作者或仓库 owner 能设置 assignee、请求和重新请求 reviewer。
- reviewer 必须仍具备仓库读取权限且已被请求，才能提交 review。
- 只有仓库 owner 能 dismiss review，且必须填写原因。
- 默认禁止作者批准本人 PR。允许后，自批仍作为独立 review 保存，后续 required approvals 是否采信由 M13 branch policy 决定。
- 默认 stale approve 不再是有效批准；关闭该策略时 stale 标记仍显示，但批准可继续计入后续 mergeability 汇总。

## 升级与回滚

升级前同时备份数据库和当前应用版本。应用启动 migration 或 `--migrate` 会应用对应 provider migration。SQLite 与 SQL Server schema 已生成验证；PostgreSQL/SonnetDB provider 仍按路线图单独回补。

EF migration 的 `Down` 会删除 reviewer request/review 历史和 assignee 字段，因此生产回滚不要依赖降级 migration。回滚时停止新版本，恢复升级前数据库备份，再部署上一应用版本。仓库目录和 Git refs 无需恢复。

## 验证

- migration-backed SQLite service tests 覆盖 assignee/reviewer 分离、未请求 reviewer 拒绝、request/re-request、approve、request changes、dismiss、Markdown 清理和 stale approval。
- 策略测试覆盖默认禁止作者自批，以及显式允许作者自批并保留 stale approval。
- Kestrel MVC integration test 覆盖 author 请求独立 reviewer、reviewer 登录并批准，以及 review 结果显示。
- SQL Server migration script 可离线生成并包含新增表、索引和外键。
