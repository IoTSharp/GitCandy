using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GitCandy.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace GitCandy.Authentication;

/// <summary>使用 Bearer PAT 建立 API 机器身份。</summary>
public sealed class PersonalAccessTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IPersonalAccessTokenService tokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private readonly IPersonalAccessTokenService _tokenService = tokenService;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return AuthenticateResult.NoResult();
        }

        if (!AuthenticationHeaderValue.TryParse(authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return AuthenticateResult.Fail("Invalid Bearer authentication header.");
        }

        var principal = await _tokenService.AuthenticateAsync(header.Parameter, Context.RequestAborted);
        return principal is null
            ? AuthenticateResult.Fail("Invalid credentials.")
            : AuthenticateResult.Success(new AuthenticationTicket(
                CredentialPrincipalFactory.Create(principal, Scheme.Name),
                Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer realm=\"GitCandy API\"";
        return Task.CompletedTask;
    }
}

internal static class CredentialPrincipalFactory
{
    public static ClaimsPrincipal Create(PersonalAccessTokenPrincipal principal, string authenticationType)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principal.UserId),
            new(ClaimTypes.Name, principal.UserName),
            new(CredentialClaimTypes.CredentialId, principal.CredentialId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(CredentialClaimTypes.CredentialKind, CredentialClaimTypes.PersonalAccessToken)
        };
        claims.AddRange(principal.Scopes.Select(static scope => new Claim(CredentialClaimTypes.Scope, scope)));
        if (principal.IsAdministrator)
        {
            claims.Add(new Claim(ClaimTypes.Role, GitCandy.Configuration.RoleNames.Administrator));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }
}

internal static class CredentialPrincipalExtensions
{
    public static bool HasPersonalAccessTokenScope(this ClaimsPrincipal principal, string scope)
    {
        return !principal.HasClaim(CredentialClaimTypes.CredentialKind, CredentialClaimTypes.PersonalAccessToken)
            || principal.HasClaim(CredentialClaimTypes.Scope, scope);
    }
}
