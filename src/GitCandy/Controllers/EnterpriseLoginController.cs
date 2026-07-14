using GitCandy.Configuration;
using GitCandy.Data.Identity;
using GitCandy.Enterprise;
using GitCandy.Web.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GitCandy.Controllers;

[Route("EnterpriseLogin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class EnterpriseLoginController(
    IEnterpriseConnectionService connectionService,
    IEnterpriseSecretResolver secretResolver,
    IEnumerable<IEnterpriseProvider> providers,
    IEnterpriseLoginStateService stateService,
    IEnterpriseSignInService enterpriseSignInService,
    UserManager<GitCandyUser> userManager,
    SignInManager<GitCandyUser> signInManager) : CandyControllerBase
{
    private readonly IEnterpriseConnectionService _connectionService = connectionService;
    private readonly IEnterpriseSecretResolver _secretResolver = secretResolver;
    private readonly IReadOnlyDictionary<EnterpriseProviderKind, IEnterpriseLoginProvider> _providers = providers
        .OfType<IEnterpriseLoginProvider>()
        .ToDictionary(provider => provider.Kind);
    private readonly IEnterpriseLoginStateService _stateService = stateService;
    private readonly IEnterpriseSignInService _enterpriseSignInService = enterpriseSignInService;
    private readonly UserManager<GitCandyUser> _userManager = userManager;
    private readonly SignInManager<GitCandyUser> _signInManager = signInManager;

    [HttpPost("Begin")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(ApiRateLimitPolicies.Write)]
    public async Task<IActionResult> Begin(
        long connectionId,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionService.GetRuntimeContextAsync(connectionId, cancellationToken);
        if (connection is null
            || !connection.IsEnabled
            || !connection.LoginEnabled
            || !_providers.TryGetValue(connection.Provider, out var provider)
            || await _secretResolver.ResolveAsync(connection.SecretReference, cancellationToken) is null)
        {
            return NotFound();
        }

        var callbackUri = Url.ActionLink(nameof(Callback), values: null, protocol: Request.Scheme);
        if (!Uri.TryCreate(callbackUri, UriKind.Absolute, out var redirectUri))
        {
            return BadRequest();
        }

        var safeReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/";
        var state = _stateService.Create(
            Response,
            connection.Id,
            safeReturnUrl,
            redirectUri.AbsoluteUri);
        var authorizationUri = await provider.CreateAuthorizationUriAsync(
            connection,
            new EnterpriseLoginChallenge(
                redirectUri,
                state.ProtectedState,
                state.Nonce,
                state.CodeChallenge),
            cancellationToken);
        return Redirect(authorizationUri.AbsoluteUri);
    }

    [HttpGet("Callback")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.Write)]
    public async Task<IActionResult> Callback(
        string? code,
        string? state,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        var loginState = string.IsNullOrWhiteSpace(state)
            ? null
            : _stateService.Consume(Request, Response, state);
        if (loginState is null || !string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            return Failure("The enterprise login response was invalid or expired.");
        }

        var connection = await _connectionService.GetRuntimeContextAsync(
            loginState.ConnectionId,
            cancellationToken);
        if (connection is null
            || !connection.IsEnabled
            || !connection.LoginEnabled
            || !_providers.TryGetValue(connection.Provider, out var provider))
        {
            return Failure("The enterprise connection is disabled.");
        }

        var secret = await _secretResolver.ResolveAsync(
            connection.SecretReference,
            cancellationToken);
        if (secret is null || !Uri.TryCreate(loginState.RedirectUri, UriKind.Absolute, out var redirectUri))
        {
            return Failure("The enterprise connection is unavailable.");
        }

        var identity = await provider.RedeemAsync(
            connection,
            secret,
            new EnterpriseLoginCallback(
                redirectUri,
                code,
                loginState.CodeVerifier,
                loginState.Nonce),
            cancellationToken);
        if (identity is null)
        {
            return Failure("Microsoft Entra ID token validation failed.");
        }

        var resolution = await _enterpriseSignInService.ResolveAsync(connection, identity, cancellationToken);
        if (resolution.Status != EnterpriseSignInStatus.Succeeded || string.IsNullOrWhiteSpace(resolution.UserId))
        {
            return Failure(resolution.Status switch
            {
                EnterpriseSignInStatus.Conflict => "The enterprise identity conflicts with an existing GitCandy account and was not linked by email.",
                EnterpriseSignInStatus.NotProvisioned => "The enterprise identity has not been provisioned for this organization.",
                EnterpriseSignInStatus.Disabled => "The enterprise identity is inactive.",
                _ => "The enterprise identity could not be used."
            });
        }

        var user = await _userManager.FindByIdAsync(resolution.UserId);
        if (user is null || !await _signInManager.CanSignInAsync(user) || await _userManager.IsLockedOutAsync(user))
        {
            return Failure("The GitCandy account cannot sign in.");
        }

        await _signInManager.SignInAsync(user, isPersistent: false, $"Enterprise:{connection.Id}");
        return LocalRedirect(loginState.ReturnUrl);
    }

    private IActionResult Failure(string message) =>
        View("~/Views/AccountSecurity/ExternalLoginFailure.cshtml", model: message);
}
