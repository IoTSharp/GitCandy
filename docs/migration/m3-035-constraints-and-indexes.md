# M3 #035 约束与索引

记录日期：2026-07-10

## 验收结论

- `GitCandyDbContext` 已显式配置 Identity key、Identity store key、GitCandy 领域字段长度、required 语义、PK/FK、cascade delete 和索引名称。
- `Repositories.NormalizedName`、`Teams.NormalizedName` 和 `SshKeys.Fingerprint` 均为唯一索引；仓库和团队大小写不敏感唯一语义通过保存前写入 `NormalizedName` 实现。
- `AspNetUsers.Id`、`AspNetRoles.Id` 以及领域表中的 Identity user id 外键统一限制为 450 字符；Identity login/token 组合 key 组件限制为 128 字符。
- SQLite `InitialIdentitySchema` migration/snapshot 已重建为当前新系统 schema，包含上述长度、required、PK/FK 和索引配置。
- 已新增 EF model metadata smoke test，验证关键字段长度、nullability、主键、外键、delete behavior 和唯一索引。
- 已新增 SQLite migration-backed smoke test，验证仓库名和团队名仅大小写不同也会被唯一约束拒绝。

## 测试入口

```powershell
dotnet test tests/GitCandy.Data.Tests/GitCandy.Data.Tests.csproj --filter "FullyQualifiedName~GitCandy.Data.Tests.GitCandyDataServiceCollectionExtensionsTests"
```

覆盖的 smoke 场景：

| 场景 | 覆盖点 |
| --- | --- |
| EF model metadata | 字段长度、required、PK、FK、cascade delete、唯一索引 |
| SQLite migration schema | `MigrateAsync()` 创建包含显式约束和索引的新 schema |
| 大小写不敏感名称 | `sample-demo` / `SAMPLE-DEMO` 仓库名重复被拒绝，`core` / `CORE` 团队名重复被拒绝 |
| SSH key 唯一性 | 重复 SSH fingerprint 被拒绝 |

## 当前边界

- 本任务仍以 SQLite 为短期业务实现和验收 provider；SQL Server migration SQL、PostgreSQL 和 SonnetDB 差异继续留给 M3 #038/#039 后续工作。
- SQLite 不强制执行 `maxLength`，本任务通过 EF metadata 和 migration 表达长度；后续 SQL Server migration SQL 需要继续审阅实际 DDL。
- 本任务不兼容旧 `Users`、`AuthorizationLog`、`PasswordVersion` 或旧 `_gc_auth` cookie。
- 本任务不实现登录、注册、Git Basic Auth、SSH public key authentication 或仓库 CRUD 页面。

## 兼容性影响

本任务只影响 ASP.NET Core 迁移主线的新 EF Core/Identity schema、SQLite migration 和测试，不改变旧 MVC5 运行时行为、公开路由、Git URL、旧 SQL 脚本、Git HTTP/SSH 协议行为或文件系统布局。
