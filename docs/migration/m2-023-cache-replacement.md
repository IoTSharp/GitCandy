# M2 #023 缓存替换

记录日期：2026-07-09

## 验收结论

- 新 ASP.NET Core 宿主显式注册 `IMemoryCache`，作为旧 `HttpRuntime.Cache` 的迁移目标。
- 新增 `IApplicationCache` 和 `MemoryApplicationCache`，为后续迁移旧 controller、认证、Git 服务和后台任务提供构造函数注入入口。
- `IApplicationCache` 支持绝对过期、相对过期、读取和移除，覆盖旧 token cache 使用的 `Insert/Get/Remove` 行为。
- `GitCandy.slnx` 的 `System.Web` 入口门禁扩展了 `HttpRuntime.Cache` 和 `System.Web.Caching` 检查，防止新迁移项目重新引入旧缓存入口。
- 旧 MVC5 项目中的 `HttpRuntime.Cache` 保持不动，只作为行为参考；本切片不迁移旧 `_gc_auth` token 体系，也不改变 Identity cookie 或 Git Basic Auth 设计。

## 迁移映射

| 旧入口 | 新入口 | 说明 |
| --- | --- | --- |
| `HttpRuntime.Cache.Insert(key, value, null, expires, Cache.NoSlidingExpiration)` | `IApplicationCache.Set(key, value, expires)` | 迁移旧绝对过期缓存语义 |
| `HttpRuntime.Cache.Get(key)` | `IApplicationCache.TryGetValue<T>(key, out value)` | 强类型读取，避免散落 object cast |
| `HttpRuntime.Cache.Remove(key)` | `IApplicationCache.Remove(key)` | 移除缓存项 |

`IApplicationCache` 只承载可从数据库、Git 仓库或配置重新推导的应用内缓存。需要跨实例共享、持久化或协议级状态的场景，后续切片应单独评估 `IDistributedCache`、数据库或专用存储，不能把内存缓存当作可靠数据源。

## 本任务验证

已运行：

- `dotnet test .\GitCandy.slnx`：通过，覆盖 `IMemoryCache`/`IApplicationCache` 注册、缓存命中、绝对过期、移除行为和既有 SQLite 数据层 smoke tests。
- `dotnet build .\GitCandy.slnx`：通过，Debug 构建 0 警告/0 错误。

未运行：

- MVC 登录和主要页面 smoke test：真实登录页面迁移属于后续 M4/M5。
- Git HTTP clone/fetch/push：#023 不改变 Git Smart HTTP 运行时代码，M6 单独验收。
- SSH clone/fetch/push：#023 不改变 SSH 运行时代码，M7 单独验收。

## 兼容性影响

- 新迁移宿主新增应用内缓存服务注册，不改变公开路由、数据库 schema、Identity cookie、Basic Auth、Git HTTP/SSH 协议行为或文件系统布局。
- 内存缓存是单进程、非持久化缓存；多实例部署或需要可靠共享状态的场景不能依赖它保存认证或权限事实。
