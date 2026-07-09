# M3 #033 GitCandy 领域表

记录日期：2026-07-10

## 验收结论

- 已在 `GitCandy.Data` 中重新建模 GitCandy 领域表：`Repositories`、`Teams`、`UserRepositoryRoles`、`TeamRepositoryRoles`、`UserTeamRoles`、`SshKeys`。
- `SshKeys` 使用 ASP.NET Core Identity 用户主键作为 `UserId` 外键，不兼容旧 long `Users.ID`。
- 仓库和团队保留大小写不敏感唯一语义，通过 `NormalizedName` 跨 provider 表达。
- SSH key 保留旧 SSH 认证所需的 `KeyType`、`Fingerprint`、`PublicKey` 业务字段，并新增全局唯一 fingerprint 约束，避免同一公钥被多个用户重复绑定造成身份歧义。
- SQLite migration-backed smoke test 已覆盖领域表/索引创建、领域表读写、Identity user id 外键、仓库权限查询和重复 SSH fingerprint 拒绝。

## 测试入口

```powershell
dotnet test GitCandy.slnx --filter "FullyQualifiedName~GitCandy.Data.Tests.GitCandyDataServiceCollectionExtensionsTests"
```

覆盖的 smoke 场景：

| 场景 | 覆盖点 |
| --- | --- |
| 仓库/团队领域表 | `Repositories`、`Teams` 可创建、写入和读取 |
| 角色关系表 | 用户仓库角色、团队仓库角色、用户团队角色可写入并用于权限查询 |
| 名称规范化 | 仓库/团队 `NormalizedName` 在保存时写入 |
| migration schema | `MigrateAsync()` 创建 Identity 表、领域表和关键索引 |
| SSH key | `SshKeys` 可写入并通过 Identity user id 关联到 `AspNetUsers` |
| SSH key 唯一性 | 重复 `Fingerprint` 会被数据库唯一约束拒绝 |

## 当前边界

- 本任务复用当前 SQLite 初始 migration/snapshot 显式表达领域表；SQL Server migration SQL 和跨 provider 差异仍由 M3 #038/#039 闭环。
- 本任务不兼容旧 `Users`、`AuthorizationLog`、`PasswordVersion` 或旧 `_gc_auth` cookie。
- 本任务不实现 SSH public key authentication handler，只提供后续 M7 复用的数据模型。
- PostgreSQL、SonnetDB 和 SQL Server 的 schema SQL 差异仍作为后续 provider 工作处理。

## 兼容性影响

本任务只扩展 ASP.NET Core 迁移主线的新 EF Core model 和测试，不改变旧 MVC5 运行时行为，也不改变公开路由、Git URL、旧 SQL 脚本、Git HTTP/SSH 协议行为或文件系统布局。
