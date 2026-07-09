# M2 #029 跨宿主路径验证

记录日期：2026-07-10

## 验收结论

- 新 ASP.NET Core host 新增启动期路径验证 hosted service，应用启动时会解析并验证 GitCandy 应用路径配置。
- `IGitCandyApplicationPaths` 增加仓库根目录和缓存根目录内的路径解析入口，后续 repository/cache/archive/delete 操作可复用同一套边界检查。
- 普通相对路径和旧式 `~/...`、`~\...` 路径必须解析在 `ContentRootPath` 或 `WebRootPath` 内；需要放到应用目录外的 repository、cache、log、git-core 等路径必须显式配置为绝对路径。
- 仓库和缓存子路径会通过归一化后的绝对路径做根目录边界验证，`../`、根目录前缀碰撞和绝对 sibling 路径都会被拒绝。
- `GitRepositoryPathResolver` 复用新的仓库根目录边界检查，不再维护一套重复的路径逃逸判断。

## 路径规则

| 场景 | 结果 |
| --- | --- |
| `App_Data/Repos` | 基于 ASP.NET Core `ContentRootPath` 解析，适用于 IIS 和 Kestrel |
| `~\App_Data\config.xml` | 兼容旧 `Server.MapPath` 风格，仍基于 `ContentRootPath` |
| `~/css/site.css` via `ResolveWebRootPath` | 基于 `WebRootPath` 解析 |
| `../Repos` | 启动期或解析期失败；相对路径不能逃逸 host root |
| `D:\GitCandy\Repositories` 或 `/var/lib/gitcandy/repositories` | 显式外部绝对路径，允许作为配置根路径 |
| `ResolvePathWithinRepositoryRoot("../outside")` | 失败，避免 repository 子路径逃逸配置的仓库根目录 |

本切片不创建目录、不删除目录、不迁移旧 XML 配置、不改变数据库连接串解释方式，也不改变 Git Smart HTTP/SSH 协议行为。archive/delete 的实际业务操作仍需在后续切片中接入当前边界验证入口。

## 本任务验证

已运行：

- `dotnet test .\GitCandy.slnx`：通过，覆盖默认相对路径、legacy `~` 路径、绝对路径、IIS/Kestrel 风格 content root、无 WebRoot、配置逃逸失败、仓库/缓存根目录边界和 host start 失败。

未运行：

- SQLite 数据读取/写入 smoke test：本切片不改变 EF Core schema 或连接串读取，现有数据层测试随 `dotnet test` 覆盖。
- MVC 登录和主要页面 smoke test：真实登录页面迁移属于后续 M4/M5。
- Git HTTP clone/fetch/push：本切片不改变 Git Smart HTTP 运行时代码，M6 单独验收。
- SSH clone/fetch/push：本切片不改变 SSH 协议运行时代码，M7 单独验收。

## 兼容性影响

- 新迁移 host 对相对路径更严格：相对配置路径必须留在 ASP.NET Core content/web root 内；外部数据目录需要使用绝对路径。
- 这不会改变旧 MVC5 项目行为，也不会改变公开 URL、认证语义、数据库 schema、Git HTTP/SSH 协议行为或文件系统默认布局。
