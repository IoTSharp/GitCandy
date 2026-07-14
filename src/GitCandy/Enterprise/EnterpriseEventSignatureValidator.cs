using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Enterprise;

namespace GitCandy.Web.Enterprise;

/// <summary>飞书与钉钉 provider event 的签名和重放时间窗校验。</summary>
public sealed class EnterpriseEventSignatureValidator(TimeProvider timeProvider)
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _timeProvider = timeProvider;

    public bool IsValid(
        EnterpriseProviderKind provider,
        EnterpriseSecret secret,
        IHeaderDictionary headers,
        ReadOnlySpan<byte> body)
    {
        return provider switch
        {
            EnterpriseProviderKind.Feishu => ValidateFeishu(secret, headers, body),
            EnterpriseProviderKind.DingTalk => ValidateDingTalk(secret, headers, body),
            _ => false
        };
    }

    private bool ValidateFeishu(
        EnterpriseSecret secret,
        IHeaderDictionary headers,
        ReadOnlySpan<byte> body)
    {
        var timestamp = headers["X-Lark-Request-Timestamp"].ToString();
        var nonce = headers["X-Lark-Request-Nonce"].ToString();
        var supplied = headers["X-Lark-Signature"].ToString();
        if (!IsFresh(timestamp) || string.IsNullOrWhiteSpace(nonce) || supplied.Length != 64)
        {
            return false;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(timestamp));
        hash.AppendData(Encoding.UTF8.GetBytes(nonce));
        hash.AppendData(Encoding.UTF8.GetBytes(secret.Value));
        hash.AppendData(body);
        return FixedTimeEqualsHex(Convert.ToHexString(hash.GetHashAndReset()), supplied);
    }

    private bool ValidateDingTalk(
        EnterpriseSecret secret,
        IHeaderDictionary headers,
        ReadOnlySpan<byte> body)
    {
        var timestamp = headers["x-acs-dingtalk-timestamp"].ToString();
        var supplied = headers["x-acs-dingtalk-signature"].ToString();
        if (!IsFresh(timestamp) || string.IsNullOrWhiteSpace(supplied))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret.Value));
        var prefix = Encoding.UTF8.GetBytes(timestamp + "\n");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        var expected = Convert.ToBase64String(hmac.ComputeHash(signed));
        return FixedTimeEquals(expected, supplied);
    }

    private bool IsFresh(string timestamp)
    {
        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        DateTimeOffset sentAt;
        try
        {
            sentAt = timestamp.Length >= 13
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return (_timeProvider.GetUtcNow() - sentAt).Duration() <= AllowedClockSkew;
    }

    private static bool FixedTimeEqualsHex(string expected, string supplied) =>
        supplied.All(Uri.IsHexDigit)
        && FixedTimeEquals(expected.ToUpperInvariant(), supplied.ToUpperInvariant());

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var suppliedBytes = Encoding.ASCII.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
