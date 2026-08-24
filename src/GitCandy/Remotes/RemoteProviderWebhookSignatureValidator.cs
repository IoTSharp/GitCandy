using System.Security.Cryptography;
using System.Text;
using GitCandy.Remotes;

namespace GitCandy.Web.Remotes;

/// <summary>校验 GitHub HMAC 及 GitLab/Gitee provider token，且始终使用固定时间比较。</summary>
public sealed class RemoteProviderWebhookSignatureValidator
{
    public bool IsValid(
        RemoteProviderKind provider,
        RemoteSecret secret,
        IHeaderDictionary headers,
        ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(headers);
        return provider switch
        {
            RemoteProviderKind.GitHub => ValidateGitHub(secret, headers, payload),
            RemoteProviderKind.GitLab => ValidateToken(secret, headers["X-Gitlab-Token"].ToString() ?? string.Empty),
            RemoteProviderKind.Gitee => ValidateToken(secret, headers["X-Gitee-Token"].ToString() ?? string.Empty),
            _ => false
        };
    }

    private static bool ValidateGitHub(
        RemoteSecret secret,
        IHeaderDictionary headers,
        ReadOnlySpan<byte> payload)
    {
        var provided = headers["X-Hub-Signature-256"].ToString();
        const string prefix = "sha256=";
        if (!provided.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || provided.Length != prefix.Length + 64)
        {
            return false;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromHexString(provided[prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (decoded.Length != 32)
        {
            return false;
        }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret.Value), payload);
        return CryptographicOperations.FixedTimeEquals(expected, decoded);
    }

    private static bool ValidateToken(RemoteSecret secret, string provided)
    {
        if (provided.Length is 0 or > 16 * 1024)
        {
            return false;
        }
        var expectedBytes = Encoding.UTF8.GetBytes(secret.Value);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
