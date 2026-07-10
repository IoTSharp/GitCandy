# M7 #070-#079 SSH 和后台任务现代化

记录日期：2026-07-10

## 验收结论

- ASP.NET Core host 使用 `SshServerHostedService` 启停真实内置 SSH listener，不需要外部 OpenSSH daemon、forced command 或独立 worker。
- 旧 SSH wire protocol 代码隔离到 `src/GitCandy.Ssh` 的 `net10.0` 类库；M7 只做 .NET 10 兼容、生命周期接入和安全收缩，不在迁移主线替换协议栈。
- 每个 SSH session 建立独立 DI scope。public key 认证、Identity 用户、管理员角色和仓库权限由 `ISshAccessService` 解析。
- SSH 后台数据库访问全部通过 `IDbContextFactory<GitCandyDbContext>` 创建短生命周期 context，不跨线程持有 scoped `GitCandyDbContext`。
- `git-upload-pack`、`git-receive-pack`、`git-upload-archive` 统一进入 M6 的 `IGitTransportBackend`，复用仓库路径边界、helper 并发限制、结构化参数、流式转发、审计日志和 Git receive-pack 原生 hook pipeline。
- Git protocol v2 只接受 SSH channel 环境变量 `GIT_PROTOCOL=version=2`；其他环境变量被拒绝。
- Quartz scheduler 在 host shutdown 时 interrupt 正在执行的 job，把取消传给 `IJobExecutionContext.CancellationToken`，并等待 job 清理完成。
- 真实 Git 2.55 + OpenSSH 9.5 clone、fetch、push 已通过；listener 释放、端口占用启动失败、host key 导入和 scheduler 取消均有测试。

## 对应 ROADMAP

| 编号 | 实现与证据 |
| --- | --- |
| #070 | `BuiltInSshServerRuntime` 替换 `PlaceholderSshServerRuntime`，由现有 hosted service 随 host 启停 |
| #071 | `GitSshSession`、`ISshAccessService`、`ISshHostKeyProvider` 全部由 built-in DI 创建 |
| #072 | 三个 SSH Git 命令构造 `GitTransportRequest` 并调用共享 `IGitTransportBackend` |
| #073 | `SshAccessService` 使用 pooled `IDbContextFactory<GitCandyDbContext>` 完成认证和授权查询 |
| #074 | 新配置 `SshHostKeyPath`；旧 `UserConfigurationPath` 中的 RSA `HostKeys` 可一次性导入 |
| #075 | 禁用 password/shell/SFTP/forwarding；收缩算法并记录剩余 SHA-1 风险 |
| #076 | Quartz `InterruptJobsOnShutdown`、`InterruptJobsOnShutdownWithWait` 和 cancellation 测试 |
| #077 | hosted service 记录端口、host key、启动/停止和任务异常诊断，不记录私钥或凭据 |
| #078 | `SshGitIntegrationTests` 使用真实 Git/OpenSSH 验证 clone、fetch、push |
| #079 | 测试验证 listener 停止后端口可重新绑定，端口已占用时 host 启动失败 |

## SSH 配置迁移

新配置位于 `GitCandy:Application`：

```json
{
  "GitCandy": {
    "Application": {
      "EnableSsh": true,
      "SshPort": 22,
      "SshHostKeyPath": "App_Data/ssh-host-key.xml"
    }
  }
}
```

环境变量分别为：

```text
GitCandy__Application__EnableSsh
GitCandy__Application__SshPort
GitCandy__Application__SshHostKeyPath
```

迁移规则：

| 旧配置 | 新配置/行为 |
| --- | --- |
| `UserConfiguration/EnableSsh` | 复制到 `GitCandy:Application:EnableSsh` |
| `UserConfiguration/SshPort` | 复制到 `GitCandy:Application:SshPort` |
| `UserConfiguration/HostKeys` | 若新 host key 文件不存在，从 `UserConfigurationPath` 自动导入第一个可用 RSA key |
| 没有旧 RSA host key | 首次启用 SSH 时生成 3072-bit RSA key 并写入 `SshHostKeyPath` |

host key 文件包含私钥，必须纳入密钥备份而不是源代码。Unix 首次创建权限为 owner read/write；Windows 使用目标目录继承 ACL。不要记录或提交该文件。删除文件会在下次启动生成新 host key，并导致客户端 host-key 变更告警。

端口被占用、host key 文件损坏、没有可用 RSA key 或无法写入 host key 路径时，SSH hosted service 记录端口和排查方向并让 host 启动失败，不静默降级成没有 SSH 的 Web host。

## 认证、授权和协议边界

- 只接受数据库 `SshKeys` 中完整 blob 与 SHA-256 fingerprint 均匹配的 `ssh-rsa` public key。
- 认证成功后更新 `LastUsedAtUtc`，不记录 public key、fingerprint、authorization header 或其他凭据。
- read 命令为 `git-upload-pack` 和 `git-upload-archive`；write 命令为 `git-receive-pack`。
- 仓库 metadata 权限使用与 Git HTTP 相同的公开/私有、owner、team 和 administrator 语义。
- 物理路径继续通过 `IGitRepositoryPathResolver` 和 `IGitTransportBackend.EnsureRepositoryExists` 做根目录与 symlink/junction 边界验证。
- command parser 只接受 `'/git/{repository}.git'` 单段仓库名，不经过 shell，不接受额外参数。
- Git helper 仍由 `GitProcessTransportBackend` 通过 `ProcessStartInfo.ArgumentList` 启动；stdin 通过有界 pipe 输入，stdout 直接流式写入 SSH channel。
- `git-receive-pack` 继续执行仓库内标准 pre-receive、update、post-receive hooks；应用级 hook 扩展可在后续独立任务建立，但 SSH 不另写协议后端。

## 安全评估和算法兼容

M7 保留的自写协议栈没有经过现代 SSH 实现同等级别的安全审计，仍不应视为长期协议基础。新 host 已移除更弱的 group1、DSA、CBC、3DES 和 HMAC-MD5，只保留验收所需最小集合：

```text
KEX:       diffie-hellman-group14-sha1
Host/user: ssh-rsa
Cipher:    aes128-ctr, aes192-ctr, aes256-ctr
MAC:       hmac-sha1
Compress:  none
```

SHA-1 KEX、RSA/SHA-1 signature 和 HMAC-SHA1 已被现代 OpenSSH 默认策略禁用。因此当前 OpenSSH 客户端需要显式兼容配置；测试使用：

```text
-o KexAlgorithms=diffie-hellman-group14-sha1
-o HostKeyAlgorithms=ssh-rsa
-o PubkeyAcceptedAlgorithms=ssh-rsa
-o Ciphers=aes128-ctr
-o MACs=hmac-sha1
```

这些参数是迁移兼容措施，不是推荐的长期安全配置。现代 KEX、RSA-SHA2/Ed25519 host/user key、AEAD cipher、限流强化和协议栈替换继续属于 M9 #092 的独立升级，必须重新验证 clone/fetch/push 和 host key 迁移。

服务器默认拒绝：

- SSH password、hostbased 和 none authentication。
- 交互 shell、PTY、SFTP subsystem。
- local/remote/dynamic port forwarding。
- 非 Git exec command。
- 除 `GIT_PROTOCOL=version=2` 外的 SSH environment request。

## Scheduler 关闭行为

- Quartz 使用 in-memory store，不新增数据库表。
- `ISchedulerJob.ExecuteAsync` 接收 Quartz execution cancellation token。
- host shutdown 同时设置 `WaitForJobsToComplete=true` 和 interrupt-with-wait；job 应尽快观察 token 并清理资源。
- `QuartzSchedulerJob` 分别记录 executing、executed、canceled 和 failed，不吞掉未记录异常。
- 每次执行创建独立 DI scope；后续访问数据库的 job 必须继续使用 `IDbContextFactory` 或执行 scope 内的 context，不得跨执行缓存 context。

## 验证记录

已运行：

```powershell
dotnet test .\GitCandy.slnx -p:UseArtifactsOutput=true -p:ArtifactsPath=D:\source\GitCandy\artifacts\m7-full-test
```

结果：

- `GitCandy.Data.Tests`：41/41 通过。
- `GitCandy.Tests`：61/61 通过。
- 总计：102/102 通过，build 过程 0 warning / 0 error。
- SQLite Identity/SSH key 创建、读取、`LastUsedAtUtc` 写入和私有仓库权限查询通过。
- MVC/Git HTTP 既有测试继续通过。
- 真实 Git/OpenSSH SSH clone、protocol v2 fetch、authorized push 通过。
- 正在执行的 Quartz job 在 host stop 时收到 cancellation，host 在超时内完成停止。
- listener stop 后原端口可重新绑定；端口已占用时 host startup 明确失败。

## 兼容性、迁移和回滚

- 公开 SSH URL 保持 `ssh://user@host:port/git/{repository}.git`；SSH username 不参与授权，身份由 public key 决定。
- 数据库 schema 和 migration 未改变；使用现有 Identity `SshKeys` 表。
- 新增 `SshHostKeyPath` 配置，默认 `App_Data/ssh-host-key.xml`。部署升级前应备份旧 RSA host key，并确保应用账户可读写目标目录。
- 新 host 不再接受 DSA 或其他非 RSA 用户 key；现有用户需要导入 RSA key。
- 回滚可先设置 `EnableSsh=false` 保留 Web/Git HTTP 服务，再恢复 M2 placeholder 或旧 MVC5 host。host key 文件和数据库无需回滚，可保留供再次启用；若回滚到旧 host，可把 RSA `KeyXml` 重新放回旧 `HostKeys` 配置。
