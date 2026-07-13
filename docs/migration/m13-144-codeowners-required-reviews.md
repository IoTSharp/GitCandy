# M13 #144 CODEOWNERS 与 Required Review

日期：2026-07-13

## 对应 ROADMAP

- `#144`：受控解析 CODEOWNERS，按 PR merge-base changed paths 解析 owner，并把最少批准、code owner review 和 stale approval 接入既有 `IGitPushGate`。
- 本切片不新增第二套 merge 判定；Git HTTP、内置 SSH、OpenSSH 和 Web merge 继续复用同一个 branch protection gate。

## CODEOWNERS 契约

- 只从目标 head 按 `.github/CODEOWNERS`、根目录 `CODEOWNERS`、`docs/CODEOWNERS` 的顺序读取第一份普通 UTF-8 文件；symlink、非 Blob、无效 UTF-8 和超限内容均拒绝。
- 支持 `*`、`**`、`?`、root-relative `/`、目录尾 `/`、escaped space/`#` 和 last matching rule wins；不支持 negation `!`、字符类 `[]` 和 email owner。
- owner token 只接受当前 GitCandy 语义明确的 `@user` 或 `@team`。团队没有 GitHub 式组织/子团队层级，因此不接受带 `/` 的 owner token。
- 固定边界为 256 KiB、2048 条规则、4096 字符单行、64 owners/rule、10000 changed paths 和 250000 次 rule/path 匹配；glob 使用 non-backtracking 引擎。
- changed paths 基于 merge-base 到 PR head 的 tree changes；rename 同时检查 old/new path，避免通过改名绕过原路径 owner。

## Required Review Gate

- branch rule 新增 `RequiredApprovals`、`RequireCodeOwnerReviews` 和 `DismissStaleApprovals`。匹配多个规则时取最高批准数，任一规则要求 code owner 即启用，stale 策略只能比应用级基线更严格。
- 应用级 `RequiredPullRequestApprovals` 继续作为 Web merge 最低基线，但现在由统一 gate 最终复核；PR 页面使用同一 gate 的无审计预览显示 approvals、required checks、CODEOWNERS 和治理阻塞。
- code owner 必须是仓库可写用户，或拥有该仓库写权限的团队成员。每个 changed path 最后命中的 owner 集合至少需要其中一名成员对当前 head 的有效批准。
- CODEOWNERS 缺失、语法无效、owner 没有仓库写权限、批准来自错误 owner、批准已 dismiss 或 head 更新后 stale，都会返回不含私有物理路径/secret 的可解释 blocker。
- 配置 required review 的保护分支拒绝直接 push，并明确要求通过已批准 PR；没有 required-review 规则时既有直接 push 行为不变。
- force/delete/access 先判定并短路。无权限操作者不会继续得到 check/review 状态；真正 Web merge 在写 ref 前重新读取 CODEOWNERS/changed paths 并记录拒绝或 bypass 审计。

## Schema、迁移与回滚

SQLite、SQL Server 和 SonnetDB 的 additive `RequiredReviewsAndCodeOwners` migration 为 `BranchProtectionRules` 增加：

- `RequiredApprovals`，旧规则默认 `0`；
- `RequireCodeOwnerReviews`，旧规则默认 `false`；
- `DismissStaleApprovals`，旧规则默认 `true`，保持既有 stale approval 行为。

升级前必须一致备份数据库和 repository filesystem。migration down 只删除这三个规则字段；回滚会失去 required-review 配置，但不会改写 PR review 历史、Git refs、CODEOWNERS 文件、公开 URL 或 Identity schema。PostgreSQL provider 仍没有仓库内 migration baseline，本切片不引入不可审查的全量初始 migration。

## 验证入口

- `CodeOwnersTests`：root/recursive glob、last match、escaped literal、非法 pattern/owner。
- `PullRequestGitRepositoryTests`：真实 bare repository 的目标 head CODEOWNERS 和 merge-base changed paths。
- `CredentialGovernanceServiceTests`：最少批准、changed-path owner、错误 owner、stale head、直接 push 和审计短路。
- `PullRequestServiceTests`：既有 approval-to-merge 流程现在通过 branch protection merge hook 和统一 gate 复核。
- `MvcPageSmokeTests`：canonical owner-only branch rule 表单保存并显示 required approvals、code owner 和 stale policy。
- `GitCandySqlServerMigrationTests`、`GitCandySonnetDbSmokeTests`：SQL schema 与真实 SonnetDB migration/read-write。

## 最终验证

- `dotnet build GitCandy.slnx --artifacts-path D:\source\GitCandy\.artifacts\m13-144`：通过，0 warning / 0 error。
- `dotnet test tests/GitCandy.Data.Tests/GitCandy.Data.Tests.csproj --artifacts-path D:\source\GitCandy\.artifacts\m13-144 --no-build --no-restore`：74/74 通过。
- `dotnet test tests/GitCandy.Tests/GitCandy.Tests.csproj --artifacts-path D:\source\GitCandy\.artifacts\m13-144 --no-build --no-restore`：119/119 通过。
- 默认 Debug/Release 输出目录被工作区既有 GitCandy 进程占用，因此本次使用隔离 artifacts path；没有停止或改写用户进程。

## 剩余风险

- 当前 owner namespace 只支持全局 `@user`/`@team`；M14 若引入组织层级，必须独立扩展 token 消歧、团队可见性和同步撤权测试。
- `#145` 才会把 inferred code owner/review/check 事件接入统一通知与外部投递；本切片只负责 merge 治理，不新建通知模型。
- `#149` 仍需用外部 CI fixture 完成 webhook -> check -> required review/check -> gate 总验收，并覆盖撤销、重试和并发。
