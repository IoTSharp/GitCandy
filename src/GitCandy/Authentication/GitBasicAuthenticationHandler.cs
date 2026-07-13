using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using GitCandy.Application;
using GitCandy.Data.Identity;
using GitCandy.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace GitCandy.Authentication;

/// <summary>
/// 使用 ASP.NET Core Identity 校验 Git Smart HTTP Basic 凭据的认证处理器。
/// </summary>
public sealed class GitBasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IMembershipService membershipService,
    SignInManager<GitCandyUser> signInManager,
    IUserClaimsPrincipalFactory<GitCandyUser> claimsPrincipalFactory,
    IOptions<IdentityOptions> identityOptions,
    IPersonalAccessTokenService personalAccessTokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string ChallengeValue = "Basic realm=\"GitCandy\", charset=\"UTF-8\"";
    private readonly IMembershipService _membershipService = membershipService;
    private readonly SignInManager<GitCandyUser> _signInManager = signInManager;
    private readonly IUserClaimsPrincipalFactory<GitCandyUser> _claimsPrincipalFactory = claimsPrincipalFactory;
    private readonly IdentityOptions _identityOptions = identityOptions.Value;
    private readonly IPersonalAccessTokenService _personalAccessTokenService = personalAccessTokenService;

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return AuthenticateResult.NoResult();
        }

        if (!AuthenticationHeaderValue.TryParse(authorization, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return AuthenticateResult.Fail("Invalid Basic authentication header.");
        }

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("Invalid Basic authentication header.");
        }

        var separatorIndex = credentials.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return AuthenticateResult.Fail("Invalid Basic authentication header.");
        }

        var userNameOrEmail = credentials[..separatorIndex];
        var password = credentials[(separatorIndex + 1)..];

        if (password.StartsWith("gcpat_", StringComparison.Ordinal))
        {
            var tokenPrincipal = await _personalAccessTokenService.AuthenticateAsync(
                password,
                Context.RequestAborted);
            if (tokenPrincipal is null
                || !string.Equals(tokenPrincipal.UserName, userNameOrEmail, StringComparison.OrdinalIgnoreCase)
                || (!tokenPrincipal.Scopes.Contains(PersonalAccessTokenScopes.GitRead)
                    && !tokenPrincipal.Scopes.Contains(PersonalAccessTokenScopes.GitWrite)))
            {
                return AuthenticateResult.Fail("Invalid credentials.");
            }

            return AuthenticateResult.Success(new AuthenticationTicket(
                CredentialPrincipalFactory.Create(tokenPrincipal, Scheme.Name),
                Scheme.Name));
        }

        var user = await _membershipService.FindUserAsync(userNameOrEmail, Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail("Invalid credentials.");
        }

        var passwordResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: true);
        if (!passwordResult.Succeeded)
        {
            return AuthenticateResult.Fail("Invalid credentials.");
        }

        var identityPrincipal = await _claimsPrincipalFactory.CreateAsync(user);
        var identity = new ClaimsIdentity(
            identityPrincipal.Claims,
            Scheme.Name,
            _identityOptions.ClaimsIdentity.UserNameClaimType,
            _identityOptions.ClaimsIdentity.RoleClaimType);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = ChallengeValue;
        return Task.CompletedTask;
    }
}
