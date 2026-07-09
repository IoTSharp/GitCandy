# M2 #025 Quartz.NET Scheduler hosted service

记录日期：2026-07-10

## 验收结论

- 新 ASP.NET Core host 引入 `Quartz.Extensions.Hosting`，并通过 `AddQuartz` / `AddQuartzHostedService` 接入应用启动和停止生命周期。
- 第一阶段使用 Quartz.NET in-memory store，不新增 Quartz 持久化 schema，不混入 EF Core / Identity migration。
- 新增 `QuartzSchedulerJob` bridge，把 Quartz job 执行转发到 DI 中注册的 `ISchedulerJob`，保留 #024 建立的迁移期任务入口。
- 新增 `QuartzSchedulerRegistrationHostedService`，启动时枚举 `IEnumerable<ISchedulerJob>` 并注册 durable Quartz job 与初始 trigger。
- `LogRotationJob` 继续作为迁移期 smoke job；首次运行只建立执行上下文，后续按 `GetNextInterval` 继续调度。

## 迁移映射

| 旧入口 | 新入口 | 说明 |
| --- | --- | --- |
| `ScheduleConfig.RegisterScheduler()` | `AddGitCandyWebShell()` 内部注册 Quartz.NET | 新 host 不再依赖 `Application_Start` 手动启动 scheduler |
| `Scheduler.Instance.StartAll()` | `AddQuartzHostedService()` | scheduler 生命周期由 ASP.NET Core host 管理 |
| MEF `IJob` runner | `ISchedulerJob` + `QuartzSchedulerJob` | 任务发现来自 ASP.NET Core DI，Quartz 负责触发和并发边界 |
| 自写 runner 队列 | Quartz in-memory job store | 第一阶段不启用 Quartz 数据库存储、集群或管理 UI |

## 行为边界

- 本任务不改变数据库 schema、Identity cookie、Git HTTP/SSH 协议、公开路由或文件系统布局。
- 本任务不引入 Quartz 持久化表；如果后续需要持久化、集群或管理 UI，应单独评估 schema、迁移和回滚方案。
- 本任务不完成 M7 #076 的完整取消诊断；当前 bridge 已把 `IJobExecutionContext.CancellationToken` 传入 `ISchedulerJob.ExecuteAsync`，但更细的停止等待、取消日志和长任务策略留给 #076。
- 本任务不迁移内置 SSH 生命周期；SSH hosted service 属于 #026 及 M7 后续切片。

## 本任务验证

已运行：

- `dotnet build D:\source\GitCandy\GitCandy.slnx`：通过，0 警告/0 错误。
- `dotnet test D:\source\GitCandy\GitCandy.slnx --no-build`：通过，`GitCandy.Data.Tests` 25 个测试、`GitCandy.Tests` 22 个测试。
- `git diff --check`：通过；仅输出既有 Git 行尾规范提示。

验证说明：

- 仓库根目录直接运行 `dotnet build .\GitCandy.slnx` 会被 `global.json` 要求的 SDK `10.0.301` 阻断；本机安装的是 `10.0.202` 和 `8.0.414`。本次构建/测试从 `%TEMP%` 目录启动 `dotnet` 并显式传入 solution 绝对路径，使用已安装的 `10.0.202` SDK 完成验证，不修改 `global.json`。

未运行：

- MVC 登录和主要页面 smoke test：本切片只接入 scheduler 生命周期，不迁移真实登录页面。
- Git HTTP clone/fetch/push：本切片不改变 Git HTTP 协议路径。
- SSH clone/fetch/push：本切片不改变 SSH 运行时代码。

## 兼容性影响

- 新迁移 host 新增运行时依赖 `Quartz.Extensions.Hosting`。
- 新迁移 host 启动后会启动 Quartz in-memory scheduler 并注册 DI 中的 scheduler job；旧 MVC5 项目保持不变，继续作为行为参考。
