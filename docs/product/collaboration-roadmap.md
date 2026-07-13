# GitCandy 代码浏览与研发协作路线图

评估日期：2026-07-11

对应活动路线图：M13。M9 `#101/#106-#109`、M10、M11、M12 已完成，验收入口见 `docs/roadmap/completed-milestones.md`。

本文比较 GitHub、GitLab、Gitea/Forgejo 和 Gitee 的代码浏览、Issue、Pull Request / Merge Request、代码评审和合并治理能力，并记录 GitCandy 的取舍。M9-M12 内容是已落地设计基线；M13 合并治理与外部集成仍是活动规划。

## 1. 结论

GitCandy 眼下最值得补的不是内置 CI、Packages 或复杂项目管理，而是以下连续闭环：

1. **代码工作区已完成**：真实仓库创建/导入、tree、blob、raw、commit、diff、blame、compare，以及可引用的代码行和代码片段。
2. **Issue 已完成**：仓库内问题跟踪、评论、Markdown 代码块、标签、里程碑、负责人、引用和 Issue 通知。
3. **Pull Request 已完成**：同仓库和跨 fork PR、draft、commits、files changed、行内评论、review、approval、冲突判断和 merge/squash。
4. **当前待补合并治理与集成**：PAT、branch protection、CODEOWNERS、commit status/check、webhook、通知事件扩展、审计和 release。

这条顺序有明确依赖：PR 的 Files changed、行内评论和冲突判断依赖稳定的 commit/diff 读取模型；required checks 依赖 status/check API；跨 fork PR 又依赖稳定 namespace 和 fork 生命周期。Issue 可以在代码工作区完成后独立推进，但应复用后续 PR 的讨论、引用、Markdown 和通知基础。

## 2. 竞品能力矩阵

| 能力 | GitHub | GitLab | Gitea / Forgejo | Gitee | GitCandy 取舍 |
| --- | --- | --- | --- | --- | --- |
| 代码浏览 | tree/blob/raw、commit、compare、permalink、行号引用 | repository/files、commit、compare、blame，与 MR 紧密联动 | 轻量 tree/blob/commit/diff，部署成本低 | 代码浏览、提交和 PR 文件改动 | M9 已落地代码工作区、固定链接和大文件边界 |
| Issue | 灵活 Issue、子 Issue/依赖、labels、milestones、assignee、订阅、模板 | Issue/work item、labels、milestones、due date、thread、boards/epics | Issue/PR 共用 labels、templates、自动引用，轻量直接 | Issue、负责人/协作者、标签、里程碑 | M11 已落地仓库级核心；board/epic/iteration 仍非当前范围 |
| PR / MR | Conversation、Commits、Checks、Files changed、draft、临时 refs | description、commits、diff、pipeline、mergeability、assignee/reviewer | fork/branch PR、WIP、review、merge，适合轻量自托管 | fork PR、draft、代码评论、squash | M12 已落地同仓库和跨 fork PR/review/merge；checks 在 M13 接入 |
| 代码评审 | 行内 thread、approve/request changes、suggestion、CODEOWNERS | 行内 discussion、reviewer、approval rules、CODEOWNERS | comment/request changes/approve | 行评论、站内通知、按文件“已阅” | 第一版必须有稳定 diff anchor、过期评论、approve/request changes；suggestion 和已阅可后补 |
| 合并方式 | merge/squash/rebase、rulesets、merge queue | merge/squash/rebase、approval、merge train | merge/rebase/squash | merge/squash、质量门禁 | 第一版 merge+squash；rebase、merge queue/train 延后 |
| 质量门禁 | required reviews/checks、rulesets、branch protection | required approvals、pipelines、policies | protected branches、status checks、Actions | PR 质量门禁和流水线 | 先提供外部 CI 可写入的 status/check 和 push/merge 门禁，不先造 runner |
| 自动化 | webhooks、Apps、Actions、REST/GraphQL | webhooks、integrations、CI/CD、API | webhooks、Actions、API | WebHook、Gitee Go、开放 API | M13 做 versioned webhook + REST API；内置 runner 独立评估 |
| 项目管理 | Projects、issue types、sub-issues | boards、epics、iterations、work items | milestones、projects | 里程碑与企业项目协同 | 只保留 labels/milestones/assignee；高级规划不是当前阻塞项 |

### 平台启示

- **GitHub** 最值得学习的是 PR 的四个核心视图、draft、checks、规则可解释性，以及 Issue/PR/commit 之间的自然引用。
- **GitLab** 最值得学习的是 reviewer 与 assignee 分离、mergeability 汇总、required approvals，以及 Issue/MR 与流水线状态在一个工作流里闭环。
- **Gitea/Forgejo** 最符合 GitCandy 的部署定位：用较小的领域模型提供完整 Issue/PR 协作，不要求拆出大量独立服务。
- **Gitee** 对国内团队有直接参考价值：负责人/协作者、里程碑、PR 草稿、行评论、文件已阅、squash 和质量门禁都属于高频功能；其中“文件已阅”和扫描平台接入可排在核心 review 之后。

## 3. 代码浏览先行

当前活动主线已经通过 M9 `#101/#106-#108` 提供 tree、blob、raw、commit、diff、blame、compare 和 archive；`#109` 已独立完成 Git LFS v2 basic transfer。下方约束继续作为回归基线，而不是尚未实施清单。

M10 已完成规范 namespace 直切，旧 `/Repository/...` 地址返回 404。永久链接固定 commit SHA，分支地址可随 HEAD 变化：

```text
/{namespace}/{repository}/tree/{**path}?revision={commitSha}
/{namespace}/{repository}/blob/{**path}?revision={commitSha}#L10-L20
/{namespace}/{repository}/raw/{**path}?revision={commitSha}
/{namespace}/{repository}/commits?revision={revision}
/{namespace}/{repository}/commit/{sha}
/{namespace}/{repository}/compare?baseRevision={base}&headRevision={head}
```

代码页第一版应包括：

- tree、blob、raw、commit history、commit detail、diff、compare、blame、branches、tags、archive。
- fenced code block 和文件语法高亮；行号选择、复制永久链接、复制所选代码片段。
- binary、submodule、symlink、超大文本、大 diff 和未知编码的明确降级状态。
- 文件内容、diff 和 archive 的大小/时间上限与取消；不能把大 blob、archive 或 diff 无界读入内存。
- 每次读取先按仓库稳定 ID 复核权限；raw、archive 和历史 commit 不能绕过私有仓库授权。

## 4. 协作领域边界

### 编号与引用

Issue 和 PR 使用仓库级、事务安全、单调递增的共享 `WorkItemNumber`。两者保留独立实体和服务，不为了共享编号建立复杂继承树。路由始终明确：

```text
/{namespace}/{repository}/issues/{number}
/{namespace}/{repository}/pulls/{number}
```

文本中的 `#123` 解析为当前仓库 work item；`owner/repo#123` 解析跨仓库引用。解析必须在展示和通知前复核目标仓库读取权限，私有仓库引用不能泄漏标题、作者或存在性。

### Markdown 与代码块

Issue、PR 和普通评论采用同一 CommonMark 子集，第一版支持 fenced code block、inline code、列表、task list、引用、链接、用户 mention 和 work-item/commit 引用。原始 Markdown 与渲染结果分离保存；渲染必须进行 HTML sanitization、危险 URL scheme 过滤和长度限制。代码块只做展示，不在服务器执行。

````markdown
```csharp
public Task<Result> MergeAsync(CancellationToken cancellationToken);
```
````

附件、图片代理、Mermaid、数学公式和任意 HTML 不进入第一版；它们分别涉及对象存储、SSRF、脚本执行和内容安全策略，应独立评估。

### Timeline 与通知

Issue 和 PR 复用 timeline event 契约，记录 open/close/reopen、title/body edit、label、milestone、assignee、review、approval、merge 和引用事件。普通评论与 review thread 分开建模；不要让 PR 行内评论退化为只有一段文本的普通评论。

通知写入持久化 inbox，邮件只是可选投递器。通知对象必须包含权限复核所需的 repository/work-item ID，不缓存私有资源标题作为无条件可见文本。mention、assignment、review request、reply、status change 和 merge 是第一版事件；watch 全仓库、digest 和复杂静默规则后补。

### Diff 评论锚点

行内评论至少保存：

```text
OriginalBaseSha
OriginalHeadSha
Path
Side (Old/New)
OriginalLine
DiffHunkContextHash
```

PR 新 push 后应尝试把 thread 映射到新 diff；无法可靠映射时标记为 `Outdated`，仍保留原始 diff 上下文。禁止只保存“当前第 N 行”，否则后续提交会把评论错误挂到另一段代码。

PR 还应维护服务端只读的 `refs/pull/{number}/head`（以及可选、无冲突时的 merge preview ref），使 source branch 删除后提交仍可追溯。Git HTTP/SSH 可以允许 fetch 这些 refs，但 `receive-pack` 必须拒绝客户端直接写入。

### Merge 一致性

合并按钮展示的状态只是快照。真正合并前必须在服务端重新读取 source/target tip、冲突状态、required approvals、required checks、draft 状态和操作者权限，并以 repository 级锁或等价乐观并发保护更新 ref。source branch 在检查与写 ref 之间变化时应失败并要求重试，不能合并旧 head。

## 5. 分阶段范围

### 状态与下一步

- M9-M12 已完成并移入完成历史。
- 当前先由 M12.7 建立统一 inbox、Todo 和 Feed 基础，再由 M13 扩展 PR/review/check/release 通知和外部投递。
- M13 继续完成 PAT、webhook、status/check、branch protection、CODEOWNERS、审计、release 和搜索。

### 明确延后

- 内置 CI runner、Actions 兼容层、merge queue/merge train。
- boards、epics、iterations、roadmaps、工时、复杂自定义字段。
- Packages/Artifacts registry、Wiki、Discussions、Pages。
- suggestion batch apply、在线 IDE、在线解决冲突、PR 文件已阅进度。
- 自动代码扫描、SAST、依赖扫描和 secret scanning 的具体引擎；M13 只预留 status/check 与 hook 接口。

## 6. 验收主链

最小可交付场景必须贯通：

1. owner 创建或导入仓库，开发者浏览 tree/blob/commit/diff，并复制固定 SHA 的 `#Lx-Ly` 代码片段链接。
2. 开发者创建 Issue，使用 fenced code block 描述问题，设置 label/milestone/assignee，mention reviewer。
3. 开发者 push 分支并创建 draft PR，关联 Issue；reviewer 查看 commits/files changed，留下行内评论并 request changes。
4. 新 commit push 后，旧 thread 正确映射或标记 outdated；reviewer approve。
5. 外部 CI 通过 token 写入 check；required approval/check 满足后 squash merge，目标分支更新，Issue 自动关闭并产生审计/timeline/notification。
6. 私有仓库匿名用户在代码、Issue、PR、通知、API、webhook payload 查询中都不能获知受保护内容。

## 7. 官方参考

以下资料均于 2026-07-11 查阅：

- GitHub Issues: https://docs.github.com/issues/tracking-your-work-with-issues/learning-about-issues/about-issues
- GitHub Pull Requests: https://docs.github.com/pull-requests/collaborating-with-pull-requests/proposing-changes-to-your-work-with-pull-requests/about-pull-requests
- GitHub Rulesets: https://docs.github.com/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets
- GitLab Issues: https://docs.gitlab.com/user/project/issues/
- GitLab Merge Requests: https://docs.gitlab.com/user/project/merge_requests/
- GitLab Merge Request approvals: https://docs.gitlab.com/user/project/merge_requests/approvals/
- Gitea Issues & Pull Requests: https://docs.gitea.com/usage/issues-prs
- Gitea Pull Request: https://docs.gitea.com/usage/issues-prs/pull-request
- Gitea labels and references: https://docs.gitea.com/usage/issues-prs/labels and https://docs.gitea.com/usage/issues-prs/automatically-linked-references
- Gitee Issue: https://help.gitee.com/base/issue/intro
- Gitee Pull Request: https://help.gitee.com/base/pullrequest/intro
- Gitee PR code comments, draft, reviewed files, squash and quality gate: https://help.gitee.com/base/pullrequest/comment, https://help.gitee.com/base/pullrequest/draft, https://help.gitee.com/base/pullrequest/check, https://help.gitee.com/base/pullrequest/squash, https://help.gitee.com/base/pullrequest/quality-access
