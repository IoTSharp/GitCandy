using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Configuration;
using GitCandy.Data.Identity;
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
    ICurrentUser currentUser,
    IOptions<GitCandyApplicationOptions> applicationOptions) : Controller
{
    private const string InvalidCredentialsMessage = "Unable to sign in with the supplied credentials.";
    private readonly UserManager<GitCandyUser> _userManager = userManager;
    private readonly SignInManager<GitCandyUser> _signInManager = signInManager;
    private readonly IMembershipService _membershipService = membershipService;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly GitCandyApplicationOptions _applicationOptions = applicationOptions.Value;

    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Login(string? returnUrl = null)
    {
        if (_currentUser.IsAuthenticated)
        {
            return RedirectToLocal(returnUrl);
        }

        ViewData[nameof(returnUrl)] = returnUrl;
        return View();
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
                return RedirectToLocal(returnUrl);
            }
        }

        ModelState.AddModelError(string.Empty, InvalidCredentialsMessage);
        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Create()
    {
        return CanCreateAccount() ? View() : NotFound();
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

        if (!_currentUser.IsAuthenticated)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);
        }

        return RedirectToAction("Index", "Home");
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

        var result = await _userManager.ChangePasswordAsync(
            user,
            model.CurrentPassword,
            model.NewPassword);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        await _signInManager.RefreshSignInAsync(user);
        return RedirectToAction(nameof(Change), new { changed = true });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout(string? returnUrl = null)
    {
        await _signInManager.SignOutAsync();
        return RedirectToLocal(returnUrl);
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

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}
