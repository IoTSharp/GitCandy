# M2 #022 日志适配

记录日期：2026-07-09

## 验收结论

- 新 ASP.NET Core host 新增 `GitCandy.Log.Logger` 迁移期兼容适配器，保留旧 `Logger.Info`、`Logger.Warning`、`Logger.Error`、`Logger.Write` 和 `Logger.SetLogPath` API 形状。
- 适配器在 `Program.cs` 构建应用后绑定 `ILoggerFactory`，旧静态日志调用会进入标准 ASP.NET Core logging pipeline。
- 旧 `{0}` composite format 调用会先按旧语义格式化，再写入 `ILogger`，便于后续迁移旧 controller、scheduler、SSH 和 cache 代码时逐步替换为构造函数注入的 `ILogger<T>`。
- `SetLogPath` 只作为兼容入口保留；新宿主的日志输出位置由 `Logging` 配置和 logging provider 决定，不再由静态文件写入器管理。

## 行为边界

- 本任务不迁移 scheduler、SSH server、Git HTTP、cache 或旧 MVC5 调用点。
- 本任务不引入第三方日志库，也不新增文件日志 provider。
- `GitCandy:Application:LogPathFormat` 仍作为配置迁移记录保留；路径解析、跨宿主路径验证和日志文件 sink 若需要，后续按独立任务处理。

## 本任务验证

已运行：

- `dotnet test .\GitCandy.slnx`：通过，覆盖旧 composite format、日志级别映射、ASP.NET Core host 绑定和 `System.Web` 门禁。
- `dotnet build .\GitCandy.slnx`：通过，Debug 构建 0 警告/0 错误。

未运行：

- SQLite 数据读取/写入 smoke test：#022 不改变数据层行为，现有数据层测试随 `dotnet test` 覆盖。
- MVC 登录和主要页面 smoke test：真实登录页面迁移属于后续 M4/M5。
- Git HTTP clone/fetch/push：#022 不改变 Git Smart HTTP 运行时代码，M6 单独验收。
- SSH clone/fetch/push：#022 不改变 SSH 运行时代码，M7 单独验收。

## 兼容性影响

- 新 host 中的旧静态 `Logger` 调用不会再直接创建每日日志文件；输出目标改由 ASP.NET Core logging provider 统一控制。
- 旧 MVC5 项目的 `GitCandy\Log\Logger.cs` 未改动，仍仅作为迁移前行为参考。
