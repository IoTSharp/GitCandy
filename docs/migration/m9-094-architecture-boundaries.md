# M9 #094 架构拆分深化

## 变更点

- 新增 `GitCandy.Core`，承载不依赖 ASP.NET Core、EF Core、Git helper 或 SSH 协议库的应用契约、权限资源、配置契约、缓存抽象、当前用户抽象和 scheduler job 契约。
- 新增 `GitCandy.Git`，集中承载仓库路径解析、Git service factory、`IGitTransportBackend` 和唯一允许启动 Git helper 的 `GitProcessTransportBackend`。
- 将仓库、团队、成员和用户管理用例的 EF Core/Identity 实现收进 `GitCandy.Data`，并由 Data 模块扩展方法注册，Web 不再了解具体实现类型。
- 将 Identity public key 与仓库权限查询实现收进 `GitCandy.Data`，其 `ISshAccessService` 契约位于 Core；host key、SSH session、server runtime 和 hosted service 收进 `GitCandy.Ssh`，SSH 协议模块不依赖 EF Core。
- `src/GitCandy` 保留 MVC controllers/views、HTTP authentication adapter、ASP.NET Core authorization handlers、host configuration 和 operational endpoints，继续作为唯一可执行程序与 composition root。
- 新增架构测试，固定活动 solution 的项目集合和直接引用方向，并禁止 Web 重新拥有 Application/Git/SSH 实现源码。

## 依赖方向

```text
GitCandy (Web host) -> Core, Data, Data.Sqlite, Git, Ssh
GitCandy.Ssh        -> Core, Git
GitCandy.Git        -> Core
GitCandy.Data.*     -> Data
GitCandy.Data       -> Core
GitCandy.Core       -> no project/package/framework dependencies
```

依赖图没有环。SQLite 仍是 host 的默认 provider；PostgreSQL、SonnetDB 和 SQL Server 继续作为独立 provider/migration 项目存在，不进入默认运行路径。

## 行为与兼容性

- 不改变公开 MVC 路由、Git Smart HTTP URL、SSH URL 或 endpoint mapping。
- 不改变 Identity cookie、Git Basic authentication scheme、授权 policy 名称或权限语义。
- 不改变 EF Core model、migration、数据库 schema、配置键或文件系统布局。
- 不改变 Git helper 参数、流式 stdin/stdout、超时、并发限制或路径边界检查。
- 不改变内置 SSH 默认启用方式、host key 格式、public key authentication、允许的 Git 命令或 graceful shutdown 行为。

本任务只改变源码归属、程序集边界、直接项目引用和 DI 注册位置。部署仍然只启动一个 `GitCandy` 进程。

## 回滚

回滚时恢复拆分前版本，将 Core 契约、Git transport、SSH runtime 和 Data 应用服务源码移回 Web 项目，并恢复 Web 内的直接 DI 注册即可。此次没有 migration 或持久化格式变化，因此不需要数据库、repository、cache、SSH host key 或 Data Protection key 回滚。

## 验证

- 已运行：`dotnet build GitCandy.slnx --no-restore`，0 warning / 0 error。
- 已运行：`dotnet test tests/GitCandy.Tests/GitCandy.Tests.csproj --no-build --filter FullyQualifiedName~ArchitectureDependencyTests`，3/3 passed。
- 已运行：`dotnet test GitCandy.slnx --no-build`，Data 41/41、Web/协议 67/67 passed，0 skipped。
- 已单独运行 SQLite/Identity/permission 过滤集，20/20 passed。
- 已单独运行 MVC smoke、Git Smart HTTP clone/fetch/push 和 SSH clone/fetch/push 过滤集，4/4 passed，0 skipped。
