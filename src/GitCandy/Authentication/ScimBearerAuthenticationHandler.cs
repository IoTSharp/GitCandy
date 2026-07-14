using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GitCandy.Enterprise;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace GitCandy.Authentication;

/// <summary>验证团队企业连接专用的 SCIM bearer。</summary>
public sealed class ScimBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IScimBearerService bearerService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string ConnectionIdClaim = "gitcandy:scim:connection_id";
    private readonly IScimBearerService _bearerService = bearerService;

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

        var connectionId = await _bearerService.ValidateAsync(header.Parameter, Context.RequestAborted);
        if (connectionId is null)
        {
            return AuthenticateResult.Fail("Invalid credentials.");
        }

        var identity = new ClaimsIdentity(
            [new Claim(ConnectionIdClaim, connectionId.Value.ToString(CultureInfo.InvariantCulture))],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer realm=\"GitCandy SCIM\"";
        return Task.CompletedTask;
    }
}
