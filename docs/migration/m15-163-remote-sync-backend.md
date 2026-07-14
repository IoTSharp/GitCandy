# M15 #163 受控 remote sync backend

## 对应 ROADMAP

- Milestone 15 / #163：受控 remote sync backend。
- 本切片只建立远程 Git `fetch`/`push` 的唯一进程边界，不创建 mirror 配置 UI、初始导入、定时任务、post-receive 入队、webhook 或运维操作。

## 变更点

- 新增 `IRemoteRepositorySyncBackend`，只接受结构化 `Fetch`/`Push`、固定 provider origin、HTTPS（或测试用 loopback HTTP）remote URL 和经过验证的 refspec；不接受任意 Git 参数或 shell command。
- `GitProcessRemoteRepositorySyncBackend` 使用 `ProcessStartInfo.ArgumentList` 启动 Git，工作目录必须是 `IManagedGitRepositoryService` 重新验证过的现有 bare repository。跨 provider origin、URL user info/query/fragment、非法 ref、fetch 删除和隐式 push prune 均在启动进程前拒绝。
- Git stdout/stderr 全程异步排空，不把 pack 或无界诊断读入内存。stderr 只保留有界尾部用于稳定错误分类，原文不写日志、不进入异常或 MVC 输出。
- 每次执行使用独立 timeout 和调用方 `CancellationToken`；取消或超时会终止整个 Git 进程树。#166 再负责实例级并发、remote 串行、lease、重试和重启恢复。
- 禁用交互提示、authenticated redirect、系统/用户 Git 配置和 repository hooks；只允许 HTTP/HTTPS transport。SSH remote credential 后端不在本切片内。

## Credential helper 边界

- token 不进入 remote URL、`ArgumentList`、环境变量、Git config、临时文件或日志。
- Git 调用同一 GitCandy 二进制的短生命周期 `--git-remote-credential-helper` 子命令。子命令不启动 Web host、DI 或数据库，只实现 Git 标准 credential-helper `get` 协议。
- 主进程通过随机命名、`CurrentUserOnly` 的一次性命名管道发送一次 username/token；helper 会同时校验 Git 请求的 protocol、host 和 path，防止凭据被转交到另一 origin 或 repository URL。
- provider 用户名固定为 GitHub `x-access-token`、GitLab/Gitee `oauth2`；PAT、OAuth 和 App token 使用相同无明文持久化边界。SSH credential 显式返回不支持。
- `CachePath/remote-sync-runtime` 只保存每次执行创建的空 Git config 和空 hooks 目录，正常结束后删除；其中不包含 token，不能作为凭据备份来源。

## 配置

`GitCandy:Remotes` 新增：

| 键 | 默认值 | 边界 |
| --- | --- | --- |
| `OperationTimeout` | `00:30:00` | 1 秒至 24 小时 |
| `StreamBufferSize` | `81920` | 4 KiB 至 1 MiB |
| `MaxDiagnosticCharacters` | `8192` | 1024 至 65536 字符 |

`RequestTimeout` 仍只控制 provider API；`OperationTimeout` 控制 Git fetch/push 子进程。反向代理 timeout 不影响后台 remote sync。

## 错误与日志

backend 只返回稳定错误码：认证失败、权限拒绝、仓库不存在、non-fast-forward、网络、TLS、超时、不支持的凭据、进程启动失败和一般进程失败。日志只包含本地仓库逻辑名称、provider、操作、稳定错误码、exit code 和时长，不包含 remote URL、物理路径、refspec、stderr 或 token。

## 数据与兼容性

- 不新增或修改 EF migration，不读取或更新 `RepositoryMirrors` 状态，不访问现有连接，只有后续应用服务显式调用 backend 时才会产生网络与 Git ref 变更。
- 不改变 Web、Git Smart HTTP、LFS 或 SSH 的公开 URL、认证、权限和流式行为。
- 仍只规划 Git refs；LFS、Issues、PR/MR、Wiki、Releases、CI 和 Packages 不随 remote sync 执行。

## 回滚

回滚到上一应用版本即可移除 backend 和 helper 子命令，不需要数据库回滚。停止应用并确认没有远程 Git 子进程后，可删除 `CachePath/remote-sync-runtime` 的无凭据残留目录；不要删除 Data Protection key ring 或 `remote-credentials`，它们仍由 #161 的账号连接使用。

## 验证

- 真实 Git fetch/push + loopback Kestrel：`git-upload-pack`/`git-receive-pack` Basic challenge 后由 credential helper 提供 token，远端只在 Authorization header 收到凭据，URL 和捕获日志均无 token。
- 专项测试覆盖 404 分类、超时杀进程树、调用方取消、跨 origin、恶意 refspec、fetch 删除和 repository root 逃逸。
- 最终 `dotnet build`、完整 `dotnet test` 和未执行项以本次变更说明为准。
