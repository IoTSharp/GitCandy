# M2 #022 标准日志

记录日期：2026-07-10

## 验收结论

- ASP.NET Core host 的运行时代码统一通过构造函数注入 `ILogger<T>`。
- 日志级别、筛选和输出目标统一进入 ASP.NET Core logging provider 管线。
- 不保留 `GitCandy.Log.Logger` 静态入口，不保留旧 composite-format 兼容层。
- 不保留 `SetLogPath`、`LogRotationJob` 或 `GitCandy:Application:LogPathFormat`；应用不自行创建和轮转日志文件。

## 行为边界

- 容器使用标准输出，systemd 使用 journald；其他日志目标通过标准 `ILoggerProvider` 配置接入。
- `appsettings.json` 的 `Logging` 节控制日志级别，不保存密码、token、Authorization header 或私钥。
- Quartz.NET 调度基础设施保持独立，不再用无实际工作的日志轮转任务充当生产 job。

## 验证

- 源码门禁确认新项目不存在 `GitCandy.Log.Logger`、`LegacyLogger`、`LogRotationJob` 或 `LogPathFormat` 引用。
- `dotnet build .\GitCandy.slnx` 和 `dotnet test .\GitCandy.slnx` 验证标准日志注入、host 生命周期、Git HTTP、SSH 和 scheduler 测试。

## 兼容性影响

- 旧 `LogPathFormat` 配置不再读取，应用不再创建每日文件日志。
- 部署者应从容器标准输出、journald 或已注册的 ASP.NET Core logging provider 收集日志。
- 本变更不影响公开 URL、Identity cookie、数据库 schema 或 Git HTTP/SSH 协议行为。
