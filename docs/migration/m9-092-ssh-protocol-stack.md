# M9 #092 SSH 协议栈替换

记录日期：2026-07-11

## 验收结论

- 内置 SSH server 已从迁移期自写协议代码替换为 `Microsoft.DevTunnels.Ssh.Tcp` 3.12.36；旧 `src/GitCandy.Ssh/Protocol` 源码仅作为迁移参考保留，不再参与编译或发布。
- ASP.NET Core `SshServerHostedService`、单进程部署、DI scope、Identity public key 认证、仓库权限和共享 `IGitTransportBackend` 边界保持不变。
- 现代 OpenSSH 不再需要 `diffie-hellman-group14-sha1`、`ssh-rsa`、`hmac-sha1` 或特定 AES-CTR 兼容参数即可完成 Git protocol v2 clone、fetch、push。
- server 只允许 public key 认证和一个 `session` channel；只接受 `GIT_PROTOCOL=version=2` 与 `git-upload-pack`、`git-receive-pack`、`git-upload-archive`。
- password、none、keyboard-interactive、hostbased、shell、PTY、SFTP、端口转发、任意 exec 和任意环境变量继续被拒绝。

## 依赖决策

选择 `Microsoft.DevTunnels.Ssh.Tcp` 3.12.36：

- Microsoft 维护、MIT license，同时实现 SSH2 client/server 并持续用 OpenSSH 做互操作验证。
- TCP 包只传递依赖同版本 `Microsoft.DevTunnels.Ssh` core；core 没有其他 NuGet 运行时依赖。
- 包目标 `netstandard2.1`，可由 GitCandy 的 `net10.0` host 使用。
- 默认提供 ECDH/DH SHA-2 KEX、RSA-SHA2/ECDSA、AES-GCM/AES-CTR 和 SHA-2 ETM MAC；GitCandy 额外移除 CBC。
- 相比继续维护自写 FxSsh 风格协议代码，该依赖显著减少密码学、wire protocol 和 OpenSSH 兼容维护面。

没有引入 `Microsoft.DevTunnels.Ssh.Keys`。现有 RSA XML host key 由 .NET `RSA` 导入后交给协议栈，因此无需改变密钥文件格式或增加第二个 key-management 依赖。

当前限制：库不支持 Ed25519，GitCandy 的 SSH key 导入仍保持本切片之前的 RSA-only 行为。扩展用户/host key 类型应作为独立兼容性任务处理，不混入协议替换。

## 安全和协议边界

当前启用算法集合：

```text
KEX:       ecdh-sha2-nistp384, ecdh-sha2-nistp256,
           diffie-hellman-group16-sha512, diffie-hellman-group14-sha256
Host/user: rsa-sha2-512, rsa-sha2-256, ecdsa-sha2-nistp384,
           ecdsa-sha2-nistp256
Cipher:    aes256-gcm@openssh.com, aes256-ctr
MAC:       hmac-sha2-512-etm@openssh.com, hmac-sha2-256-etm@openssh.com,
           hmac-sha2-512, hmac-sha2-256
Compress:  none
```

- 配置不包含 SHA-1 KEX/signature/MAC、CBC、`none` encryption 或 password authentication。
- 未注册 Dev Tunnels SSH TCP port-forwarding service；非 `session` channel 在打开阶段明确拒绝。
- channel 输入使用 SSH window flow control，Git helper stdin/stdout 继续流式传输，不完整缓冲 pack。
- RSA public key blob 与 SHA-256 fingerprint 继续和 `SshKeys` 表做固定时间匹配；不记录 key、fingerprint 或私钥。
- 只有携带有效签名的 public-key authentication 才更新 `LastUsedAtUtc`；签名less key query 只检查 key 是否可用，不写审计时间。
- command parser、仓库路径归一化、symlink/junction 边界和 `ProcessStartInfo.ArgumentList` 规则没有放宽。

## 兼容性和迁移

- 公开 URL 保持 `ssh://user@host:port/git/{repository}.git`，配置键、端口、数据库 schema 和文件系统布局不变。
- `SshHostKeyPath` 继续读取现有 `ssh-rsa` XML 私钥；RSA key material 不变，因此客户端已有 known-host key 可继续使用。
- SSH identification banner 从 `FxSsh` 改为 `Microsoft.DevTunnels.Ssh`，属于可观察但不影响 Git URL 的协议实现变化。
- 只支持 SHA-1 的旧 SSH 客户端不再能连接；迁移方案是升级客户端。现代 OpenSSH 应删除 M7 临时兼容参数。
- 回滚时可恢复上一版 runtime 和 `GitCandy.Ssh.csproj` 编译项；数据库、host key 文件和仓库数据无需降级。也可先设置 `GitCandy:Application:EnableSsh=false`，保留 Web 和 Git HTTP 服务。

## 验证入口

```powershell
dotnet restore GitCandy.slnx -p:UseArtifactsOutput=true -p:ArtifactsPath=D:\source\GitCandy\artifacts\m9-092
dotnet build GitCandy.slnx --no-restore -p:UseArtifactsOutput=true -p:ArtifactsPath=D:\source\GitCandy\artifacts\m9-092
dotnet test GitCandy.slnx --no-restore -p:UseArtifactsOutput=true -p:ArtifactsPath=D:\source\GitCandy\artifacts\m9-092
```

验证覆盖：

- 配置只允许 public key，算法集合不包含 SHA-1/CBC 并包含 RSA-SHA2/AES-GCM。
- legacy RSA host key 导入、listener 启停、IPv4/IPv6 dual mode 和端口冲突失败。
- Identity SSH key 认证、`LastUsedAtUtc` 写入和私有仓库 read/write 权限。
- 真实 Git/OpenSSH 在没有旧算法选项时执行 protocol v2 clone、fetch、push。
- 真实 OpenSSH 任意 `whoami` exec 请求被拒绝。

实际结果：

- `.NET SDK 10.0.301` build：0 warning / 0 error。
- `GitCandy.Data.Tests`：41/41 通过；`GitCandy.Tests`：63/63 通过；总计 104/104 通过。
- Git `2.55.0.windows.2` + OpenSSH for Windows `9.5p2`：未设置旧算法兼容参数，protocol v2 clone、fetch、push 通过。
- `dotnet format --verify-no-changes` 对本切片 C# 文件通过。
- `dotnet list GitCandy.slnx package --vulnerable --include-transitive`：所有项目均未发现已知易受攻击包。
- Release `dotnet publish` 通过，产物包含 `Microsoft.DevTunnels.Ssh.dll` 和 `Microsoft.DevTunnels.Ssh.Tcp.dll`。
