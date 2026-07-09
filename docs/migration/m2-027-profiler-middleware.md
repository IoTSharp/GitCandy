# M2 #027 Profiler 迁移

记录日期：2026-07-10

## 验收结论

- 新 ASP.NET Core host 新增 `RequestProfiler`，保留旧 `Profiler` 每个请求记录 elapsed time 的轻量行为。
- 新增 `GitCandyRequestProfilerMiddleware`，用 ASP.NET Core middleware 替代旧 `Application_BeginRequest` 中的 `Profiler.Start()`。
- 新增 `IRequestProfilerAccessor` 和 `HttpContextRequestProfilerAccessor`，让 Razor view、controller 或后续服务通过 DI 读取当前请求 profiler，而不依赖 `HttpContext.Current`。
- 新 host 的 `_Layout.cshtml` 通过 ASP.NET Core `HttpContext` 扩展方法在 footer title 中读取当前请求耗时，作为旧布局 `title="@Profiler.Current.Elapsed"` 的迁移占位。

## 迁移映射

| 旧入口 | 新入口 | 说明 |
| --- | --- | --- |
| `Application_BeginRequest` | `app.UseGitCandyRequestProfiler()` | profiler 由 ASP.NET Core middleware 在请求管线前段启动 |
| `Profiler.Start()` | `GitCandyRequestProfilerMiddleware.InvokeAsync()` | 每个请求创建一个新的 `RequestProfiler` |
| `Profiler.Current` | `IRequestProfilerAccessor.Current` | 当前请求状态通过 `HttpContext.Items` 保存，并通过 DI accessor 暴露 |
| `_Layout.cshtml` 中的 `Profiler.Current.Elapsed` | `_Layout.cshtml` 调用 `Context.GetGitCandyRequestProfiler()` | 视图不再依赖 legacy `System.Web` 静态上下文 |

## 行为边界

- 本任务只迁移轻量请求耗时探针，不引入 MiniProfiler、OpenTelemetry、metrics backend 或外部观测依赖。
- 本任务不记录请求 URL、header、token、authorization header、用户名或仓库路径，避免把 profiler 变成敏感信息日志入口。
- 本任务不改变公开路由、Identity cookie、Basic Auth、数据库 schema、Git HTTP/SSH 协议行为或文件系统布局。
- 若后续需要 tracing/metrics/logging 统一观测能力，应放到 M9 #095 Observability 独立评估。

## 本任务验证

已运行：

- `dotnet build D:\source\GitCandy\GitCandy.slnx`
- `dotnet test D:\source\GitCandy\GitCandy.slnx --no-build`

验证说明：

- 本机当前安装 `10.0.202` 和 `8.0.414` SDK，仓库根目录直接运行 `dotnet build .\GitCandy.slnx` 会被 `global.json` 要求的 `10.0.301` 阻断。本次验证从 `%TEMP%` 目录启动 `dotnet`，并显式传入 `D:\source\GitCandy\GitCandy.slnx`，不修改 `global.json`。

未运行：

- SQLite 数据读取/写入 smoke test：本切片不改数据访问层；现有 `dotnet test` 覆盖既有数据层测试。
- MVC 登录和主要页面 smoke test：真实登录页面迁移属于后续 M4/M5。
- Git HTTP clone/fetch/push：本切片不改 Git Smart HTTP 协议路径。
- SSH clone/fetch/push：本切片不改 SSH runtime 或 Git command dispatch。

## 兼容性影响

- 新迁移 host 启动后会为每个 HTTP 请求创建一个轻量 `Stopwatch` profiler，并保存在当前 `HttpContext.Items` 中。
- 旧 MVC5 项目保持不变，继续作为 profiler 行为参考。
