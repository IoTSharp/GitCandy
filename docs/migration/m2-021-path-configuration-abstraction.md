# M2 #021 路径配置抽象

记录日期：2026-07-09

## 验收结论

- 已新增 `IGitCandyApplicationPaths` 和 `GitCandyApplicationPaths`，作为 ASP.NET Core 宿主中的统一路径解析入口。
- 路径解析基于 `IWebHostEnvironment.ContentRootPath` 和 `IWebHostEnvironment.WebRootPath`，替代旧 MVC5 代码中的 `Server.MapPath` 使用方式。
- `GitCandy:Application` 中的 `LogPathFormat`、`UserConfigurationPath`、`RepositoryPath`、`CachePath` 和 `GitCorePath` 均可通过该抽象得到绝对路径。
- 支持旧式虚拟路径 `~`、`~/...`、`~\...`，也支持普通相对路径和部署者显式配置的绝对路径。
- `GitCorePath` 未配置时保持为空字符串，留给后续 Git backend 发现逻辑处理。

## 路径解析规则

| 配置路径形式 | 解析结果 |
| --- | --- |
| `App_Data/Repos` | 基于 `ContentRootPath` 解析 |
| `~\App_Data\config.xml` | 基于 `ContentRootPath` 解析，兼容旧 `Server.MapPath` 风格 |
| `~/css/site.css` via `ResolveWebRootPath` | 基于 `WebRootPath` 解析 |
| `D:\GitCandy\Repositories` 或 `/var/lib/gitcandy/repositories` | 保留为绝对路径并标准化 |
| 空 `GitCorePath` | 保持为空字符串 |

本切片不创建目录、不删除目录、不自动迁移旧 XML 配置，也不改变 `ConnectionStrings:GitCandy` 中数据库连接串的解释方式。repository/cache/archive/delete 的路径逃逸边界检查继续放在 M2 #029 和 Git transport 后续切片中完成。

## 本任务验证

已运行：

- `dotnet test .\GitCandy.slnx`：通过，覆盖默认相对路径、legacy `~` 路径、绝对路径、WebRootPath 解析和 DI 注册。
- `dotnet build .\GitCandy.slnx`：通过，Debug 构建 0 警告/0 错误。

未运行：

- SQLite 数据读取/写入 smoke test：#021 不改变 EF Core schema 或连接串读取，现有测试随 `dotnet test` 覆盖。
- MVC 登录和主要页面 smoke test：真实登录页面迁移属于后续 M4/M5。
- Git HTTP clone/fetch/push：#021 不改变 Git Smart HTTP 运行时代码，M6 单独验收。
- SSH clone/fetch/push：#021 不改变 SSH 运行时代码，M7 单独验收。

## 兼容性影响

- 新 ASP.NET Core 宿主新增路径解析服务；旧 MVC5 项目未改动，仍仅作为行为参考。
- 旧 `~\App_Data\...` 风格路径在新宿主中有明确替代语义，默认相对路径统一基于 content root，而不是进程当前工作目录或 build 输出目录。
- 本任务没有更改公开 URL、认证语义、数据库 schema、Git HTTP/SSH 协议行为或文件系统布局。
