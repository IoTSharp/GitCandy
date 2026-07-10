# M3 #037 DbContext 注入边界

记录日期：2026-07-10

## 验收结论

- SQLite、SQL Server、PostgreSQL 和 SonnetDB provider 统一使用 `AddPooledDbContextFactory<GitCandyDbContext>` 注册。
- DI 同时暴露 scoped `GitCandyDbContext` 和 singleton `IDbContextFactory<GitCandyDbContext>`：请求/应用服务直接注入 scoped context，后台工作按执行单元通过 factory 创建并释放 context。
- `RepositoryService`、`GitCandyRepositoryPermissionQuery` 和 Identity stores 继续在请求 scope 内共享 context；Quartz bridge job 每次执行创建独立 scope，不跨线程持有 scoped context。
- 生命周期测试验证同一 scope 内 context 复用、不同 scope 间 context 隔离，以及并发 factory 调用返回不同 context 实例。

## 使用约束

- MVC controller、scoped 应用服务和 authorization handler 可构造函数注入 `GitCandyDbContext`。
- `BackgroundService`、singleton、并行任务和 scope 外回调必须注入 `IDbContextFactory<GitCandyDbContext>`，在单次工作内调用 `CreateDbContextAsync` 并及时释放。
- 不在多个线程间共享 `DbContext`、entity 或未完成的查询；需要并行数据库工作时，每个并行分支创建自己的 context。
- context pooling 只复用已释放实例，不改变 `DbContext` 非线程安全语义，也不允许把 scoped context 缓存在静态字段或 singleton 中。

## 测试入口

```powershell
dotnet test tests/GitCandy.Data.Tests/GitCandy.Data.Tests.csproj --filter "FullyQualifiedName~GitCandyDbContextLifetimeTests"
```

## 兼容性影响

本任务只收敛 ASP.NET Core 迁移主线的数据访问生命周期，不改变 schema、旧 MVC5 运行时、公开 URL、Git HTTP/SSH 协议或文件系统布局。
