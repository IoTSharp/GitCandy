# M9 #095 Observability

## 变更点

- composition root 注册 OpenTelemetry tracing、metrics 和 logging provider，resource service name 默认为 `GitCandy`。
- tracing 覆盖 ASP.NET Core 请求、共享 Git transport backend 和 Quartz job；`/health/live` 不创建高频 request span。
- metrics 覆盖 ASP.NET Core、.NET runtime，以及 Git transport/Quartz 的操作数、活跃数、结果和耗时。
- OpenTelemetry logging provider 复用现有结构化 `ILogger<T>` 日志和 trace/span correlation，不恢复旧静态 logger 或应用内日志文件。
- 支持可选 OTLP 与诊断 Console exporter；两者默认关闭，collector 和 dashboard 不进入 GitCandy 单进程部署边界。

## 依赖评估

新增依赖均为 OpenTelemetry .NET 1.16.0 包：hosting、ASP.NET Core/runtime instrumentation、OTLP exporter 和 Console exporter。它们提供标准信号模型、批量 exporter、W3C trace context 和 collector 兼容能力；用自定义 exporter 重写这些能力会增加协议、重试和生命周期维护成本。Git transport 模块只增加 BCL `System.Diagnostics.ActivitySource`/`Meter`，不依赖 OpenTelemetry SDK，保持 #094 的项目依赖方向。

Console exporter 只用于本地诊断。OTLP 是唯一网络导出路径，支持接入 OpenTelemetry Collector 后再转发到具体 observability backend；本任务不绑定 Prometheus、Jaeger、Grafana、Application Insights 或商业平台。

## 信号与安全边界

| 信号 | Source/Meter | 关键字段 |
| --- | --- | --- |
| HTTP trace/metrics | ASP.NET Core instrumentation | framework 标准 method、route、status、duration |
| Git trace | `GitCandy.Git.Transport` | service、advertise refs、result |
| Git metrics | `gitcandy.git.transport.*` | service、result、duration、active operations |
| Scheduler trace | `GitCandy.Scheduler` | result |
| Scheduler metrics | `gitcandy.scheduler.*` | result、duration、active executions |
| Logs | OpenTelemetry logging provider | message template、structured state、scope、trace/span correlation |

自定义 Git/scheduler telemetry 不记录 repository 名称、actor、文件系统路径、header、token、SSH key 或进程 stderr。错误 activity 只标记异常类型，不写异常消息。现有 `ILogger<T>` 的业务日志行为未在本切片改变；生产 collector 必须作为受保护的运维系统管理。

## 配置、兼容和回滚

新增 `GitCandy:Observability` 配置节，不改变公开 URL、Identity/Basic Auth、权限语义、EF Core schema、文件布局或 Git HTTP/SSH framing。默认 provider 开启但 exporter 关闭，现有部署的 stdout/journald/Windows logging 行为保持不变。

启用 OTLP 只需配置 `GitCandy__Observability__Otlp__Enabled=true` 和标准 `OTEL_EXPORTER_OTLP_*` 环境变量。配置的 endpoint 必须是绝对 HTTP/HTTPS URI，trace sampling ratio 必须在 `0..1`；非法配置在 host 启动时失败。回滚先关闭 OTLP exporter，必要时关闭整个 observability provider 并重启；没有数据库、repository 或持久化数据回滚步骤。

## 验证

- `dotnet build GitCandy.slnx`：0 warning / 0 error。
- `dotnet test tests/GitCandy.Tests/GitCandy.Tests.csproj --filter FullyQualifiedName~ObservabilityTests`：4/4 passed。
- Git telemetry 失败路径测试验证 activity/error counter/duration，并确认导出标签不含 repository、actor 或物理路径。
- `dotnet test GitCandy.slnx --no-build`：Data 41/41、Web/协议 71/71，共 112/112 passed，0 skipped；包含 SQLite、MVC、Git HTTP clone/fetch/push 和 SSH clone/fetch/push 覆盖。
- `dotnet list GitCandy.slnx package --vulnerable --include-transitive`：11 个项目均无已知 vulnerable package。
- 临时 SQLite/随机 HTTP 端口真实 host smoke：`/health/live` 和 `/health/ready` 均为 HTTP 200，Console exporter 实际输出 trace、metrics 和 OpenTelemetry log record。
