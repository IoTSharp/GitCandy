using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Data.Identity;
using GitCandy.Models;
using GitCandy.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitCandy.Controllers;

[AutoValidateAntiforgeryToken]
public sealed class AccountController(
    UserManager<GitCandyUser> userManager,
    SignInManager<GitCandyUser> signInManager,
    IMembershipService membershipService,
    INamespaceProvisioningService namespaceProvisioningService,
    INameManagementService nameManagementService,
    IUserAdministrationService userAdministrationService,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser,
    IOptions<GitCandyApplicationOptions> applicationOptions) : CandyControllerBase
{
    private const string InvalidCredentialsMessage = "Unable to sign in with the supplied credentials.";
    private readonly UserManager<GitCandyUser> _userManager = userManager;
    private readonly SignInManager<GitCandyUser> _signInManager = signInManager;
    private readonly IMembershipService _membershipService = membershipService;
    private readonly INamespaceProvisioningService _namespaceProvisioningService = namespaceProvisioningService;
    private readonly INameManagementService _nameManagementService = nameManagementService;
    private readonly IUserAdministrationService _userAdministrationService = userAdministrationService;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly GitCandyApplicationOptions _applicationOptions = applicationOptions.Value;

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<IActionResult> Index(string? query, CancellationToken cancellationToken)
    {
        return View(new AccountIndexViewModel
        {
            Query = query,
            Users = await _userAdministrationService.GetUsersAsync(query, cancellationToken)
        });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Detail(string? name, CancellationToken cancellationToken)
    {
        if (!_applicationOptions.IsPublicServer && !_currentUser.IsAuthenticated)
        {
            return Challenge();
        }

        var effectiveName = string.IsNullOrWhiteSpace(name) ? _currentUser.UserName : name;
        if (string.IsNullOrWhiteSpace(effectiveName))
        {
            return NotFound();
        }

        var user = await _userAdministrationService.GetUserAsync(
            effectiveName,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var canEdit = (await _authorizationService.AuthorizeAsync(
            User,
            new CurrentUserAuthorizationResource(effectiveName),
            AuthorizationPolicies.CurrentUser)).Succeeded;
        return View(new AccountDetailsViewModel { User = user, CanEdit = canEdit });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Search(string? query, CancellationToken cancellationToken)
    {
        var users = await _userAdministrationService.GetUsersAsync(query, cancellationToken);
        return Json(users.Take(10).Select(static user => user.UserName));
    }

    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (_currentUser.IsAuthenticated)
        {
            return RedirectToStartPage(returnUrl);
        }

        ViewData[nameof(returnUrl)] = returnUrl;
        return View(new LoginViewModel
        {
            ExternalProviders = await GetExternalLoginProvidersAsync()
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        ViewData[nameof(returnUrl)] = returnUrl;
        if (!ModelState.IsValid)
        {
            model.ExternalProviders = await GetExternalLoginProvidersAsync();
            return View(model);
        }

        var user = await _membershipService.FindUserAsync(model.UserNameOrEmail, cancellationToken);
        if (user is not null)
        {
            var result = await _signInManager.PasswordSignInAsync(
                user,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true);
            if (result.Succeeded)
            {
                return RedirectToStartPage(returnUrl);
            }

            if (result.RequiresTwoFactor)
            {
                return RedirectToAction(
                    nameof(AccountSecurityController.LoginWithTwoFactor),
                    "AccountSecurity",
                    new { returnUrl, model.RememberMe });
            }
        }

        ModelState.AddModelError(string.Empty, InvalidCredentialsMessage);
        model.ExternalProviders = await GetExternalLoginProvidersAsync();
        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Create()
    {
        return CanCreateAccount() ? View() : NotFound();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Forgot()
    {
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Create(
        CreateAccountViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!CanCreateAccount())
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var user = new GitCandyUser
        {
            UserName = model.UserName,
            Email = model.Email,
            DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.UserName : model.DisplayName.Trim(),
            Description = model.Description?.Trim()
        };
        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        if (await _namespaceProvisioningService.EnsureUserNamespaceAsync(user.Id, cancellationToken) is null)
        {
            await _userManager.DeleteAsync(user);
            ModelState.AddModelError(nameof(model.UserName), "The user name is reserved or already occupied by a user or team namespace.");
            return View(model);
        }

        if (!_currentUser.IsAuthenticated)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToStartPage();
        }

        return RedirectToAction(nameof(Detail), new { name = user.UserName });
    }

    [HttpGet]
    [Authorize]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Change(bool changed = false)
    {
        ViewData[nameof(changed)] = changed;
        return View();
    }

    [HttpPost]
    [Authorize]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Change(
        ChangePasswordViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        await _signInManager.RefreshSignInAsync(user);
        return RedirectToAction(nameof(Change), new { changed = true });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Edit(string? name, CancellationToken cancellationToken)
    {
        var effectiveName = string.IsNullOrWhiteSpace(name) ? _currentUser.UserName : name;
        if (string.IsNullOrWhiteSpace(effectiveName))
        {
            return NotFound();
        }

        var denied = await RequireCurrentUserAsync(effectiveName);
        if (denied is not null)
        {
            return denied;
        }

        var user = await _userAdministrationService.GetUserAsync(
            effectiveName,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
        return user is null
            ? NotFound()
            : View(new EditAccountViewModel
            {
                UserName = user.UserName,
                DisplayName = user.DisplayName,
                Email = user.Email,
                Description = user.Description,
                IsAdministrator = user.IsAdministrator
            });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Edit(EditAccountViewModel model, CancellationToken cancellationToken)
    {
        var denied = await RequireCurrentUserAsync(model.UserName);
        if (denied is not null)
        {
            return denied;
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var actor = await _userManager.GetUserAsync(User);
        if (actor is null || !await _userManager.CheckPasswordAsync(actor, model.CurrentPassword))
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "The current password is incorrect.");
            return View(model);
        }

        if (string.Equals(actor.UserName, model.UserName, StringComparison.OrdinalIgnoreCase)
            && _currentUser.IsAdministrator
            && !model.IsAdministrator)
        {
            ModelState.AddModelError(nameof(model.IsAdministrator), "Administrators cannot remove their own role.");
            return View(model);
        }

        var isAdministrator = _currentUser.IsAdministrator && model.IsAdministrator;
        var result = await _userAdministrationService.UpdateUserAsync(
            model.UserName,
            model.DisplayName,
            model.Email,
            model.Description,
            isAdministrator,
            cancellationToken);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        if (string.Equals(actor.UserName, model.UserName, StringComparison.OrdinalIgnoreCase))
        {
            var updatedActor = await _userManager.FindByNameAsync(model.UserName);
            if (updatedActor is not null)
            {
                await _signInManager.RefreshSignInAsync(updatedActor);
            }
        }

        return RedirectToAction(nameof(Detail), new { name = model.UserName });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Ssh(string? name, CancellationToken cancellationToken)
    {
        var effectiveName = string.IsNullOrWhiteSpace(name) ? _currentUser.UserName : name;
        if (string.IsNullOrWhiteSpace(effectiveName))
        {
            return NotFound();
        }

        var denied = await RequireCurrentUserAsync(effectiveName);
        if (denied is not null)
        {
            return denied;
        }

        var keys = await _userAdministrationService.GetSshKeysAsync(effectiveName, cancellationToken);
        return keys is null
            ? NotFound()
            : View(new SshKeysViewModel { UserName = effectiveName, Keys = keys });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Rename(string? name, CancellationToken cancellationToken)
    {
        var effectiveName = string.IsNullOrWhiteSpace(name) ? _currentUser.UserName : name;
        var denied = await RequireCurrentUserAsync(effectiveName);
        if (denied is not null || string.IsNullOrWhiteSpace(effectiveName))
        {
            return denied ?? NotFound();
        }

        var snapshot = await _nameManagementService.GetNamespaceSnapshotAsync(effectiveName, cancellationToken);
        return snapshot is null
            ? NotFound()
            : View("~/Views/Shared/Rename.cshtml", new NameChangeViewModel
            {
                CurrentSlug = snapshot.CurrentSlug,
                NewSlug = snapshot.CurrentSlug,
                SubjectType = NameSubjectType.Namespace,
                Snapshot = snapshot
            });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Rename(NameChangeViewModel model, CancellationToken cancellationToken)
    {
        var denied = await RequireCurrentUserAsync(model.CurrentSlug);
        if (denied is not null)
        {
            return denied;
        }

        var snapshot = await _nameManagementService.GetNamespaceSnapshotAsync(model.CurrentSlug, cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }

        model.Snapshot = snapshot;
        model.SubjectType = NameSubjectType.Namespace;
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return View("~/Views/Shared/Rename.cshtml", model);
        }

        var changeOverride = _currentUser.IsAdministrator && model.UseOverride
            ? new NameChangeOverride(model.OverrideReason ?? string.Empty, model.ConfirmOverride)
            : null;
        var result = await _nameManagementService.RenameNamespaceAsync(
            snapshot.SubjectId,
            model.NewSlug,
            _currentUser.UserId,
            changeOverride,
            cancellationToken);
        if (result.Status != NameChangeStatus.Succeeded)
        {
            ModelState.AddModelError(nameof(model.NewSlug), GetNameChangeError(result.Status));
            return View("~/Views/Shared/Rename.cshtml", model);
        }

        var renamedUser = await _userManager.FindByNameAsync(result.CanonicalSlug!);
        if (renamedUser is not null && renamedUser.Id == _currentUser.UserId)
        {
            await _signInManager.RefreshSignInAsync(renamedUser);
        }

        return RedirectToAction(nameof(Detail), new { name = result.CanonicalSlug });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ChooseSsh(
        AddSshKeyViewModel model,
        string act = "add",
        string? fingerprint = null,
        CancellationToken cancellationToken = default)
    {
        var denied = await RequireCurrentUserAsync(model.User);
        if (denied is not null)
        {
            return denied;
        }

        if (string.Equals(act, "del", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(fingerprint)
                && await _userAdministrationService.DeleteSshKeyAsync(model.User, fingerprint, cancellationToken)
                    ? Json("success")
                    : BadRequest("Unable to remove the SSH key.");
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var addedFingerprint = await _userAdministrationService.AddSshKeyAsync(
            model.User,
            model.PublicKey,
            cancellationToken);
        return addedFingerprint is null
            ? BadRequest("The SSH public key is invalid or already exists.")
            : Json(addedFingerprint);
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<IActionResult> Delete(string name, CancellationToken cancellationToken)
    {
        var user = await _userAdministrationService.GetUserAsync(
            name,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
        return user is null ? NotFound() : View(model: user.UserName);
    }

    [HttpPost, ActionName(nameof(Delete))]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<IActionResult> DeleteConfirmed(string name, CancellationToken cancellationToken)
    {
        if (string.Equals(_currentUser.UserName, name, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "Administrators cannot delete their own account.");
            return View(nameof(Delete), model: name);
        }

        var result = await _userAdministrationService.DeleteUserAsync(name, cancellationToken);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(nameof(Delete), model: name);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout(string? returnUrl = null)
    {
        await _signInManager.SignOutAsync();
        return RedirectToStartPage(returnUrl);
    }

    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private bool CanCreateAccount()
    {
        return _applicationOptions.AllowRegisterUser || _currentUser.IsAdministrator;
    }

    private async Task<IActionResult?> RequireCurrentUserAsync(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return NotFound();
        }

        var result = await _authorizationService.AuthorizeAsync(
            User,
            new CurrentUserAuthorizationResource(userName),
            AuthorizationPolicies.CurrentUser);
        return result.Succeeded ? null : Forbid();
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }

    private static string GetNameChangeError(NameChangeStatus status)
    {
        return status switch
        {
            NameChangeStatus.InvalidSlug => "The URL slug is invalid.",
            NameChangeStatus.Reserved => "The URL slug is reserved by the application.",
            NameChangeStatus.Occupied => "The URL slug is already occupied by a current name or retained alias.",
            NameChangeStatus.RateLimited => "The rolling rename limit has been reached.",
            NameChangeStatus.ConfirmationRequired => "An override requires a reason and explicit confirmation.",
            NameChangeStatus.Conflict => "Another rename completed concurrently. Reload and try again.",
            _ => "The name could not be changed."
        };
    }

    private async Task<IReadOnlyList<ExternalLoginProviderViewModel>> GetExternalLoginProvidersAsync()
    {
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        return schemes
            .Where(static scheme => !string.IsNullOrWhiteSpace(scheme.DisplayName))
            .Select(static scheme => new ExternalLoginProviderViewModel(
                scheme.Name,
                scheme.DisplayName ?? scheme.Name))
            .ToArray();
    }
}
