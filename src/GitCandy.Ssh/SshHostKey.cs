namespace GitCandy.Ssh;

/// <summary>
/// 内置 SSH server 使用的 host key。
/// </summary>
/// <param name="KeyType">SSH host key 算法。</param>
/// <param name="PrivateKeyXml">旧协议栈兼容的私钥 XML。</param>
public sealed record SshHostKey(string KeyType, string PrivateKeyXml);
