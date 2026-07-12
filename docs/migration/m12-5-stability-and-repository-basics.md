# M12.5 稳定性、代码浏览基本面与开发门禁

## 变更点

- Git HTTP alias 增加独立的真实 protocol v2 并发 clone/fetch/push 矩阵；只有规范 claim 可解析时才返回 308，legacy 映射继续原地流式服务。
- Issue 创建与评论在 `Serializable` 事务内同时检查滚动一分钟额度并写 timeline，冲突重试后重新读取额度；SQLite 与 SQL Server 新增 actor/type/time 索引。
- Identity 使用默认 Data Protection token provider 实现一小时一次性密码恢复和邮件确认；统一 SMTP 抽象、枚举防护、限流、安全戳失效和管理员恢复审计日志。
- `docker-compose.tls.yml` 提供 Caddy HTTPS；Forwarded Headers 仅在显式配置 known proxy 时启用。
- CI 同时在 Windows/Linux build+test，上传 Cobertura、Playwright 桌面/移动截图，并运行一致恢复集演练。
- 规范地址新增 Branches、Tags、Contributors；删除 ref 只能经过 `IManagedGitRepositoryService`，默认分支和非法 namespace 在服务层拒绝。

本机 Cobertura 结果：`GitCandy.Tests` 86.50%，`GitCandy.Data.Tests` 89.08%，Git assembly 84.55%。CI 对每份测试报告执行 80% 整体 line-rate 门禁；Core、Data、Web/Auth 单 assembly 尚未全部达到 80%，后续新增代码仍按 AGENTS 的模块目标补测试，不得用排除规则掩盖。

## 数据影响

`AtomicIssueDiscussionRateLimit` migration 只新增 `IssueTimelineEvents(ActorUserId, Type, CreatedAtUtc)` 非唯一索引，不改写已有 Issue 数据。SQLite 与 SQL Server migration 均可向下删除该索引；回滚后功能仍可运行，但并发限流查询和范围锁效率下降。

## 配置与兼容

- 新配置：`GitCandy:Identity:AccountRecovery`、`GitCandy:Proxy`、`RepositoryBrowser:MaxStatisticsCommits`、`RepositoryBrowser:MaxContributors`。
- 旧 `/git/{project}[.git]`、规范 Git URL、Smart HTTP content type、streaming 和认证 scheme 不变。
- SMTP 默认禁用；TLS proxy 默认禁用，因此现有部署不会因缺少新配置而停止。

## 回滚

停止 GitCandy，恢复同一时点的应用版本、SQLite、repositories、LFS、Data Protection keys 和配置。仅回滚二进制时，新增索引可保留；不要把已升级数据库交给不兼容的旧版本。Branch/Tag 删除是实际 Git ref 变更，回滚应用不会恢复已删除 ref，应从 reflog/备份或仍持有对象的客户端显式推回。
