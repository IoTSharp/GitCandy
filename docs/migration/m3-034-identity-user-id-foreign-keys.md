# M3 #034 Identity user id 外键

记录日期：2026-07-10

## 验收结论

- GitCandy 领域表中所有用户关系都使用 ASP.NET Core Identity 用户主键 `AspNetUsers.Id`，不再引用旧 MVC5 `Users.ID`。
- `UserRepositoryRoles.UserId`、`UserTeamRoles.UserId`、`SshKeys.UserId` 均为 `string`，并通过 EF Core relationship 显式关联到 `GitCandyUser`。
- SQLite 初始 migration 已生成外键：`FK_UserRepositoryRoles_AspNetUsers_UserId`、`FK_UserTeamRoles_AspNetUsers_UserId`、`FK_SshKeys_AspNetUsers_UserId`。
- 外键删除行为为 cascade；删除 Identity 用户时，会清理该用户的仓库直接角色、团队成员角色和 SSH key，不影响仓库或团队本身。
- 已新增 migration-backed SQLite smoke tests，验证缺失 `AspNetUsers.Id` 的领域用户关系会被数据库拒绝，删除 Identity 用户会级联清理领域用户外键行。

## 测试入口

```powershell
dotnet test tests/GitCandy.Data.Tests/GitCandy.Data.Tests.csproj --filter "FullyQualifiedName~GitCandy.Data.Tests.GitCandyDataServiceCollectionExtensionsTests"
```

覆盖的 smoke 场景：

| 场景 | 覆盖点 |
| --- | --- |
| 缺失 Identity 用户 | `UserRepositoryRoles`、`UserTeamRoles`、`SshKeys` 不能写入不存在的 `AspNetUsers.Id` |
| 删除 Identity 用户 | 级联删除用户仓库角色、用户团队角色和 SSH key |
| 非用户领域数据保留 | 删除用户不会删除 `Repositories` 或 `Teams` |

## 当前边界

- 本任务只闭环新 EF Core/Identity schema 的用户外键，不兼容旧 `Users`、`AuthorizationLog`、`PasswordVersion` 或旧 `_gc_auth` cookie。
- 当前短线仍按 SQLite-first 运行；M3 #038/#039 已验证 SQL Server migration SQL 中的 Identity user id 外键，PostgreSQL 和 SonnetDB provider 差异后续独立回补。
- 本任务不实现 Web 登录、Git Basic Auth 或 SSH public key authentication handler。

## 兼容性影响

本任务只影响 ASP.NET Core 迁移主线的新数据层测试和文档记录，不改变旧 MVC5 运行时行为、公开路由、Git URL、旧 SQL 脚本、Git HTTP/SSH 协议行为或文件系统布局。
