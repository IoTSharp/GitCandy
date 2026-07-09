using GitCandy.Data.Identity;

namespace GitCandy.Data.Domain;

/// <summary>
/// GitCandy 用户 SSH 公钥领域实体。
/// </summary>
public sealed class GitCandySshKey
{
    /// <summary>
    /// SSH 公钥主键。
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Identity 用户主键。
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// SSH 公钥算法类型，例如 ssh-rsa 或 ssh-dss。
    /// </summary>
    public string KeyType { get; set; } = string.Empty;

    /// <summary>
    /// SSH 公钥指纹。
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>
    /// SSH 公钥主体，不包含算法类型和注释。
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// 公钥导入时间。
    /// </summary>
    public DateTime ImportedAtUtc { get; set; }

    /// <summary>
    /// 公钥最近使用时间；从未使用时为空。
    /// </summary>
    public DateTime? LastUsedAtUtc { get; set; }

    /// <summary>
    /// Identity 用户。
    /// </summary>
    public GitCandyUser User { get; set; } = null!;
}
