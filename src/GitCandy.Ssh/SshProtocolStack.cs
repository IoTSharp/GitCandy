using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Messages;

namespace GitCandy.Ssh;

/// <summary>
/// 创建 GitCandy 内置 SSH server 使用的现代、安全收敛协议配置。
/// </summary>
public static class SshProtocolStack
{
    /// <summary>
    /// 创建只允许 public key 认证、禁用 CBC 且不注册端口转发服务的 SSH 配置。
    /// </summary>
    /// <returns>可供 SSH server session 使用的独立配置。</returns>
    public static SshSessionConfiguration CreateServerConfiguration()
    {
        var configuration = new SshSessionConfiguration();
        configuration.AuthenticationMethods.Clear();
        configuration.AuthenticationMethods.Add(AuthenticationMethods.PublicKey);
        configuration.EncryptionAlgorithms.Remove(SshAlgorithms.Encryption.Aes256Cbc);
        return configuration;
    }
}
