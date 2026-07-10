# M3 #036 Lazy loading 决策

记录日期：2026-07-10

## 验收结论

- 新 EF Core 数据层默认不引入 `Microsoft.EntityFrameworkCore.Proxies`，不调用 `UseLazyLoadingProxies()`。
- `GitCandyDbContext` 初始化时显式设置 `ChangeTracker.LazyLoadingEnabled = false`。
- GitCandy 新领域实体保持 `sealed`，导航属性不声明为 `virtual`，也不通过 `ILazyLoader` 或 lazy-loading delegate 构造函数触发隐式加载。
- 需要跨导航读取数据时，查询必须使用显式 `Include`、join、`Any` 子查询或投影；服务层不能依赖访问 navigation property 后自动打数据库。
- 已补 SQLite smoke test，验证未 `Include` 的 `Repository.UserRoles` 不会隐式加载，显式 `Include` 后才加载角色集合。

## 决策原因

- GitCandy 的仓库、团队、用户角色和 SSH key 会进入权限判断、Git HTTP/SSH 鉴权和页面列表等热路径；隐式 lazy loading 容易引入 N+1 查询，并让权限查询的数据库访问边界不透明。
- ASP.NET Core 迁移主线需要先保护行为和可验证性。显式查询能让权限、列表、后台任务和 Git transport 调用更容易审阅、测试和限流。
- 旧 EF6 模型中的 `virtual` navigation 只作为历史参考；新 EF Core schema 不以 EF6 lazy loading 行为为兼容目标。

## 查询约束

- Controller、应用服务和权限服务应返回 view model、DTO 或专用查询结果，不把 EF entity graph 直接传到视图后再依赖 navigation 加载。
- 读列表和权限判断优先用投影、join 或 `Any`，避免先拉出实体再遍历 navigation。
- 确实需要实体图时，在同一个查询中显式 `Include` / `ThenInclude`，并按数据规模选择 split query 或投影。
- 后台任务后续使用 `IDbContextFactory<GitCandyDbContext>` 时也必须遵守显式查询，不跨线程或跨 scope 持有实体图等待 navigation 自动加载。

## 测试入口

```powershell
dotnet test tests/GitCandy.Data.Tests/GitCandy.Data.Tests.csproj --filter "FullyQualifiedName~GitCandy.Data.Tests.GitCandyLazyLoadingDecisionTests"
```

覆盖的 smoke 场景：

| 场景 | 覆盖点 |
| --- | --- |
| Provider 配置 | 未注册 EF Core proxies 扩展，`ChangeTracker.LazyLoadingEnabled` 为 `false` |
| 实体形态 | GitCandy 新领域实体为 `sealed`，navigation property 非 `virtual`，没有 `ILazyLoader` 构造注入 |
| 查询行为 | 未 `Include` 时 navigation 集合不隐式加载，显式 `Include` 后加载 |

## 当前边界

- 本任务不改变数据库 schema、migration、旧 SQL 脚本或旧 MVC5 EF6 运行时行为。
- 本任务不重写现有权限查询；当前 `GitCandyRepositoryPermissionQuery` 和 `RepositoryService` 已使用显式 join、`Any` 或投影。
- SQL Server 已在 #038/#039 验证 provider 独立 migration；PostgreSQL 和 SonnetDB 差异后续独立回补。lazy loading 决策对 provider-neutral 数据层生效。

## 兼容性影响

本任务只影响 ASP.NET Core 迁移主线的新 EF Core 数据层约束和测试，不改变公开 URL、Git HTTP/SSH 协议行为、Identity cookie、Basic Auth、文件系统布局或旧数据库兼容策略。
