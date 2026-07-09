# M3 #032 Identity 标准 schema

记录日期：2026-07-10

## 验收结论

- 已新增 SQLite EF Core 初始 migration `InitialIdentitySchema` 和 `GitCandyDbContext` model snapshot。
- migration 使用 ASP.NET Core Identity 标准表名：`AspNetUsers`、`AspNetRoles`、`AspNetRoleClaims`、`AspNetUserClaims`、`AspNetUserLogins`、`AspNetUserRoles`、`AspNetUserTokens`。
- `AspNetUsers` 保留 Identity 标准列，并扩展 `DisplayName`、`Description` 作为 `GitCandyUser` 页面资料字段。
- 已新增 migration-backed SQLite smoke test，使用 `MigrateAsync()` 创建数据库并检查 Identity 表、`EmailIndex`、`UserNameIndex`、`RoleNameIndex` 和 `__EFMigrationsHistory`。
- 测试同时确认不会创建旧认证表 `Users` 和 `AuthorizationLog`。

## 变更文件

| 文件 | 说明 |
| --- | --- |
| `src/GitCandy.Data.Sqlite/Migrations/20260709172133_InitialIdentitySchema.cs` | SQLite 初始 migration |
| `src/GitCandy.Data.Sqlite/Migrations/20260709172133_InitialIdentitySchema.Designer.cs` | migration target model |
| `src/GitCandy.Data.Sqlite/Migrations/GitCandyDbContextModelSnapshot.cs` | EF Core model snapshot |
| `tests/GitCandy.Data.Tests/GitCandyDataServiceCollectionExtensionsTests.cs` | migration-backed Identity schema smoke test |

## 当前边界

- 当前 DbContext 已包含 M0/M3 阶段落地的仓库、团队、权限角色和 SSH key 领域表骨架，因此初始 migration/snapshot 反映的是当前完整 EF Core 模型；#032 的验收重点仍限定在 Identity 标准 schema。
- 本任务不兼容旧 `Users`、`AuthorizationLog`、`PasswordVersion` 或旧 `_gc_auth` cookie。
- 按当前 SQLite-first 校准，本任务只新增 SQLite migration；SQL Server migration SQL、跨 provider schema 差异和领域表完整约束继续留给后续 M3 任务闭环。
- 不在应用启动时自动执行生产 schema 变更。

## 测试入口

```powershell
dotnet ef migrations has-pending-model-changes --project src/GitCandy.Data.Sqlite --startup-project src/GitCandy.Data.Sqlite --context GitCandyDbContext
dotnet build GitCandy.slnx
dotnet test tests/GitCandy.Data.Tests/GitCandy.Data.Tests.csproj
dotnet test GitCandy.slnx
```

本轮已通过以上验证。其中 `has-pending-model-changes` 返回当前模型相对最新 migration 无 pending changes。
