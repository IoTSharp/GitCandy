# M0 #004 SSH 行为清单

记录日期：2026-07-09

## 验收结论

- 已记录旧 ASP.NET MVC 5 内置 SSH 行为基线，覆盖 SSH clone、fetch、push、host key、端口、public key 认证、Git 命令分派和权限语义。
- 本清单作为后续 ASP.NET Core 内置 SSH hosted service 迁移的行为保护网。迁移实现可以重建生命周期、DI 接入和 Git transport backend，但 SSH URL、host key 管理、认证方式、权限语义、禁用交互能力和 clone/fetch/push 行为必须能对照本文件说明差异。
- 本任务只做静态行为梳理，没有启动旧 MVC5 站点执行真实 SSH `git clone/fetch/push`。M0 #001 样例数据当前不自动生成 SSH key；执行手动验证前需要先给样例用户添加本机 public key。
- Git Smart HTTP 行为不在本文件展开，由 M0 #003 和 M0 #008 覆盖。

## 行为来源

主要读取的旧实现：

- `GitCandy/Global.asax.cs`
- `GitCandy/App_Start/SshServerConfig.cs`
- `GitCandy/Ssh/SshServer.cs`
- `GitCandy/Ssh/Session.cs`
- `GitCandy/Ssh/StartingInfo.cs`
- `GitCandy/Ssh/KeyUtils.cs`
- `GitCandy/Ssh/Services/UserauthService.cs`
- `GitCandy/Ssh/Services/ConnectionService.cs`
- `GitCandy/Ssh/Services/Channel.cs`
- `GitCandy/Git/GitSshService.cs`
- `GitCandy/Data/MembershipService.cs`
- `GitCandy/Data/RepositoryService.cs`
- `GitCandy/Controllers/SettingController.cs`
- `GitCandy/Controllers/AccountController.cs`
- `GitCandy/Controllers/RepositoryController.cs`
- `GitCandy/Configuration/UserConfiguration.cs`
- `GitCandy/Configuration/HostKeyResloverAttribute.cs`
- `GitCandy/Configuration/ConfigurationEntry.cs`
- `GitCandy/Models/SettingModel.cs`
- `GitCandy/DAL/SshKey.cs`
- `GitCandy/DAL/Mapping/SshKeyMap.cs`
- `Sql/Create.Sqlite.sql`
- `Sql/Create.MsSql.sql`

## 启动、端口和 host key

### 生命周期

旧站点的 SSH server 随 ASP.NET MVC 5 Web 应用进程启动和停止：

| 生命周期点 | 旧行为 |
| --- | --- |
| `Application_Start` | 调用 `SshServerConfig.StartSshServer()`。 |
| `Application_End` | 调用 `SshServerConfig.StopSshServer()`，随后停止 scheduler 并杀掉当前进程。 |
| 设置页修改 `SshPort` 或 `EnableSsh` | 保存配置后调用 `SshServerConfig.RestartSshServer()`。 |
| 设置页修改 `CachePath` | 先停止 SSH server，再 `HttpRuntime.UnloadAppDomain()`。 |
| 设置页 `ReGenSsh` | 重新生成所有支持算法的 host key，保存配置，然后重启 SSH server。 |

迁移目标仍应保持单 GitCandy 应用进程承载 Web、Git HTTP、内置 SSH 和后台任务；ASP.NET Core 迁移时应把旧静态启动迁移为 `IHostedService` 或 `BackgroundService`。

### 配置默认值

| 配置 | 旧默认值 | 备注 |
| --- | --- | --- |
| `EnableSsh` | `true` | 关闭时 `StartSshServer()` 直接返回。 |
| `SshPort` | `22` | `SettingModel` 校验范围是 `1` 到 `65534`。 |
| `HostKeys` | 自动生成 `ssh-rsa` 和 `ssh-dss` | 新建 `~\App_Data\config.xml` 时由 `HostKeyResloverAttribute` 生成。 |
| `RepositoryPath` | 配置文件路径 | SSH helper 的仓库实参由 `Path.Combine(RepositoryPath, project)` 得到。 |
| `GitCorePath` | Git core 路径 | SSH 直接调用 `git-upload-pack.exe`、`git-receive-pack.exe` 或 `git-upload-archive.exe`。 |

`UserConfiguration` 通过 `Web.config` 中的 `UserConfiguration` appSetting 指向 `~\App_Data\config.xml`。host key 私钥以 XML 形式保存在该配置文件中；迁移时不得记录、提交或输出 host key 私钥内容。

### Listener 行为

- 启用 SSH 且当前没有 `_server` 实例时，旧代码创建 `SshServer(new StartingInfo(IPAddress.IPv6Any, SshPort))`。
- `SshServer` 在 `IPAddress.IPv6Any` 下使用 `TcpListener.Create(port)`，注释标记为 dual stack。
- socket 设置 `ExclusiveAddressUse=false` 和 `ReuseAddress=true`。
- 启动最多重试 10 次，每次失败后等待 1 秒并记录 `Attempt to start SSH server failed...`。
- 成功后记录 `SSH server started.`；停止后记录 `SSH server stoped.`。
- 如果 10 次全部失败，旧实现没有抛出最终异常，也没有明确重置 `_server`，这是端口占用诊断和重试语义的迁移风险。

### Host key 行为

- 支持的 server host key 算法来自 `KeyUtils.SupportedAlgorithms`：`ssh-rsa`、`ssh-dss`。
- `ReGenSsh` 会清空旧 host keys，并分别调用 `KeyUtils.GeneratePrivateKey(type)` 生成新私钥。
- host key 变化会导致客户端 `known_hosts` 记录失效；迁移文档或发布说明应提示部署者备份和迁移 host key。
- 旧实现不提供 host key 指纹展示页，只在 SSH 握手中向客户端发送 host key。

## SSH 协议能力

### 协议和算法

| 能力 | 旧行为 |
| --- | --- |
| Server version | `SSH-2.0-FxSsh`。 |
| SSH 版本 | 只接受 `SSH-2.0-*` 客户端。 |
| Key exchange | `diffie-hellman-group14-sha1`、`diffie-hellman-group1-sha1`。 |
| Host key | `ssh-rsa`、`ssh-dss`。 |
| Encryption | `aes128-ctr`、`aes192-ctr`、`aes256-ctr`、`aes128-cbc`、`3des-cbc`、`aes192-cbc`、`aes256-cbc`。 |
| MAC | `hmac-md5`、`hmac-sha1`。 |
| Compression | 只支持 `none`。 |
| Socket timeout | Release 构建 30 秒，Debug 构建 1 天。 |

这些算法反映旧行为，不代表迁移目标应继续使用弱算法。若 ASP.NET Core 迁移时替换 SSH 协议栈或调整算法集，必须说明兼容性影响、host key 迁移方式和 Git 客户端验证结果。

### 禁用能力

旧 SSH server 只处理 Git 客户端需要的 session + exec 流程：

| 请求类型 | 旧行为 |
| --- | --- |
| `session` channel | 接受并返回 open confirmation。 |
| `exec` request | 解析命令并尝试启动 Git helper。 |
| `shell` request | 返回 channel failure 后断开。 |
| `subsystem` / SFTP | 返回 channel failure 后断开。 |
| `direct-tcpip` / port forwarding | channel open failure 后断开。 |
| SSH password 登录 | 返回 userauth failure，不允许。 |
| SSH `none` / `hostbased` | 返回 userauth failure，不允许。 |

迁移目标必须继续默认禁用交互 shell、SFTP、端口转发和 SSH 密码登录。

## 用户 SSH key 管理

### Web 管理入口

| Endpoint | 权限 | 行为 |
| --- | --- | --- |
| GET `/Account/Ssh/{name?}` | 当前用户或系统管理员 | 显示该用户 SSH key fingerprint 列表。 |
| POST `/Account/ChooseSsh` with `act=add` | 当前用户或系统管理员 | 提交 OpenSSH public key 文本，成功返回 fingerprint。 |
| POST `/Account/ChooseSsh` with `act=del` | 当前用户或系统管理员 | 按 fingerprint 删除 key，成功返回 JSON `"success"`。 |

新增 key 的解析行为：

- `MembershipService.AddSshKey` 对输入调用 `sshkey.Split()`，取第一个片段作为 `KeyType`，第二个片段作为 base64 `PublicKey`。
- fingerprint 是 public key base64 解码后做 MD5，再以冒号分隔的十六进制字符串保存。
- `SshKeys.ImportData` 和 `SshKeys.LastUse` 新增时都写 `DateTime.UtcNow`。
- 旧认证路径没有更新 `LastUse`。
- 旧 schema 没有在 SQL 脚本中为 fingerprint 或 public key 建唯一索引。

### SSH public key 认证

认证流程：

1. 客户端请求 `ssh-userauth` 服务。
2. 仅 `publickey` 方法会继续处理；`password`、`hostbased` 和 `none` 都失败。
3. 客户端未附签名时，若 key 算法是 server 支持的 `ssh-rsa` 或 `ssh-dss`，返回 `SSH_MSG_USERAUTH_PK_OK`。
4. 客户端附签名时，server 验证签名。
5. 签名有效后，`GitSshService.Userauth` 调用 `MembershipService.HasSshKey(fingerprint)`。
6. 任意用户存在相同 fingerprint 时，SSH userauth 成功并注册 `ssh-connection` 服务。

重要兼容点：

- SSH 登录用户名没有参与认证或授权判断。UI 生成 `git@host`，但旧协议代码实际只看 public key fingerprint 和 key bytes。
- 认证阶段只检查 fingerprint 是否存在；命令授权阶段才同时检查 fingerprint 和 public key base64 是否匹配具备仓库权限的用户或团队成员。
- 未注册 key 在 SSH userauth 阶段失败；因此旧 SSH 没有真正的匿名连接，即使仓库允许匿名读，也需要至少使用一个已注册 public key 完成 SSH 认证。

## Git 命令和 URL 行为

### UI 展示的 SSH URL

仓库 Tree 根页面和空仓库页面会在 `EnableSsh=true` 时显示 SSH URL：

| 端口 | SSH URL |
| --- | --- |
| `SshPort=22` | `git@{host}:git/{name}.git` |
| 非 22 | `ssh://git@{host}:{port}/git/{name}.git` |

HTTP URL 同时仍显示为 `/git/{name}.git`。SSH URL 的 `.git` 后缀是兼容重点。

### 命令解析

`GitSshService` 使用以下正则解析 exec 命令：

```text
(?<cmd>git-receive-pack|git-upload-pack|git-upload-archive) \'/?git/(?<proj>.+)\.git\'
```

支持的命令：

| SSH exec command | 权限要求 | Git helper |
| --- | --- | --- |
| `git-upload-pack '/git/{repo}.git'` | 读权限 | `{GitCorePath}\git-upload-pack.exe` |
| `git-receive-pack '/git/{repo}.git'` | 写权限 | `{GitCorePath}\git-receive-pack.exe` |
| `git-upload-archive '/git/{repo}.git'` | 读权限 | `{GitCorePath}\git-upload-archive.exe` |

兼容风险：

- 旧 SSH 正则要求路径带 `.git` 后缀；`ssh://git@host/git/{repo}` 这类不带 `.git` 的 remote 可能匹配失败。
- 旧 SSH 正则要求 Git 客户端命令使用单引号包裹路径；其他 quoting 形式可能匹配失败。
- `project` 捕获值直接参与 `Path.Combine(RepositoryPath, project)`，旧实现没有路径归一化和 repository 根目录边界检查。迁移时必须修复路径逃逸风险，并在兼容性说明中标为安全增强。

### Git helper streaming

旧 SSH Git helper 行为：

- 通过 `ProcessStartInfo(Path.Combine(GitCorePath, command + ".exe"), repositoryPath)` 启动 Git helper。
- `UseShellExecute=false`，但外部程序参数仍是单个字符串，不是结构化 `ArgumentList`。
- channel data 直接写入 helper stdin。
- helper stdout 以 64 KiB buffer 循环读取并发送为 SSH channel data。
- helper stdout 结束后发送 EOF、exit status 和 channel close。
- stderr 被重定向但没有读取或记录。
- 旧实现没有显式 timeout、取消令牌、限流或 graceful shutdown。

迁移时 Git HTTP 和 SSH 必须共用 `IGitTransportBackend` 或等价受控抽象，并使用结构化参数、路径边界检查、流式转发和可诊断日志。

## 权限语义

### 读权限

SSH `git-upload-pack` 和 `git-upload-archive` 调用 `RepositoryService.CanReadRepository(reponame, fingerprint, publickey)`：

- 仓库 `AllowAnonymousRead=true` 时允许读取，但前提是 SSH 连接已经用任意已注册 key 完成认证。
- 某个用户拥有匹配 fingerprint/public key，且该用户的直接仓库角色 `AllowRead=true` 时允许读取。
- 某个团队成员拥有匹配 fingerprint/public key，且该团队的仓库角色 `AllowRead=true` 时允许读取。

### 写权限

SSH `git-receive-pack` 调用 `RepositoryService.CanWriteRepository(reponame, fingerprint, publickey)`：

- 仓库 `AllowAnonymousRead=true` 且 `AllowAnonymousWrite=true` 时允许写入，但前提同样是 SSH 连接已经用任意已注册 key 完成认证。
- 某个用户拥有匹配 fingerprint/public key，且该用户的直接仓库角色 `AllowRead=true` 且 `AllowWrite=true` 时允许写入。
- 某个团队成员拥有匹配 fingerprint/public key，且该团队的仓库角色 `AllowRead=true` 且 `AllowWrite=true` 时允许写入。

### 和 HTTP/WEB 权限的差异

旧 SSH key 权限和用户名权限存在重要差异：

- SSH 权限重载没有系统管理员兜底查询。系统管理员的 SSH key 如果没有直接用户角色或团队角色，不能仅凭管理员身份读写私有仓库。
- `IsPrivate` 不直接参与 Git HTTP/SSH 权限判断；匿名或 key 用户能否读写由 `AllowAnonymousRead`、`AllowAnonymousWrite` 和角色决定。
- 旧 SSH 授权不映射到明确用户名，只通过 fingerprint/public key 从用户或团队角色反查权限。

迁移时可以修复这些差异，但必须在权限迁移说明中明确：哪些是旧行为兼容，哪些是安全或一致性增强。

## 错误和失败行为

| 场景 | 旧行为 |
| --- | --- |
| `EnableSsh=false` | 不启动 SSH listener；客户端连接失败。 |
| 端口被占用 | 启动重试并写日志；最终行为不够明确。 |
| 客户端 SSH 版本不是 2.0 | 断开，reason 为 `ProtocolVersionNotSupported`。 |
| 算法协商失败 | 断开，reason 为 `KeyExchangeFailed`。 |
| 未注册 public key | 发送 userauth failure 后断开。 |
| password 登录 | 发送 userauth failure。 |
| 不支持的 channel/request | 发送 failure 或 open failure 后断开。 |
| 不符合正则的 Git 命令 | 抛出 `Unexpected command.` 并断开。 |
| 仓库无读写权限 | 抛出 `Access denied.` 并断开。 |
| Git helper 失败 | stdout 结束后尝试返回 helper exit status；stderr 不写入诊断日志。 |

Git 客户端看到的错误文本取决于 OpenSSH/Git 版本，M0 保护重点是成功场景、认证失败、权限失败和禁止交互能力的行为类别。

## M0 样例数据 SSH 场景

以下场景基于 M0 #001 样例用户和仓库，并假设旧站点以 `EnableSsh=true` 运行。`alice` 是两个样例仓库 owner；`bob` 通过 `core` 团队获得 `private-demo` 读写权限；`carol` 没有 `private-demo` 权限。

执行前置条件：

- 为 `alice`、`bob`、`carol` 分别准备本机 SSH key。
- 通过 `/Account/Ssh/{name}` 或 `/Account/ChooseSsh` 把对应 public key 添加到旧 GitCandy 用户。
- 不把 private key、host key 私钥或真实密码写入仓库。

| 编号 | 场景 | 请求/命令 | 旧行为期望 |
| --- | --- | --- | --- |
| GITSSH-001 | 首次连接 host key | `ssh -p <port> git@localhost` | 客户端看到 `SSH-2.0-FxSsh` server host key；首次连接需要接受 known_hosts。 |
| GITSSH-002 | 匿名或未注册 key clone 公开仓库 | 使用未注册 key clone `public-demo` | public key 认证失败，clone 失败。 |
| GITSSH-003 | 注册 key clone 公开仓库 | 使用 `alice` key clone `git@localhost:git/public-demo.git` | `git-upload-pack` 读权限通过，clone 成功。 |
| GITSSH-004 | 注册 key fetch 公开仓库 | 已 clone 后执行 `git fetch --all --tags` | `git-upload-pack` 读权限通过，fetch 成功。 |
| GITSSH-005 | owner push 公开仓库 | 使用 `alice` key push `public-demo` | `git-receive-pack` 写权限通过，push 成功。 |
| GITSSH-006 | 注册但无写权限 key push 公开仓库 | 使用 `carol` key push `public-demo` | 默认公开仓库 `AllowAnonymousWrite=false`，写权限失败并断开。 |
| GITSSH-007 | owner clone 私有仓库 | 使用 `alice` key clone `private-demo` | 直接 owner/read 权限通过，clone 成功。 |
| GITSSH-008 | 团队成员 clone 私有仓库 | 使用 `bob` key clone `private-demo` | 团队仓库读权限通过，clone 成功。 |
| GITSSH-009 | 团队成员 push 私有仓库 | 使用 `bob` key push `private-demo` | 团队仓库写权限通过，push 成功。 |
| GITSSH-010 | 无权限用户访问私有仓库 | 使用 `carol` key clone/fetch/push `private-demo` | key 认证成功，但仓库权限失败并断开。 |
| GITSSH-011 | 不存在仓库 | 使用注册 key clone `missing-demo` | 权限查询找不到可读仓库，访问失败并断开。 |
| GITSSH-012 | 不带 `.git` 后缀 | clone `ssh://git@localhost:<port>/git/public-demo` | 旧命令解析通常失败，收到 `Unexpected command.` 类失败。 |
| GITSSH-013 | upload-archive | `git archive --remote=ssh://git@localhost:<port>/git/public-demo.git HEAD` | 走 `git-upload-archive`，需要读权限。 |
| GITSSH-014 | 交互 shell | `ssh -p <port> git@localhost` | 不提供 shell，请求失败并断开。 |
| GITSSH-015 | password 登录 | `ssh -o PreferredAuthentications=password ...` | SSH password 认证失败。 |
| GITSSH-016 | host key 重新生成 | 执行 `Setting/ReGenSsh?Conform=Yes` 后重新连接 | 客户端 known_hosts 校验应提示 host key 已变化。 |

## 手动验证命令草案

后续 M7 #078 会在 SSH hosted service 迁移时建立可重复的 SSH clone/fetch/push 验证。手动执行旧行为验证时先启动旧 MVC5 GitCandy 站点，准备 M0 #001 样例数据和 bare repositories，并把 public key 添加到对应用户。

```powershell
$port = 22
$work = ".\artifacts\migration\m0-004"
New-Item -ItemType Directory -Force $work | Out-Null

$env:GIT_SSH_COMMAND = "ssh -p $port -i `"$env:GITCANDY_M0_ALICE_SSH_KEY`" -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new"
git clone "git@localhost:git/public-demo.git" "$work\public-alice"
git -C "$work\public-alice" fetch --all --tags

# 非 22 端口使用 ssh:// URL 形式。
git clone "ssh://git@localhost:$port/git/private-demo.git" "$work\private-alice"
```

切换用户 key 时只替换 `GIT_SSH_COMMAND` 中的 private key 路径：

```powershell
$env:GIT_SSH_COMMAND = "ssh -p $port -i `"$env:GITCANDY_M0_BOB_SSH_KEY`" -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new"
git clone "ssh://git@localhost:$port/git/private-demo.git" "$work\private-bob"

$env:GIT_SSH_COMMAND = "ssh -p $port -i `"$env:GITCANDY_M0_CAROL_SSH_KEY`" -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new"
git clone "ssh://git@localhost:$port/git/private-demo.git" "$work\private-carol-denied"
```

调试 SSH 和 Git 协议时可临时启用：

```powershell
$env:GIT_TRACE = "1"
$env:GIT_TRACE_PACKET = "1"
$env:GIT_SSH_COMMAND = "$env:GIT_SSH_COMMAND -vvv"
```

## ASP.NET Core 迁移验收要求

M7 SSH 迁移时至少要保留或明确说明以下行为：

- 内置 SSH server 默认随 GitCandy ASP.NET Core host 同进程启动和停止，不要求部署者配置外部 OpenSSH。
- `EnableSsh=false` 时不监听 SSH 端口；`SshPort` 支持配置并有端口冲突诊断。
- host key 可持久化、可备份、可迁移；重新生成 host key 的兼容性影响有明确提示。
- UI 继续在启用 SSH 时显示 `git@{host}:git/{repo}.git` 或 `ssh://git@{host}:{port}/git/{repo}.git`。
- SSH 默认只允许 Git 必需命令：`git-upload-pack`、`git-receive-pack`、`git-upload-archive`。
- 默认不提供交互 shell、SFTP、端口转发或 SSH 密码登录。
- public key 认证接入 ASP.NET Core Identity 用户和新 SSH key schema；旧 `SshKeys` 表只作为行为参考，不作为兼容目标。
- 私有仓库匿名/无权限 clone、fetch、push 失败；具备 owner/team/admin 等迁移后约定权限的 key 可以 clone、fetch、push。
- 若迁移后修正“管理员 SSH key 无隐式权限”或“公开仓库 SSH 仍要求注册 key”等旧差异，必须有测试和兼容性说明。
- SSH clone、fetch、push 复用 Git HTTP 同一套仓库路径解析、权限判断、审计、hook 和 `IGitTransportBackend`。
- Git helper 调用使用结构化参数，不把用户输入拼进 shell command 或单个未校验参数字符串。
- 仓库路径必须归一化并验证位于配置的 repository 根目录内。
- pack 请求/响应必须流式转发，不能把 pack 完整读入内存。
- listener 停止、应用关闭、客户端断开和 Git helper 退出都要支持 graceful shutdown 和可诊断日志。

## 兼容性影响

本任务只新增迁移文档并更新路线图状态，不改变：

- 公开路由和 Git URL。
- 数据库 schema、索引、默认值或 seed 行为。
- Identity cookie、Basic Auth、SSH public key 或权限语义。
- 配置键、环境变量或部署方式。
- Git HTTP/SSH 协议行为和响应 header。
- repository、cache、App_Data、host keys、logs 等文件系统布局。
