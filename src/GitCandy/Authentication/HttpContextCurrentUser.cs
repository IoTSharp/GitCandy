using System.Security.Claims;
using GitCandy.Configuration;

namespace GitCandy.Authentication;

/// <summary>
/// 基于 <see cref="IHttpContextAccessor" /> 的当前用户访问器。
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    /// <inheritdoc />
    public ClaimsPrincipal Principal => _httpContextAccessor.HttpContext?.User ?? AnonymousPrincipal;

    /// <inheritdoc />
    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public string? UserId => Principal.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <inheritdoc />
    public string? UserName => Principal.Identity?.Name;

    /// <inheritdoc />
    public bool IsAdministrator => Principal.IsInRole(RoleNames.Administrator);

    /// <inheritdoc />
    public CancellationToken RequestAborted =>
        _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
