using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

/// <summary>管理员 alias 生命周期操作入口。</summary>
[AutoValidateAntiforgeryToken]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
public sealed class NameManagementController(
    INameManagementService nameManagementService,
    ICurrentUser currentUser) : Controller
{
    private readonly INameManagementService _nameManagementService = nameManagementService;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>延长一个尚未释放的 alias 保留期。</summary>
    [HttpPost]
    public async Task<IActionResult> ExtendAlias(
        NameSubjectType subjectType,
        long aliasId,
        DateTime expiresAtUtc,
        string reason,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)
            || string.IsNullOrWhiteSpace(reason))
        {
            return BadRequest("An audited extension reason is required.");
        }

        var utcExpiry = DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc);
        if (!await _nameManagementService.ExtendAliasAsync(
            subjectType,
            aliasId,
            utcExpiry,
            _currentUser.UserId,
            reason,
            cancellationToken))
        {
            return BadRequest("The alias could not be extended.");
        }

        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Repository");
    }
}
