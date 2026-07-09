# M2 #026 SSH 生命周期占位

记录日期：2026-07-10

## 验收结论

- 新 ASP.NET Core host 新增 `SshServerHostedService`，将内置 SSH server 启停接入 `IHostedService` 生命周期。
- 新增 `ISshServerRuntime` 作为后续真实 SSH listener 的受控生命周期入口。
- 新增 `PlaceholderSshServerRuntime`，当前只记录启动/停止日志，不监听端口、不处理 SSH 协议、不调用 Git helper。
- `SshServerHostedService` 读取 `GitCandy:Application:EnableSsh` 和 `SshPort`；禁用时不启动 runtime，启用时把配置端口传入 runtime。
- 停止路径将 ASP.NET Core host 的 `CancellationToken` 传给 `ISshServerRuntime.StopAsync`，为后续 graceful shutdown、会话关闭和 listener 停止保留入口。

## 迁移映射

| 旧入口 | 新入口 | 说明 |
| --- | --- | --- |
| `Application_Start` 调用 `SshServerConfig.StartSshServer()` | `SshServerHostedService.StartAsync()` | SSH 生命周期由 ASP.NET Core host 管理 |
| `Application_End` 调用 `SshServerConfig.StopSshServer()` | `SshServerHostedService.StopAsync()` | 应用停止时走 hosted service 停止路径 |
| 静态 `_server` 字段 | `ISshServerRuntime` | 后续真实 listener 应封装在 runtime 内，不散落到 controller 或后台任务 |
| `UserConfiguration.Current.EnableSsh/SshPort` | `GitCandyApplicationOptions.EnableSsh/SshPort` | 配置已由 M2 #020 迁移到 strongly typed options |

## 行为边界

- 本任务只建立生命周期占位，不迁移旧 `GitCandy/Ssh/*` 协议栈。
- 本任务不开放交互 shell、SFTP、端口转发或 SSH 密码登录。
- 本任务不实现 host key、public key authentication、SSH key schema、仓库权限判断或 Git command dispatch。
- 本任务不改变 Git HTTP、Identity cookie、数据库 schema、公开 URL 或仓库文件系统布局。
- 后续 M7 #070 到 #079 需要把真实 listener、host key、public key auth、权限服务、`IGitTransportBackend`、clone/fetch/push 验证和端口冲突诊断接到当前 runtime 入口。

## 本任务验证

已运行：

- `dotnet build D:\source\GitCandy\GitCandy.slnx`：通过，0 警告/0 错误。命令从 `%TEMP%` 目录启动，以避开仓库 `global.json` 对未安装 SDK `10.0.301` 的解析。
- `dotnet test D:\source\GitCandy\GitCandy.slnx --no-build`：通过，`GitCandy.Data.Tests` 25 个测试，`GitCandy.Tests` 24 个测试。

未运行：

- 仓库根目录直接运行 `dotnet build .\GitCandy.slnx`：未通过，原因是本机只安装 `8.0.414` 和 `10.0.202` SDK，而 `global.json` 要求 `10.0.301`。本任务不修改 `global.json`。
- MVC 登录和主要页面 smoke test：本切片只接入 SSH lifecycle hosted service，不迁移登录页面。
- Git HTTP clone/fetch/push：本切片不改 Git HTTP 协议路径。
- SSH clone/fetch/push：本切片只建立生命周期占位，不启动真实 SSH listener。

## 兼容性影响

- 新迁移 host 启动后会注册 SSH lifecycle hosted service；`EnableSsh=false` 时不会启动 runtime。
- 默认 `PlaceholderSshServerRuntime` 不监听端口，因此不会改变当前部署的网络暴露面。
- 旧 MVC5 项目保持不变，继续作为 SSH 行为参考。
