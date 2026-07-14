using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace GitCandy.Web.Enterprise;

public sealed record EnterpriseLoginState(
    long ConnectionId,
    string ReturnUrl,
    string RedirectUri,
    string Nonce,
    string CodeVerifier,
    string Correlation,
    DateTimeOffset CreatedAt);

public sealed record EnterpriseLoginStateChallenge(
    string ProtectedState,
    string Nonce,
    string CodeVerifier,
    string CodeChallenge);

public interface IEnterpriseLoginStateService
{
    EnterpriseLoginStateChallenge Create(
        HttpResponse response,
        long connectionId,
        string returnUrl,
        string redirectUri);

    EnterpriseLoginState? Consume(HttpRequest request, HttpResponse response, string protectedState);
}

/// <summary>使用 Data Protection、PKCE 和浏览器 correlation cookie 保护企业登录状态。</summary>
public sealed class EnterpriseLoginStateService(
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : IEnterpriseLoginStateService
{
    private const string CookieName = ".GitCandy.EnterpriseCorrelation";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "GitCandy.EnterpriseLogin.State.v1");
    private readonly TimeProvider _timeProvider = timeProvider;

    public EnterpriseLoginStateChallenge Create(
        HttpResponse response,
        long connectionId,
        string returnUrl,
        string redirectUri)
    {
        var nonce = CreateRandomValue();
        var verifier = CreateRandomValue();
        var correlation = CreateRandomValue();
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        var state = new EnterpriseLoginState(
            connectionId,
            returnUrl,
            redirectUri,
            nonce,
            verifier,
            correlation,
            _timeProvider.GetUtcNow());
        response.Cookies.Append(CookieName, correlation, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = Lifetime,
            Path = "/EnterpriseLogin/Callback"
        });
        return new EnterpriseLoginStateChallenge(
            _protector.Protect(JsonSerializer.Serialize(state)),
            nonce,
            verifier,
            challenge);
    }

    public EnterpriseLoginState? Consume(
        HttpRequest request,
        HttpResponse response,
        string protectedState)
    {
        response.Cookies.Delete(CookieName, new CookieOptions
        {
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/EnterpriseLogin/Callback"
        });
        if (string.IsNullOrWhiteSpace(protectedState)
            || !request.Cookies.TryGetValue(CookieName, out var correlation))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<EnterpriseLoginState>(
                _protector.Unprotect(protectedState));
            if (state is null
                || state.CreatedAt > _timeProvider.GetUtcNow()
                || _timeProvider.GetUtcNow() - state.CreatedAt > Lifetime
                || !FixedTimeEquals(correlation, state.Correlation))
            {
                return null;
            }

            return state;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CreateRandomValue() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
