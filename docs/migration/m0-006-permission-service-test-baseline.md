# M0 #006 权限服务测试基线

记录日期：2026-07-09

## 验收结论

- 已新增 `GitCandyRepositoryPermissionQueryTests`，覆盖匿名、公有仓库、私有仓库、owner、team member、administrator 和无权限用户的仓库读写语义。
- 权限查询入口为 `IGitCandyRepositoryPermissionQuery`，由 `AddGitCandyData(...)` 注册，后续 Web、Git HTTP、SSH 和后台任务迁移应复用该入口或在 M4 中围绕它扩展。
- 测试使用 SQLite 真实数据库文件，通过 `EnsureCreatedAsync` 创建 Identity + 领域表，并验证权限查询可以被 EF Core 翻译执行。
- 仓库和团队名称通过 `NormalizedName` 保存规范化值，测试覆盖了仓库名称大小写不敏感查询。

## 测试入口

```powershell
dotnet test .\GitCandy.slnx --filter "FullyQualifiedName~GitCandyRepositoryPermissionQueryTests"
```

本次已运行完整测试：

```powershell
dotnet test .\GitCandy.slnx
```

结果：25 个测试通过。

## 权限矩阵

| Actor | Repository | Read | Write | 覆盖测试 |
| --- | --- | --- | --- | --- |
| anonymous | `public-demo` | 是 | 否 | `CanReadRepository_WithAnonymousAndPublicRepository_ReturnsTrue`、`CanWriteRepository_WithAnonymousAndPublicRepository_ReturnsFalse` |
| anonymous | `private-demo` | 否 | 否 | `CanReadRepository_WithAnonymousAndPrivateRepository_ReturnsFalse` |
| owner `alice` | `private-demo` | 是 | 是 | `CanReadRepository_WithPrivateRepositoryAndOwner_ReturnsTrue`、`CanWriteRepository_WithPrivateRepositoryAndOwner_ReturnsTrue` |
| team member `bob` | `private-demo` | 是 | 是 | `CanReadRepository_WithPrivateRepositoryAndTeamMember_ReturnsTrue`、`CanWriteRepository_WithPrivateRepositoryAndTeamMember_ReturnsTrue` |
| administrator `admin` | `private-demo` | 是 | 是 | `CanReadRepository_WithPrivateRepositoryAndAdministrator_ReturnsTrue`、`CanWriteRepository_WithPrivateRepositoryAndAdministrator_ReturnsTrue` |
| authenticated no-role user `carol` | `private-demo` | 否 | 否 | `CanReadRepository_WithPrivateRepositoryAndUnassignedUser_ReturnsFalse`、`CanWriteRepository_WithPrivateRepositoryAndUnassignedUser_ReturnsFalse` |
| administrator `admin` | missing repository | 否 | 否 | `CanReadRepository_WithMissingRepositoryAndAdministrator_ReturnsFalse` |

## 当前服务边界

- `IGitCandyRepositoryPermissionQuery` 接收 Identity 用户主键和管理员标记；管理员标记由后续 Web Identity role/policy、Git Basic/PAT 或 SSH key 认证层判定后传入。
- 写权限要求匿名写或角色同时具备 `AllowRead=true` 与 `AllowWrite=true`，保留旧 GitCandy 的写入语义。
- owner 当前通过用户仓库角色授予读写权限；`IsOwner` 字段作为后续仓库管理权限语义保留。
- 本任务不实现 Web cookie、Git Basic Auth、PAT、SSH public key 或具体 authorization handler。
- 本任务仍使用 `EnsureCreatedAsync` 作为 M0 早期测试入口；正式 SQLite/SQL Server migration、migration SQL 和 provider 差异已由 M3 闭环。

## 兼容性影响

本任务新增 ASP.NET Core 迁移主线的权限服务测试基线和领域表骨架，不改变旧 MVC5 运行时行为，也不改变：

- 公开路由和 Git URL。
- 旧数据库 schema、旧 SQL 脚本和旧认证 cookie。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。
