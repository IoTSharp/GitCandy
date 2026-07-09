# M2 #024 DI 替换 MEF

记录日期：2026-07-09

## 验收结论

- 新 ASP.NET Core host 显式注册 `IMembershipService`/`MembershipService`，用户查找和管理员判断进入 Identity `UserManager<GitCandyUser>`。
- 新增 `IRepositoryService`/`RepositoryService`，仓库摘要查询、可见仓库列表和 read/write 权限判断通过 EF Core + `IGitCandyRepositoryPermissionQuery` 注入。
- 新增 `IGitRepositoryPathResolver` 和 `IGitServiceFactory`，后续 Git HTTP/SSH 迁移不再需要在 controller 或 session handler 中直接 `new GitService(project)`，并先建立仓库根目录边界检查。
- 新增 `ISchedulerJob`、`SchedulerJobContext`、`SchedulerJobType` 和 `LogRotationJob`，把旧 `LogJob` 的任务发现入口从 MEF 改为 ASP.NET Core DI 的 `IEnumerable<ISchedulerJob>`。
- `SystemWebEntryCheckTests` 增加 `System.Composition` / `System.ComponentModel.Composition` 门禁，防止新迁移项目重新引入 MEF。

## 迁移映射

| 旧入口 | 新入口 | 说明 |
| --- | --- | --- |
| MEF `[Export(typeof(MembershipService))]` | `services.TryAddScoped<IMembershipService, MembershipService>()` | 只迁新 Identity 语义，不兼容旧 `_gc_auth`、旧密码 hash 或 `AuthorizationLog` |
| MEF `[Export(typeof(RepositoryService))]` | `services.TryAddScoped<IRepositoryService, RepositoryService>()` | 先覆盖仓库读取和权限查询，CRUD 页面后续 M5 垂直切片继续补齐 |
| `new GitService(project)` | `IGitServiceFactory.Create(project)` | #024 只固定 DI 和路径边界；Git Smart HTTP streaming 后端在 M6 继续迁移 |
| MEF `builder.ForTypesDerivedFrom<IJob>()` | `IEnumerable<ISchedulerJob>` | #024 只注册 job；实际 hosted service 生命周期在 #025 处理 |

## 行为边界

- 本任务不启动 scheduler，也不改变应用启动/停止生命周期；hosted service 属于 #025。
- 本任务不迁移 Git Smart HTTP 的 pack streaming、headers 或 helper 执行；这些属于 M6。
- 本任务不迁移内置 SSH server 的 session 生命周期；这些属于 M7。
- 旧 MVC5 项目中的 MEF 配置和 `[Export]/[Import]` 保持不动，仅作为迁移前行为参考。

## 本任务验证

已运行：

- `dotnet test .\GitCandy.slnx`：通过，覆盖 `MembershipService`、`RepositoryService`、Git factory、scheduler job DI 注册、仓库路径边界拒绝和既有数据层 smoke tests。
- `dotnet build .\GitCandy.slnx`：通过，Debug 构建 0 警告/0 错误。

未运行：

- MVC 登录和主要页面 smoke test：真实登录页面迁移属于后续 M4/M5。
- Git HTTP clone/fetch/push：#024 只增加 DI 和路径解析入口，协议迁移属于 M6。
- SSH clone/fetch/push：#024 不改变 SSH 运行时代码，M7 单独验收。

## 兼容性影响

- 新迁移宿主新增 ASP.NET Core DI 服务注册，不改变公开路由、数据库 schema、Identity cookie、Basic Auth、Git HTTP/SSH 协议行为或文件系统布局。
- 仓库路径解析现在会拒绝包含目录分隔符的仓库名，作为后续 Git HTTP/SSH 路径安全迁移的保守入口。
