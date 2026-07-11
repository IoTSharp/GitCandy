using System.Security.Claims;
using System.Text;
using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Configuration;
using GitCandy.Data.Identity;
using GitCandy.Models.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitCandy.Controllers;

[Route("Account")]
[AutoValidateAntiforgeryToken]
public sealed class AccountSecurityController(
    UserManager<GitCandyUser> userManager,
    SignInManager<GitCandyUser> signInManager,
    INamespaceProvisioningService namespaceProvisioningService,
    IOptions<GitCandyApplicationOptions> applicationOptions) : CandyControllerBase
{
    private readonly UserManager<GitCandyUser> _userManager = userManager;
    private readonly SignInManager<GitCandyUser> _signInManager = signInManager;
    private readonly INamespaceProvisioningService _namespaceProvisioningService = namespaceProvisioningService;
    private readonly GitCandyApplicationOptions _applicationOptions = applicationOptions.Value;

    [HttpGet("Security")]
    [Authorize]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Security(string? message = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var currentLogins = await _userManager.GetLoginsAsync(user);
        var availableSchemes = (await _signInManager.GetExternalAuthenticationSchemesAsync())
            .Where(static scheme => !string.IsNullOrWhiteSpace(scheme.DisplayName))
            .ToArray();
        var currentProviders = currentLogins
            .Select(login => new UserExternalLoginViewModel(
                login.LoginProvider,
                availableSchemes.FirstOrDefault(scheme => string.Equals(
                    scheme.Name,
                    login.LoginProvider,
                    StringComparison.Ordinal))?.DisplayName
                    ?? login.ProviderDisplayName
                    ?? login.LoginProvider))
            .ToArray();
        var linkedProviderNames = currentLogins
            .Select(static login => login.LoginProvider)
            .ToHashSet(StringComparer.Ordinal);

        return View(new AccountSecurityViewModel
        {
            HasPassword = await _userManager.HasPasswordAsync(user),
            HasAuthenticator = !string.IsNullOrWhiteSpace(await _userManager.GetAuthenticatorKeyAsync(user)),
            IsTwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user),
            RecoveryCodesLeft = await _userManager.CountRecoveryCodesAsync(user),
            IsMachineRemembered = await _signInManager.IsTwoFactorClientRememberedAsync(user),
            CurrentLogins = currentProviders,
            OtherLogins = availableSchemes
                .Where(scheme => !linkedProviderNames.Contains(scheme.Name))
                .Select(static scheme => new ExternalLoginProviderViewModel(
                    scheme.Name,
                    scheme.DisplayName ?? scheme.Name))
                .ToArray(),
            StatusMessage = message
        });
    }

    [HttpGet("EnableAuthenticator")]
    [Authorize]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> EnableAuthenticator()
    {
        var user = await _userManager.GetUserAsync(User);
        return user is null ? Challenge() : View(await CreateAuthenticatorModelAsync(user));
    }

    [HttpPost("EnableAuthenticator")]
    [Authorize]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> EnableAuthenticator(EnableAuthenticatorViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return View(await CreateAuthenticatorModelAsync(user, model.Code));
        }

        var code = NormalizeAuthenticatorCode(model.Code);
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);
        if (!isValid)
        {
            ModelState.AddModelError(nameof(model.Code), "The verification code is invalid.");
            return View(await CreateAuthenticatorModelAsync(user, model.Code));
        }

        var enabled = await _userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded)
        {
            AddIdentityErrors(enabled);
            return View(await CreateAuthenticatorModelAsync(user, model.Code));
        }

        await _signInManager.RefreshSignInAsync(user);
        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return View("RecoveryCodes", new RecoveryCodesViewModel
        {
            RecoveryCodes = recoveryCodes?.ToArray() ?? []
        });
    }

    [HttpPost("DisableAuthenticator")]
    [Authorize]
    public async Task<IActionResult> DisableAuthenticator()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!result.Succeeded)
        {
            return RedirectToAction(nameof(Security), new { message = "Unable to disable two-factor authentication." });
        }

        await _signInManager.RefreshSignInAsync(user);
        return RedirectToAction(nameof(Security), new { message = "Two-factor authentication disabled." });
    }

    [HttpPost("ResetAuthenticator")]
    [Authorize]
    public async Task<IActionResult> ResetAuthenticator()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var disabled = await _userManager.SetTwoFactorEnabledAsync(user, false);
        var reset = disabled.Succeeded
            ? await _userManager.ResetAuthenticatorKeyAsync(user)
            : disabled;
        if (!reset.Succeeded)
        {
            return RedirectToAction(nameof(Security), new { message = "Unable to reset the authenticator." });
        }

        await _signInManager.RefreshSignInAsync(user);
        return RedirectToAction(nameof(EnableAuthenticator));
    }

    [HttpPost("GenerateRecoveryCodes")]
    [Authorize]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> GenerateRecoveryCodes()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return BadRequest("Two-factor authentication must be enabled before generating recovery codes.");
        }

        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return View("RecoveryCodes", new RecoveryCodesViewModel
        {
            RecoveryCodes = recoveryCodes?.ToArray() ?? []
        });
    }

    [HttpPost("ForgetTwoFactorClient")]
    [Authorize]
    public async Task<IActionResult> ForgetTwoFactorClient()
    {
        await _signInManager.ForgetTwoFactorClientAsync();
        return RedirectToAction(nameof(Security), new { message = "This browser is no longer remembered." });
    }

    [HttpGet("LoginWithTwoFactor")]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LoginWithTwoFactor(bool rememberMe, string? returnUrl = null)
    {
        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() is null)
        {
            return RedirectToAction(nameof(AccountController.Login), "Account", new { returnUrl });
        }

        ViewData[nameof(returnUrl)] = returnUrl;
        return View(new TwoFactorLoginViewModel { RememberMe = rememberMe });
    }

    [HttpPost("LoginWithTwoFactor")]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LoginWithTwoFactor(
        TwoFactorLoginViewModel model,
        string? returnUrl = null)
    {
        ViewData[nameof(returnUrl)] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() is null)
        {
            return RedirectToAction(nameof(AccountController.Login), "Account", new { returnUrl });
        }

        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(
            NormalizeAuthenticatorCode(model.Code),
            model.RememberMe,
            model.RememberMachine);
        if (result.Succeeded)
        {
            return RedirectToStartPage(returnUrl);
        }

        ModelState.AddModelError(string.Empty, "The authenticator code is invalid.");
        return View(model);
    }

    [HttpGet("LoginWithRecoveryCode")]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LoginWithRecoveryCode(string? returnUrl = null)
    {
        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() is null)
        {
            return RedirectToAction(nameof(AccountController.Login), "Account", new { returnUrl });
        }

        ViewData[nameof(returnUrl)] = returnUrl;
        return View();
    }

    [HttpPost("LoginWithRecoveryCode")]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LoginWithRecoveryCode(
        RecoveryCodeLoginViewModel model,
        string? returnUrl = null)
    {
        ViewData[nameof(returnUrl)] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() is null)
        {
            return RedirectToAction(nameof(AccountController.Login), "Account", new { returnUrl });
        }

        var recoveryCode = model.RecoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal);
        var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
        if (result.Succeeded)
        {
            return RedirectToStartPage(returnUrl);
        }

        ModelState.AddModelError(string.Empty, "The recovery code is invalid.");
        return View(model);
    }

    [HttpPost("ExternalLogin")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLogin(string provider, string? returnUrl = null)
    {
        if (!await IsExternalProviderAsync(provider))
        {
            return NotFound();
        }

        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), values: new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    [HttpGet("ExternalLoginCallback")]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            return View("ExternalLoginFailure", model: "The external login provider returned an error.");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return View("ExternalLoginFailure", model: "External login information could not be loaded.");
        }

        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: false);
        if (result.Succeeded)
        {
            return RedirectToStartPage(returnUrl);
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToAction(nameof(LoginWithTwoFactor), new { returnUrl, rememberMe = false });
        }

        if (result.IsLockedOut)
        {
            return View("ExternalLoginFailure", model: "This account is locked.");
        }

        if (!_applicationOptions.AllowRegisterUser)
        {
            return Forbid();
        }

        var email = info.Principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        ViewData[nameof(returnUrl)] = returnUrl;
        return View("ExternalLoginConfirmation", new ExternalLoginConfirmationViewModel
        {
            Email = email,
            UserName = CreateSuggestedUserName(email, info.Principal.Identity?.Name)
        });
    }

    [HttpPost("ExternalLoginConfirmation")]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> ExternalLoginConfirmation(
        ExternalLoginConfirmationViewModel model,
        string? returnUrl = null)
    {
        ViewData[nameof(returnUrl)] = returnUrl;
        if (!_applicationOptions.AllowRegisterUser)
        {
            return Forbid();
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return View("ExternalLoginFailure", model: "External login information could not be loaded.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new GitCandyUser
        {
            UserName = model.UserName,
            Email = model.Email,
            DisplayName = model.UserName
        };
        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return View(model);
        }

        if (await _namespaceProvisioningService.EnsureUserNamespaceAsync(user.Id) is null)
        {
            await _userManager.DeleteAsync(user);
            ModelState.AddModelError(nameof(model.UserName), "The user name is reserved or already occupied by a user or team namespace.");
            return View(model);
        }

        var loginResult = await _userManager.AddLoginAsync(user, info);
        if (!loginResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            AddIdentityErrors(loginResult);
            return View(model);
        }

        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
        return RedirectToStartPage(returnUrl);
    }

    [HttpPost("LinkExternalLogin")]
    [Authorize]
    public async Task<IActionResult> LinkExternalLogin(string provider)
    {
        if (!await IsExternalProviderAsync(provider))
        {
            return NotFound();
        }

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var redirectUrl = Url.Action(nameof(LinkExternalLoginCallback));
        var userId = _userManager.GetUserId(User);
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(
            provider,
            redirectUrl,
            userId);
        return Challenge(properties, provider);
    }

    [HttpGet("LinkExternalLoginCallback")]
    [Authorize]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LinkExternalLoginCallback()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var info = await _signInManager.GetExternalLoginInfoAsync(user.Id);
        if (info is null)
        {
            return RedirectToAction(nameof(Security), new { message = "External login information could not be loaded." });
        }

        var result = await _userManager.AddLoginAsync(user, info);
        return RedirectToAction(nameof(Security), new
        {
            message = result.Succeeded
                ? $"{info.ProviderDisplayName} login linked."
                : "Unable to link the external login."
        });
    }

    [HttpPost("RemoveExternalLogin")]
    [Authorize]
    public async Task<IActionResult> RemoveExternalLogin(string loginProvider)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var logins = await _userManager.GetLoginsAsync(user);
        if (!await _userManager.HasPasswordAsync(user) && logins.Count <= 1)
        {
            return RedirectToAction(nameof(Security), new { message = "Add a password or another login before removing this login." });
        }

        var login = logins.FirstOrDefault(item => string.Equals(
            item.LoginProvider,
            loginProvider,
            StringComparison.Ordinal));
        if (login is null)
        {
            return NotFound();
        }

        var result = await _userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        if (result.Succeeded)
        {
            await _signInManager.RefreshSignInAsync(user);
        }

        return RedirectToAction(nameof(Security), new
        {
            message = result.Succeeded ? "External login removed." : "Unable to remove the external login."
        });
    }

    [HttpGet("SetPassword")]
    [Authorize]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> SetPassword()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        return await _userManager.HasPasswordAsync(user)
            ? RedirectToAction(nameof(AccountController.Change), "Account")
            : View();
    }

    [HttpPost("SetPassword")]
    [Authorize]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (await _userManager.HasPasswordAsync(user))
        {
            return RedirectToAction(nameof(AccountController.Change), "Account");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _userManager.AddPasswordAsync(user, model.NewPassword);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        await _signInManager.RefreshSignInAsync(user);
        return RedirectToAction(nameof(Security), new { message = "Password added." });
    }

    private async Task<EnableAuthenticatorViewModel> CreateAuthenticatorModelAsync(
        GitCandyUser user,
        string? code = null)
    {
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            var result = await _userManager.ResetAuthenticatorKeyAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Unable to create an authenticator key.");
            }

            await _signInManager.RefreshSignInAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Identity did not return an authenticator key.");
        }

        var accountName = user.Email ?? user.UserName ?? user.Id;
        return new EnableAuthenticatorViewModel
        {
            SharedKey = FormatKey(key),
            AuthenticatorUri = GenerateAuthenticatorUri(accountName, key),
            Code = code ?? string.Empty
        };
    }

    private async Task<bool> IsExternalProviderAsync(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return false;
        }

        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        return schemes.Any(scheme => string.Equals(scheme.Name, provider, StringComparison.Ordinal));
    }

    private static string NormalizeAuthenticatorCode(string code)
    {
        return code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
    }

    private static string FormatKey(string key)
    {
        var result = new StringBuilder();
        for (var index = 0; index < key.Length; index += 4)
        {
            if (result.Length > 0)
            {
                result.Append(' ');
            }

            result.Append(key.AsSpan(index, Math.Min(4, key.Length - index)));
        }

        return result.ToString().ToLowerInvariant();
    }

    private static string GenerateAuthenticatorUri(string accountName, string key)
    {
        const string issuer = "GitCandy";
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}" +
            $"?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6";
    }

    private static string CreateSuggestedUserName(string email, string? displayName)
    {
        var source = !string.IsNullOrWhiteSpace(email)
            ? email.Split('@', 2)[0]
            : displayName ?? "user";
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            {
                builder.Append(character);
            }
        }

        if (builder.Length == 0 || !char.IsAsciiLetter(builder[0]))
        {
            builder.Insert(0, "user-");
        }

        return builder.Length > 20 ? builder.ToString(0, 20) : builder.ToString();
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}
