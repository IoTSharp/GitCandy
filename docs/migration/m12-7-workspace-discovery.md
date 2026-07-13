# M12.7 个人工作台、公开个人页与仓库发现验收

验收日期：2026-07-13

对应任务：`#139M-#139W`

## 完成范围

- 登录后 `/` 进入私人 `/me`；匿名 `/` 保持公开起始页。`/me`、`/todos`、`/notifications` 和 `/me/*` 使用 Identity authorization，并保留安全本地 `returnUrl`。
- `IWorkspaceService` 提供有界、可取消、稳定分页的 Dashboard/Todo/Notification/Feed/Repository/Team/Profile/Explore 投影；Controller 和 Razor 不执行复杂 EF 查询。
- Todo 覆盖 Issue assignee、mention、PR review request 和本人 changes-requested PR，支持 complete、restore、1-30 天 snooze、乐观并发、源资源自动解决和读取时失权退出。
- M11 Issue 通知和 PR review request 投影进统一 `Notifications`，保留 read/unread、投递原因、资源类型和 team 来源；Todo 状态与通知已读互不联动。
- `ActivityEvents` 使用版本化幂等 event ID，由 Quartz 后台增量投影 Issue/PR timeline，并独立执行 180 天 retention；Feed 只读取已投影事件。
- `/{username}` 仅允许 repositories、stars、packages、teams 四个 tab，输出 DTO 白名单；邮箱、认证/安全设置、SSH/PAT、私有仓库和不可公开团队不进入投影。`settings` 返回 404。
- Repository Star 使用 Identity user ID + repository ID 复合主键，star/unstar 幂等；`/me/stars` 和公开 Stars tab 读取相同持久事实并重新应用公开边界。
- Packages 只提供真实空目录和空状态，不提供 upload/push API；实际 OCI 数据仍由 M15.6 接入。
- 公开指标按日记录 90 天提交分布、Star、成功 archive/LFS/Git fetch、匿名日去重页面访问和显式 SPDX metadata。页面访问不保存 IP、Authorization 或完整 User-Agent，并过滤 bot、health probe 和管理员探测。
- `m12.7-v1` Quartz 作业生成不可变推荐快照、归一化信号、稳定排名和解释标签；`/explore` 只读取匿名可读仓库，快照为空或失败时回退为确定性的近期公开仓库排序。

## Schema 与 provider

新增表：`Todos`、`Notifications`、`ActivityEvents`、`RepositoryStars`、`RepositoryInteractions`、`RepositoryMetricsDaily`、`RepositoryPageViews`、`RepositoryRecommendationSnapshots`。

增量 migrations：

- SQLite：`20260713042915_WorkspaceDashboardDiscovery`
- SQL Server：`20260713042928_WorkspaceDashboardDiscovery`
- SonnetDB：`20260713042941_WorkspaceDashboardDiscovery`

PostgreSQL 项目此前没有活动 migration 基线，也不是当前 Web host 可选 provider，因此本任务没有把全库初始 schema 伪装成 M12.7 增量。默认 SQLite 不变；SonnetDB 仍只能通过专用配置显式启用。

升级影响：migration 只扩展 schema，并为 `me`、`todos`、`notifications`、`explore` 增加 namespace 保留 claim；不改写 Identity 密码/cookie、仓库路径或 Git URL。首次后台运行会从现有 Issue/PR timeline 增量建立 Activity 投影；用户首次读取工作台或通知时，会从现有 Issue 通知增量建立其统一 inbox 行。

## 兼容与回滚

- `/Repository` 等现有固定 controller 路由继续优先；公开 `/{username}` 是固定路由之后、双段 repository 路由之前的末端 conventional route。
- Git Smart HTTP、SSH 和 LFS 路由与 wire behavior 不变。成功 upload-pack/LFS/archive 只在流式操作完成后写入短事务指标；采集失败不能改变已经完成的协议响应。
- 回滚应用前应备份数据库。若必须回滚到 M12.6，可先停机，再把三个 provider 中对应 migration 回退到上一 migration；这会删除 M12.7 投影、Star 和聚合数据，不影响 Identity、仓库、Issue、PR 或 Git 对象。
- `ActivityEvents` 是用户 Feed 输入，不是 M13 不可篡改审计日志；不能将其当作安全审计证据。

## 验证

- `dotnet build GitCandy.slnx --configuration Release`：0 warning、0 error。
- `dotnet test GitCandy.slnx --configuration Release --no-build`：`GitCandy.Data.Tests` 67/67、`GitCandy.Tests` 103/103，共 170/170，0 skip、0 failure。
- SQLite：migration-backed Workspace service 测试覆盖 Todo/通知/Feed/Star/指标/推荐和失权；默认数据库实际迁移、读写通过。
- SonnetDB：完整 migration、Identity、RepositoryStar 和 Todo 写读 smoke 通过。
- SQL Server：idempotent migration SQL 包含八张 M12.7 表、复合唯一索引和外键。
- MVC：匿名/登录根路由、固定路由优先级、公开字段白名单、管理员不绕过、invalid tab、Star 页面和 antiforgery 路径通过。
- Playwright/Chrome：1440x900 与 390x844、长显示名/仓库名/描述、空状态、桌面/移动导航、Skip Link、Tab、Enter、Escape、公开匿名页和安全 returnUrl 通过；浏览器控制台 0 error、0 warning。
- 既有全量 Web/Git/SSH/LFS 测试继续覆盖 clone/fetch/push、流式协议、路径边界和权限回归。

## 已知边界

- M13 `#145` 继续扩展 check/release 和外部邮件/webhook 投递，不新建第二套 inbox。
- M15.6 完成前 Packages 目录保持真实空状态。
- 页面访问日去重依赖随机匿名 visitor cookie；拒绝非必要 cookie 的访客不会被虚构成个人画像，指标精度可低于允许 cookie 的流量。
