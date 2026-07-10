# M9 #097-#099 Git backend 与外部 OpenSSH 适配

## 范围

本切片按 `#097 -> #098 -> #099` 完成三个相邻但独立的改进：

- `#097`：活动 ASP.NET Core 10 主线采用 LibGit2Sharp 0.31.0，验证其托管 API 和 native runtime。
- `#098`：非 Git wire protocol 仓库操作进入 `IManagedGitRepositoryService`；官方 Git helper 缩减并锁定为三个 transport 命令。
- `#099`：提供默认关闭的 OpenSSH `AuthorizedKeysCommand` + key-specific forced-command 适配；内置 SSH 仍是默认路线。

没有修改公开 Web/Git URL、Identity cookie、Basic Auth、数据库 schema、仓库目录布局或内置 SSH 默认行为。

## #097 LibGit2Sharp 0.31.0

### 依赖和 native binary

活动 `net10.0` 项目 `GitCandy.Git` 通过 central package management 引用 `LibGit2Sharp 0.31.0`。该包选择 `net8.0` 资产并传递引入 `LibGit2Sharp.NativeBinaries 2.0.323`，不再像旧 MVC5 项目那样手工引用独立 native props。

旧 `GitCandy/GitCandy.csproj` 仍是 .NET Framework 4.5 行为参考，其 `LibGit2Sharp 0.22.0` 和 `NativeBinaries 1.0.129` 不参与 `GitCandy.slnx` restore/build/publish。直接把旧项目升级到 0.31.0 不可行，因为当前包最低 .NET Framework 资产是 `net472`；本切片没有改变冻结的旧项目。

### API 落点和差异

新增 `IManagedGitRepositoryService` / `LibGit2RepositoryService`，覆盖：

- bare repository 初始化；
- legacy `{name}` 和规范 `{name}.git` 仓库发现；
- 仓库格式、根目录、symlink/junction 最终目标边界验证；
- HEAD、最近 commit、local branches 和 tags 读取。

从 0.22 迁移时不复用旧静态 accessor/cache。0.31 的 tag object 读取使用 `Tag.Target.Id`；不能对抽象 `GitObject` 调用泛型 `Peel<T>`。新服务以短生命周期 `Repository` 实例工作，不跨线程缓存 native handle。

## #098 Git helper 最小边界

### 已改为 LibGit2Sharp/托管实现

| 操作 | 当前实现 |
| --- | --- |
| 仓库路径发现和格式验证 | `LibGit2RepositoryService.ResolveExistingPath` |
| bare repository 初始化 | `LibGit2RepositoryService.InitializeBare` |
| HEAD/commit/branch/tag 摘要 | `LibGit2RepositoryService.ReadSnapshot` |
| transport 前的仓库存在性验证 | `GitProcessTransportBackend` 委托给托管仓库服务 |

### 保留官方 helper

| helper | 保留原因 |
| --- | --- |
| `upload-pack` | clone/fetch negotiation、sideband、shallow/partial clone、protocol v2 和 pack streaming |
| `receive-pack` | push negotiation、atomic update、服务端 hook 和 pack streaming |
| `upload-archive` | SSH archive wire protocol 兼容 |

这三个命令仍只由 `IGitTransportBackend` 的 `GitProcessTransportBackend` 使用 `ProcessStartInfo.ArgumentList` 启动。源码门禁禁止其他活动模块新增进程调用；readiness health check 仅执行 `git --version`，用于提前发现协议 helper 缺失。

本切片不尝试纯托管重写 pack/wire protocol，也不把 LibGit2Sharp 当作 Git LFS server。以后若减少上述任一 helper，必须另做任务，并先覆盖 protocol v2、LFS、大 pack、shallow/partial clone、atomic push、hooks、并发和压力测试。

## #099 OpenSSH forced-command 适配

### 决策和数据流

外部 OpenSSH 适合已有企业 sshd、集中端口/审计或主机级 SSH 策略的部署，但代价是每次连接启动一个短生命周期 GitCandy adapter 进程。默认部署仍使用 ASP.NET Core host 内的 `SshServerHostedService`。

```text
sshd public-key authentication
  -> AuthorizedKeysCommand: GitCandy --openssh-authorized-key %f
  -> GitCandy returns: restrict,command="... --openssh-forced-command SHA256:..." public-key
  -> sshd verifies the key and supplies SSH_ORIGINAL_COMMAND
  -> GitCandy resolves the fingerprint to an Identity user
  -> strict Git command parser
  -> repository read/write authorization
  -> shared path resolver and IGitTransportBackend
```

adapter 只接受：

- `git-upload-pack '/git/{repository}.git'`
- `git-receive-pack '/git/{repository}.git'`
- `git-upload-archive '/git/{repository}.git'`
- `GIT_PROTOCOL=version=2` 或未设置

输出的 authorized key 使用 OpenSSH `restrict` 和 key-specific `command=`。交互 shell、PTY、SFTP、agent/X11/端口转发和任意命令均不可用。fingerprint 只用于查找数据库中唯一的 SHA-256 key；sshd 仍负责 public-key 签名验证。

### GitCandy 配置

编辑安装目录中的 `appsettings.Production.json`，使用 self-contained Linux 包中的可执行文件，并确保路径为绝对路径：

```json
{
  "GitCandy": {
    "OpenSsh": {
      "Enabled": true,
      "ExecutablePath": "/opt/gitcandy/GitCandy"
    }
  }
}
```

默认值为 `Enabled=false`。adapter CLI 从可执行文件目录加载 `appsettings.json` 和 `appsettings.Production.json`；未显式设置环境时默认为 Production。它不会启动 Kestrel、内置 SSH、Quartz 或自动执行 migration。部署升级必须先通过正常 GitCandy host 启动或发布流程应用 migration。

### sshd_config 示例

为专用 `git` OS 账号配置。该账号需要一个可供 sshd 执行 forced command 的 shell，但下面的 `Match`、key-specific `restrict` 和 `command=` 会禁止交互登录：

```text
PubkeyAuthentication yes

Match User git
    PasswordAuthentication no
    KbdInteractiveAuthentication no
    PermitTTY no
    DisableForwarding yes
    AuthorizedKeysFile none
    AuthorizedKeysCommand /opt/gitcandy/GitCandy --openssh-authorized-key %f
    AuthorizedKeysCommandUser git
```

然后运行 `sshd -t` 验证配置并 reload sshd。`AuthorizedKeysCommandUser` 必须能读取 GitCandy 配置和数据库。示例让同一个 `git` 账号执行查询和 forced command；部署时应把它加入 GitCandy 数据访问组或配置等价 ACL，使其能够读取 `/opt/gitcandy/appsettings.Production.json`，读写 SQLite 数据库及其 WAL/SHM 文件，按 push 权限读写仓库，并执行 Git helper。不要让该账号获得仓库根目录和必要数据库目录之外的写权限。

不应额外设置全局 `ForceCommand`：GitCandy 返回的 key-specific `command=` 已绑定数据库 fingerprint。若企业 sshd 版本不支持 `%f`、`restrict` 或 `DisableForwarding`，应升级 OpenSSH；不要通过放宽 shell/forwarding 限制来兼容旧版本。

### 运行验证

启用后至少执行：

```bash
git -c protocol.version=2 clone ssh://git@host/git/repository.git
git -C repository fetch origin
git -C repository push origin HEAD:refs/heads/openssh-smoke
ssh git@host whoami
```

前三项应成功（push 需要写权限），最后一项必须失败。还应验证未知 key、无权限仓库、不存在仓库、非法 `GIT_PROTOCOL` 和端口转发失败。

### 迁移和回滚

1. 保持内置 SSH 可用，在不同端口先完成 OpenSSH smoke test。
2. 企业入口切到 sshd 后，可按需将 `GitCandy:Application:EnableSsh` 设为 `false`；这不是 adapter 的强制要求。
3. 回滚时删除 `Match User git` 适配配置或恢复原 sshd 配置，将 `GitCandy:OpenSsh:Enabled` 设为 `false`，重新启用内置 SSH。

回滚不修改 Identity、SSH key、仓库或数据库 schema；同一批已登记 public key 可继续用于内置 SSH。

## 验证记录

- `dotnet restore GitCandy.slnx`
- `dotnet build GitCandy.slnx --no-restore`：0 warnings / 0 errors
- `dotnet test GitCandy.slnx --no-restore --no-build`：GitCandy.Data.Tests 41/41，GitCandy.Tests 81/81
- LibGit2Sharp managed repository/native runtime tests
- OpenSSH adapter authorized-key、命令拒绝、read/write 权限、protocol v2 和 shared backend tests
- Linux x64 self-contained publish 包含 `libgit2-3f4182d.so`；Windows x64 包含 `git2-3f4182d.dll`
- adapter CLI 禁用态以 exit code 1 和空 stdout/stderr fail closed
- 现有真实 Git Smart HTTP 和内置 SSH clone/fetch/push 回归由完整 test suite 执行

当前验证主机安装了 OpenSSH client 但没有 `sshd`，因此未在本机修改系统 sshd 配置或执行外部 daemon 端到端测试。部署者仍须在目标企业主机按上述步骤运行 `sshd -t` 和 clone/fetch/push/shell-rejection smoke test。
