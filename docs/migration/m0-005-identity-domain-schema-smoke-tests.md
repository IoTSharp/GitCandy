# M0 #005 Identity 与领域 schema smoke test 骨架

记录日期：2026-07-09

## 验收结论

- 已在 `GitCandy.Data` 中建立 Identity 用户扩展、仓库、团队、用户仓库角色、团队仓库角色、用户团队角色的 EF Core 领域 schema 骨架。
- 已新增 `IGitCandyRepositoryPermissionQuery` 作为后续 Web、Git HTTP 和 SSH 复用的权限查询入口。
- 已新增 SQLite smoke test，覆盖新数据库创建、Identity 用户写入、领域实体写入、大小写不敏感名称规范化、领域数据读取和基础权限查询。
- `EnsureCreatedAsync` 只保留为 M0 早期 smoke test；正式 SQLite/SQL Server migration 与 migration SQL 验收已由 M3 #032/#038/#039 闭环。

## 测试入口

```powershell
dotnet test GitCandy.slnx --filter "FullyQualifiedName~GitCandy.Data.Tests.GitCandyDataServiceCollectionExtensionsTests"
```

覆盖的 smoke 场景：

| 场景 | 覆盖点 |
| --- | --- |
| Identity 用户读写 | `AspNetUsers` 可创建并读取 `GitCandyUser` |
| 领域表创建 | `Repositories`、`Teams`、`UserRepositoryRoles`、`TeamRepositoryRoles`、`UserTeamRoles` 可由 SQLite 创建 |
| 领域数据读写 | 写入公开仓库、私有仓库、团队、owner 授权和团队授权 |
| 名称规范化 | 仓库/团队名称保存时写入 `NormalizedName`，查询时按规范化名称匹配 |
| 权限查询入口 | 匿名读公开仓库、匿名写公开仓库失败、owner 读写、团队成员读写、无权限用户失败、管理员兜底读取 |

## 当前边界

- 本任务不定义最终 migration，不以 `EnsureCreatedAsync` 作为发布建库策略。
- 本任务不兼容旧 `Users`、`AuthorizationLog`、`PasswordVersion` 或旧 `_gc_auth` cookie。
- 管理员身份由调用方通过 Identity role/policy 判断后传入权限查询；后续 M0 #006 和 M4 会补齐完整权限服务语义。
- SSH key、PAT、Basic Auth scheme 和 Git transport backend 不在本任务实现范围内。

## 兼容性影响

本任务新增 ASP.NET Core 迁移主线的数据层骨架和测试，不改变旧 MVC5 运行时行为，也不改变：

- 公开路由和 Git URL。
- 旧数据库 schema、旧 SQL 脚本和旧认证 cookie。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。
