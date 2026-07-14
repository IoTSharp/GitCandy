using System.Data;
using System.Text.Json;
using GitCandy.Application;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Enterprise;
using GitCandy.Teams;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitCandy.Web.Enterprise;

/// <summary>把已验证的稳定企业身份绑定到 ASP.NET Core Identity。</summary>
public sealed class EnterpriseSignInService(
    GitCandyDbContext dbContext,
    UserManager<GitCandyUser> userManager,
    INamespaceProvisioningService namespaceProvisioningService,
    IOptions<GitCandyApplicationOptions> applicationOptions,
    TimeProvider timeProvider) : IEnterpriseSignInService
{
    private const int MaxExternalIdLength = 256;
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly UserManager<GitCandyUser> _userManager = userManager;
    private readonly INamespaceProvisioningService _namespaceProvisioningService = namespaceProvisioningService;
    private readonly GitCandyApplicationOptions _applicationOptions = applicationOptions.Value;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<EnterpriseSignInResult> ResolveAsync(
        EnterpriseConnectionContext connection,
        EnterpriseLoginIdentity identity,
        CancellationToken cancellationToken = default)
    {
        if (!connection.IsEnabled
            || !connection.LoginEnabled
            || !string.Equals(
                connection.ExternalOrganizationId,
                identity.TenantId,
                StringComparison.OrdinalIgnoreCase))
        {
            return new EnterpriseSignInResult(EnterpriseSignInStatus.Disabled, ErrorCode: "connection_disabled");
        }

        if (string.IsNullOrWhiteSpace(identity.ExternalId)
            || identity.ExternalId.Length > MaxExternalIdLength)
        {
            return new EnterpriseSignInResult(EnterpriseSignInStatus.InvalidIdentity, ErrorCode: "external_id_invalid");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var persistedConnection = await _dbContext.EnterpriseConnections.SingleOrDefaultAsync(
            item => item.Id == connection.Id && item.IsEnabled && item.LoginEnabled,
            cancellationToken);
        if (persistedConnection is null)
        {
            return new EnterpriseSignInResult(EnterpriseSignInStatus.Disabled, ErrorCode: "connection_disabled");
        }

        var externalIdentity = await _dbContext.EnterpriseExternalIdentities.SingleOrDefaultAsync(
            item => item.ConnectionId == connection.Id && item.ExternalId == identity.ExternalId,
            cancellationToken);
        if (externalIdentity is not null && !externalIdentity.IsActive)
        {
            return new EnterpriseSignInResult(EnterpriseSignInStatus.Disabled, ErrorCode: "identity_inactive");
        }

        GitCandyUser? user = null;
        if (!string.IsNullOrWhiteSpace(externalIdentity?.UserId))
        {
            user = await _userManager.FindByIdAsync(externalIdentity.UserId);
            if (user is null)
            {
                return new EnterpriseSignInResult(EnterpriseSignInStatus.Conflict, ErrorCode: "mapped_user_missing");
            }
        }

        if (user is null && !string.IsNullOrWhiteSpace(identity.Email))
        {
            var existingByEmail = await _userManager.FindByEmailAsync(identity.Email);
            if (existingByEmail is not null)
            {
                return new EnterpriseSignInResult(EnterpriseSignInStatus.Conflict, ErrorCode: "email_conflict");
            }
        }

        var created = false;
        if (user is null)
        {
            if (!_applicationOptions.AllowRegisterUser || !AllowsJit(connection.ConfigurationJson))
            {
                return new EnterpriseSignInResult(EnterpriseSignInStatus.NotProvisioned, ErrorCode: "identity_not_provisioned");
            }

            var userName = await CreateAvailableUserNameAsync(identity.UserName, cancellationToken);
            if (userName is null || string.IsNullOrWhiteSpace(identity.Email))
            {
                return new EnterpriseSignInResult(EnterpriseSignInStatus.InvalidIdentity, ErrorCode: "profile_incomplete");
            }

            user = new GitCandyUser
            {
                UserName = userName,
                Email = identity.Email,
                EmailConfirmed = true,
                DisplayName = string.IsNullOrWhiteSpace(identity.DisplayName) ? userName : identity.DisplayName
            };
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return new EnterpriseSignInResult(EnterpriseSignInStatus.Conflict, ErrorCode: "user_create_conflict");
            }

            if (await _namespaceProvisioningService.EnsureUserNamespaceAsync(user.Id, cancellationToken) is null)
            {
                return new EnterpriseSignInResult(EnterpriseSignInStatus.Conflict, ErrorCode: "namespace_conflict");
            }

            created = true;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (externalIdentity is null)
        {
            externalIdentity = new GitCandyEnterpriseExternalIdentity
            {
                ConnectionId = connection.Id,
                ExternalId = identity.ExternalId,
                FirstSeenAtUtc = now
            };
            _dbContext.EnterpriseExternalIdentities.Add(externalIdentity);
        }

        externalIdentity.UserId = user.Id;
        externalIdentity.UserName = identity.UserName;
        externalIdentity.NormalizedUserName = identity.UserName.ToUpperInvariant();
        externalIdentity.Email = identity.Email;
        externalIdentity.DisplayName = identity.DisplayName;
        externalIdentity.IsActive = true;
        externalIdentity.LastSeenAtUtc = now;
        externalIdentity.DeprovisionedAtUtc = null;

        if (!await _dbContext.UserTeamRoles.AnyAsync(
                role => role.TeamId == connection.TeamId && role.UserId == user.Id,
                cancellationToken))
        {
            _dbContext.UserTeamRoles.Add(new GitCandyUserTeamRole
            {
                TeamId = connection.TeamId,
                UserId = user.Id,
                Role = TeamRole.Member
            });
        }

        var loginProvider = $"Enterprise:{connection.Id}";
        var existingLogins = await _userManager.GetLoginsAsync(user);
        if (!existingLogins.Any(login =>
                string.Equals(login.LoginProvider, loginProvider, StringComparison.Ordinal)
                && string.Equals(login.ProviderKey, identity.ExternalId, StringComparison.Ordinal)))
        {
            var loginResult = await _userManager.AddLoginAsync(
                user,
                new UserLoginInfo(loginProvider, identity.ExternalId, connection.Name));
            if (!loginResult.Succeeded)
            {
                return new EnterpriseSignInResult(EnterpriseSignInStatus.Conflict, ErrorCode: "external_login_conflict");
            }
        }

        _dbContext.TeamAuditEvents.Add(new GitCandyTeamAuditEvent
        {
            TeamId = connection.TeamId,
            TeamName = connection.TeamName,
            ActorUserId = user.Id,
            ActorName = user.UserName ?? user.Id,
            Action = created ? "enterprise.login.jit" : "enterprise.login",
            Outcome = "succeeded",
            Subject = identity.ExternalId,
            Detail = $"Provider={connection.Provider}; connection={connection.Id}.",
            OccurredAtUtc = now
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new EnterpriseSignInResult(EnterpriseSignInStatus.Succeeded, user.Id);
    }

    private async Task<string?> CreateAvailableUserNameAsync(
        string source,
        CancellationToken cancellationToken)
    {
        var localPart = source.Split('@', 2)[0];
        var sanitized = new string(localPart
            .Where(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            .ToArray());
        if (sanitized.Length == 0 || !char.IsAsciiLetter(sanitized[0]))
        {
            sanitized = $"u{sanitized}";
        }

        sanitized = sanitized[..Math.Min(16, sanitized.Length)];
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = attempt == 0 ? sanitized : $"{sanitized}{attempt}";
            if (candidate.Length > 20)
            {
                candidate = candidate[..20];
            }

            if (await _userManager.FindByNameAsync(candidate) is null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool AllowsJit(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(configurationJson);
            return document.RootElement.TryGetProperty("allowJit", out var allowJit)
                && allowJit.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
