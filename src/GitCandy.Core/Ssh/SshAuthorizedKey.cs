namespace GitCandy.Ssh;

/// <summary>
/// 已登记 SSH public key 及其 Identity 用户。
/// </summary>
/// <param name="Principal">key 对应的 Identity 用户。</param>
/// <param name="KeyType">OpenSSH key 类型。</param>
/// <param name="PublicKey">不含 key 类型和注释的 Base64 public key。</param>
/// <param name="Fingerprint">不含 <c>SHA256:</c> 前缀和 padding 的 SHA-256 fingerprint。</param>
public sealed record SshAuthorizedKey(
    SshPrincipal Principal,
    string KeyType,
    string PublicKey,
    string Fingerprint);
