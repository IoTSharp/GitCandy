# M2 #028 启停诊断

记录日期：2026-07-10

## 验收结论

- 新增 `GitCandyHostDiagnosticsHostedService`，通过 ASP.NET Core `IHostedLifecycleService` 记录 GitCandy host starting、started、stopping、stopped 生命周期日志。
- 启动日志包含 environment、application、content root、web root、SSH 开关和 SSH 端口，便于定位宿主环境与后台能力配置。
- `SshServerHostedService` 在 SSH runtime 启动失败时记录端口、端口占用和 host key 配置等排查方向，并继续抛出异常让 host 按失败启动处理。
- `SshServerHostedService` 在停止失败或停止取消时记录诊断日志，避免 graceful shutdown 问题静默发生。
- `QuartzSchedulerRegistrationHostedService` 在注册 scheduler jobs 时记录任务数量；任务名重复、Quartz 注册失败或其他启动异常会先写 error 日志再抛出。
- 新增测试覆盖 host 生命周期日志、SSH 启动失败日志和 scheduler 启动失败日志。

## 行为边界

- 本任务只增强 ASP.NET Core host、SSH lifecycle placeholder 和 Quartz scheduler 注册路径的日志诊断。
- 本任务不迁移真实 SSH listener、host key 加载、public key auth、Git command dispatch 或 SSH clone/fetch/push。
- 本任务不改变 Git HTTP/SSH 协议行为、公开路由、Identity cookie、数据库 schema 或文件系统布局。
- 路径边界和跨宿主路径预测继续属于 M2 #029；本任务仅记录启动期可诊断上下文。

## 本任务验证

已运行：

- `dotnet build D:\source\GitCandy\GitCandy.slnx`
- `dotnet test D:\source\GitCandy\GitCandy.slnx --no-build`

验证结果：

- `GitCandy.Data.Tests`：25 个测试通过。
- `GitCandy.Tests`：34 个测试通过。

未运行：

- SQLite 数据读取/写入 smoke test：本切片不改数据访问层；现有 `dotnet test` 覆盖既有数据层测试。
- MVC 登录和主要页面 smoke test：真实登录页面迁移属于后续 M4/M5。
- Git HTTP clone/fetch/push：本切片不改 Git Smart HTTP 协议路径。
- SSH clone/fetch/push：本切片只增强 SSH lifecycle placeholder 日志，不启动真实 SSH listener。

## 兼容性影响

- 新迁移 host 启停时会额外输出结构化诊断日志。
- SSH runtime 和 scheduler job 注册失败时日志更明确，但异常仍会向上抛出，不隐藏失败。
- 旧 MVC5 项目保持不变，继续作为启停行为参考。
