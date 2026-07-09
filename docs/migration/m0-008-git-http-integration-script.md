# M0 #008 Git HTTP integration script

记录日期：2026-07-09

## 验收结论

- 已新增可重复运行的本地 Git HTTP 集成脚本：`tools/migration/m0-008/Invoke-GitHttpIntegration.ps1`。
- 脚本使用真实 `git` 客户端验证 `clone`、`fetch`、`push`、匿名拒绝、认证拒绝、无权限拒绝和仓库不存在拒绝场景。
- 生成物默认写入 `artifacts/migration/m0-008/work/`，该目录由 `.gitignore` 的 `artifacts/` 规则排除，不提交 clone worktree、日志或本机状态。
- 脚本不启动 GitCandy 应用；执行前需要先启动旧 MVC5 站点或后续 ASP.NET Core 站点，并加载 M0 #001 样例用户、权限和 bare repositories。

## 运行命令

先生成 M0 #001 fixture，并把样例数据加载到当前要验证的 GitCandy 实例。然后设置本机密码环境变量：

```powershell
$env:GITCANDY_M0_ALICE_PASSWORD = '<local-alice-password>'
$env:GITCANDY_M0_BOB_PASSWORD = '<local-bob-password>'
$env:GITCANDY_M0_CAROL_PASSWORD = '<local-carol-password>'
```

执行集成脚本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\migration\m0-008\Invoke-GitHttpIntegration.ps1 -BaseUrl "http://localhost:<port>"
```

只跑匿名场景时可使用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\migration\m0-008\Invoke-GitHttpIntegration.ps1 -BaseUrl "http://localhost:<port>" -SkipAuthenticatedScenarios
```

默认成功 push 会创建临时远端分支并在验证后删除。若需要保留远端分支排查问题，可追加 `-KeepRemoteBranches`。

## 前置条件

- 本机 `git` 可在 `PATH` 中直接运行。
- GitCandy 站点已启动，`-BaseUrl` 指向站点根地址，不包含 `/git`。
- 站点中存在 M0 #001 样例仓库：`public-demo` 和 `private-demo`。
- 站点中存在 M0 #001 样例用户和权限：`alice` 是 owner，`bob` 通过 `core` 团队可读写私有仓库，`carol` 无私有仓库权限。
- 公有仓库允许匿名读、不允许匿名写；私有仓库不允许匿名读写。
- 需要完整场景时，`alice`、`bob`、`carol` 的本机密码通过环境变量或脚本参数提供。

## 覆盖场景

| 场景 | 命令类别 | 期望 |
| --- | --- | --- |
| public anonymous clone | `git clone /git/public-demo.git` | 成功 |
| public anonymous fetch | `git fetch --all --tags` | 成功 |
| public no-suffix clone | `git clone /git/public-demo` | 成功 |
| public anonymous push | `git push` | 失败，匿名无写权限 |
| private anonymous clone | `git clone /git/private-demo.git` | 失败，匿名无读权限 |
| missing repository clone | `git clone /git/missing-demo.git` | 失败 |
| public owner push | `alice` Basic Auth + `git push` | 成功，随后删除临时远端分支 |
| private team clone/fetch | `bob` Basic Auth + `clone/fetch` | 成功 |
| private team push | `bob` Basic Auth + `git push` | 成功，随后删除临时远端分支 |
| private denied user clone | `carol` Basic Auth + `git clone` | 失败 |
| private wrong password clone | `alice` 错误密码 + `git clone` | 失败 |

## 输出

每次运行会清理并重建默认 work root：

- `artifacts/migration/m0-008/work/summary.json`
- `artifacts/migration/m0-008/work/logs/*.log`
- `artifacts/migration/m0-008/work/clones/*`

`summary.json` 记录 run id、Base URL、仓库名、每个步骤的退出码、日志路径和是否跳过认证场景。日志不记录密码或 Authorization header。

## 凭据与日志约束

- 密码不写入仓库文件，也不写入脚本输出。
- 认证场景通过 Git 的 `GIT_CONFIG_KEY_0=http.extraHeader` 和 `GIT_CONFIG_VALUE_0=Authorization: Basic ...` 进程环境传递 Basic Auth header，避免把凭据放进 remote URL。
- 脚本运行时会清空 `GIT_TRACE`、`GIT_TRACE_PACKET`、`GIT_CURL_VERBOSE` 等进程环境，避免 Git 调试输出泄漏认证 header。
- Git credential helper 被禁用，避免测试凭据进入本机 credential store。

## 与 M6 迁移的关系

M0 #008 只建立本地集成脚本，不改变旧 MVC5 Git HTTP 实现。后续 M6 迁移 `GitController.Smart`、Basic/PAT authentication scheme 和 `IGitTransportBackend` 时，应继续使用本脚本验证：

- `/git/{project}.git/{*verb}` 和 `/git/{project}/{*verb}` 两套 URL。
- 公开仓库匿名 clone/fetch 成功。
- 默认公开仓库匿名 push 失败。
- 私有仓库匿名 clone/fetch 失败。
- owner/team/admin 等有写权限用户可以 push。
- 无权限用户和密码错误用户不能 clone/fetch/push 私有仓库。
- 真实 Git 客户端能完成 clone/fetch/push，而不是只通过 controller 单元测试。

## 当前验证

已验证：

- PowerShell parser 可以解析 `tools/migration/m0-008/Invoke-GitHttpIntegration.ps1`。
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\migration\m0-008\Invoke-GitHttpIntegration.ps1 -BaseUrl "http://127.0.0.1:1" -SkipAuthenticatedScenarios`：按预期在首次 `git clone` 失败，并成功生成 `summary.json` 和步骤日志。
- `dotnet build GitCandy.slnx`：通过。
- `dotnet test GitCandy.slnx`：通过，25 个测试通过。

未运行完整 Git HTTP 集成脚本：

- 当前任务没有启动旧 MVC5 GitCandy 站点，也没有本机 M0 #001 样例数据库密码。完整场景需要在目标 GitCandy 实例运行中执行。

## 兼容性影响

本任务只新增本地验证脚本和迁移文档，不改变：

- 公开路由和 Git URL。
- 数据库 schema、索引、默认值或 seed 行为。
- Identity cookie、Basic Auth 或权限语义。
- 配置键、环境变量或部署方式。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。
