using System.Data;
using GitCandy.Application;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Enterprise;
using GitCandy.Teams;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Web.Enterprise;

/// <summary>SCIM 2.0 Users/Groups 对 Identity 与团队成员关系的持久化实现。</summary>
public sealed class ScimProvisioningService(
    GitCandyDbContext dbContext,
    UserManager<GitCandyUser> userManager,
    INamespaceProvisioningService namespaceProvisioningService,
    TimeProvider timeProvider) : IScimProvisioningService
{
    private const int MaxPageSize = 100;
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly UserManager<GitCandyUser> _userManager = userManager;
    private readonly INamespaceProvisioningService _namespaceProvisioningService = namespaceProvisioningService;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<ScimPage<ScimUserResource>> GetUsersAsync(
        long connectionId,
        int startIndex,
        int count,
        string? filterAttribute,
        string? filterValue,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EnterpriseExternalIdentities.AsNoTracking()
            .Where(item => item.ConnectionId == connectionId);
        query = filterAttribute switch
        {
            "externalId" => query.Where(item => item.ExternalId == filterValue),
            "userName" => query.Where(item => item.NormalizedUserName == (filterValue ?? string.Empty).ToUpper()),
            _ => query
        };
        var total = await query.CountAsync(cancellationToken);
        var normalizedStart = Math.Max(1, startIndex);
        var normalizedCount = Math.Clamp(count, 0, MaxPageSize);
        var entities = await query.OrderBy(item => item.Id)
            .Skip(normalizedStart - 1)
            .Take(normalizedCount)
            .ToArrayAsync(cancellationToken);
        return new ScimPage<ScimUserResource>(
            total,
            normalizedStart,
            entities.Length,
            entities.Select(ToUserResource).ToArray());
    }

    public async Task<ScimUserResource?> GetUserAsync(
        long connectionId,
        long resourceId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.EnterpriseExternalIdentities.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ConnectionId == connectionId && item.Id == resourceId,
                cancellationToken);
        return entity is null ? null : ToUserResource(entity);
    }

    public async Task<ScimWriteResult<ScimUserResource>> UpsertUserAsync(
        long connectionId,
        ScimUserData user,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidUser(user))
        {
            return Failed<ScimUserResource>("invalidValue");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var connection = await GetActiveConnectionAsync(connectionId, cancellationToken);
        if (connection?.Team is null)
        {
            return Failed<ScimUserResource>("notFound");
        }

        var identity = await _dbContext.EnterpriseExternalIdentities.SingleOrDefaultAsync(
            item => item.ConnectionId == connectionId && item.ExternalId == user.ExternalId,
            cancellationToken);
        var created = identity is null;
        var result = await ApplyUserAsync(connection, identity, user, cancellationToken);
        if (!result.Succeeded)
        {
            return result;
        }

        await transaction.CommitAsync(cancellationToken);
        return result with { Created = created };
    }

    public async Task<ScimWriteResult<ScimUserResource>> PatchUserAsync(
        long connectionId,
        long resourceId,
        ScimUserData user,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidUser(user))
        {
            return Failed<ScimUserResource>("invalidValue");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var connection = await GetActiveConnectionAsync(connectionId, cancellationToken);
        var identity = await _dbContext.EnterpriseExternalIdentities.SingleOrDefaultAsync(
            item => item.ConnectionId == connectionId && item.Id == resourceId,
            cancellationToken);
        if (connection?.Team is null || identity is null)
        {
            return Failed<ScimUserResource>("notFound");
        }

        var stableUser = user with { ExternalId = identity.ExternalId };
        var result = await ApplyUserAsync(connection, identity, stableUser, cancellationToken);
        if (result.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<ScimPage<ScimGroupResource>> GetGroupsAsync(
        long connectionId,
        int startIndex,
        int count,
        string? filterAttribute,
        string? filterValue,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EnterpriseGroups.AsNoTracking()
            .Where(item => item.ConnectionId == connectionId);
        query = filterAttribute switch
        {
            "externalId" => query.Where(item => item.ExternalId == filterValue),
            "displayName" => query.Where(item => item.DisplayName == filterValue),
            _ => query
        };
        var total = await query.CountAsync(cancellationToken);
        var normalizedStart = Math.Max(1, startIndex);
        var normalizedCount = Math.Clamp(count, 0, MaxPageSize);
        var groups = await query.OrderBy(item => item.Id)
            .Skip(normalizedStart - 1)
            .Take(normalizedCount)
            .ToArrayAsync(cancellationToken);
        var resources = new List<ScimGroupResource>(groups.Length);
        foreach (var group in groups)
        {
            resources.Add(await ToGroupResourceAsync(group, cancellationToken));
        }

        return new ScimPage<ScimGroupResource>(total, normalizedStart, resources.Count, resources);
    }

    public async Task<ScimGroupResource?> GetGroupAsync(
        long connectionId,
        long resourceId,
        CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.EnterpriseGroups.AsNoTracking().SingleOrDefaultAsync(
            item => item.ConnectionId == connectionId && item.Id == resourceId,
            cancellationToken);
        return group is null ? null : await ToGroupResourceAsync(group, cancellationToken);
    }

    public async Task<ScimWriteResult<ScimGroupResource>> UpsertGroupAsync(
        long connectionId,
        ScimGroupData group,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidGroup(group))
        {
            return Failed<ScimGroupResource>("invalidValue");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var connection = await GetActiveConnectionAsync(connectionId, cancellationToken);
        if (connection?.Team is null)
        {
            return Failed<ScimGroupResource>("notFound");
        }

        var entity = await _dbContext.EnterpriseGroups.SingleOrDefaultAsync(
            item => item.ConnectionId == connectionId && item.ExternalId == group.ExternalId,
            cancellationToken);
        var created = entity is null;
        var result = await ApplyGroupAsync(connection, entity, group, cancellationToken);
        if (result.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result with { Created = created && result.Succeeded };
    }

    public async Task<ScimWriteResult<ScimGroupResource>> PatchGroupAsync(
        long connectionId,
        long resourceId,
        ScimGroupData group,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidGroup(group))
        {
            return Failed<ScimGroupResource>("invalidValue");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var connection = await GetActiveConnectionAsync(connectionId, cancellationToken);
        var entity = await _dbContext.EnterpriseGroups.SingleOrDefaultAsync(
            item => item.ConnectionId == connectionId && item.Id == resourceId,
            cancellationToken);
        if (connection?.Team is null || entity is null)
        {
            return Failed<ScimGroupResource>("notFound");
        }

        var stableGroup = group with { ExternalId = entity.ExternalId };
        var result = await ApplyGroupAsync(connection, entity, stableGroup, cancellationToken);
        if (result.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<EnterpriseDeprovisionResult> DeactivateMissingUsersAsync(
        long connectionId,
        IReadOnlyCollection<string> activeExternalIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeExternalIds);
        var activeIds = activeExternalIds.ToHashSet(StringComparer.Ordinal);
        var missing = await _dbContext.EnterpriseExternalIdentities.AsNoTracking()
            .Where(item => item.ConnectionId == connectionId && item.IsActive)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.ExternalId,
                item.UserName,
                item.Email,
                item.DisplayName
            })
            .ToArrayAsync(cancellationToken);
        var deactivated = 0;
        var protectedOwners = 0;
        var failed = 0;
        foreach (var item in missing.Where(item => !activeIds.Contains(item.ExternalId)))
        {
            var result = await PatchUserAsync(
                connectionId,
                item.Id,
                new ScimUserData(
                    item.ExternalId,
                    item.UserName,
                    item.Email,
                    item.DisplayName,
                    Active: false),
                cancellationToken);
            if (result.Succeeded)
            {
                deactivated++;
            }
            else if (string.Equals(result.ErrorCode, "lastTeamOwner", StringComparison.Ordinal))
            {
                protectedOwners++;
            }
            else
            {
                failed++;
            }
        }

        return new EnterpriseDeprovisionResult(deactivated, protectedOwners, failed);
    }

    private async Task<ScimWriteResult<ScimUserResource>> ApplyUserAsync(
        GitCandyEnterpriseConnection connection,
        GitCandyEnterpriseExternalIdentity? identity,
        ScimUserData input,
        CancellationToken cancellationToken)
    {
        GitCandyUser? identityUser = null;
        if (!string.IsNullOrWhiteSpace(identity?.UserId))
        {
            identityUser = await _userManager.FindByIdAsync(identity.UserId);
            if (identityUser is null)
            {
                return Failed<ScimUserResource>("mappedUserMissing");
            }
        }

        if (!string.IsNullOrWhiteSpace(input.Email))
        {
            var emailOwner = await _userManager.FindByEmailAsync(input.Email);
            if (emailOwner is not null && emailOwner.Id != identityUser?.Id)
            {
                return Failed<ScimUserResource>("uniqueness");
            }
        }

        var created = identity is null;
        if (identityUser is null)
        {
            var existingName = await _userManager.FindByNameAsync(input.UserName);
            if (existingName is not null || string.IsNullOrWhiteSpace(input.Email))
            {
                return Failed<ScimUserResource>("uniqueness");
            }

            identityUser = new GitCandyUser
            {
                UserName = input.UserName,
                Email = input.Email,
                EmailConfirmed = true,
                DisplayName = input.DisplayName ?? input.UserName,
                LockoutEnabled = true
            };
            var createResult = await _userManager.CreateAsync(identityUser);
            if (!createResult.Succeeded)
            {
                return Failed<ScimUserResource>("uniqueness");
            }

            if (await _namespaceProvisioningService.EnsureUserNamespaceAsync(
                    identityUser.Id,
                    cancellationToken) is null)
            {
                return Failed<ScimUserResource>("uniqueness");
            }
        }

        if (identity is null)
        {
            identity = new GitCandyEnterpriseExternalIdentity
            {
                ConnectionId = connection.Id,
                ExternalId = input.ExternalId,
                FirstSeenAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            };
            _dbContext.EnterpriseExternalIdentities.Add(identity);
        }

        var membership = await _dbContext.UserTeamRoles.SingleOrDefaultAsync(
            role => role.TeamId == connection.TeamId && role.UserId == identityUser.Id,
            cancellationToken);
        if (!input.Active && membership?.Role == TeamRole.TeamOwner)
        {
            var anotherOwnerExists = await _dbContext.UserTeamRoles.AsNoTracking().AnyAsync(
                role => role.TeamId == connection.TeamId
                    && role.UserId != identityUser.Id
                    && role.Role == TeamRole.TeamOwner,
                cancellationToken);
            if (!anotherOwnerExists)
            {
                return Failed<ScimUserResource>("lastTeamOwner");
            }
        }

        identity.UserId = identityUser.Id;
        identity.UserName = input.UserName;
        identity.NormalizedUserName = input.UserName.ToUpperInvariant();
        identity.Email = input.Email;
        identity.DisplayName = input.DisplayName;
        identity.IsActive = input.Active;
        identity.LastSeenAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        identity.DeprovisionedAtUtc = input.Active ? null : identity.LastSeenAtUtc;
        identityUser.DisplayName = input.DisplayName ?? identityUser.DisplayName;
        if (!string.IsNullOrWhiteSpace(input.Email))
        {
            identityUser.Email = input.Email;
            identityUser.NormalizedEmail = _userManager.NormalizeEmail(input.Email);
        }

        if (input.Active)
        {
            if (identityUser.LockoutEnd == DateTimeOffset.MaxValue)
            {
                identityUser.LockoutEnd = null;
            }

            if (membership is null)
            {
                _dbContext.UserTeamRoles.Add(new GitCandyUserTeamRole
                {
                    TeamId = connection.TeamId,
                    UserId = identityUser.Id,
                    Role = TeamRole.Member
                });
            }
        }
        else
        {
            var hasOtherActiveIdentity = await _dbContext.EnterpriseExternalIdentities.AsNoTracking().AnyAsync(
                item => item.Id != identity.Id
                    && item.UserId == identityUser.Id
                    && item.IsActive,
                cancellationToken);
            if (!hasOtherActiveIdentity)
            {
                identityUser.LockoutEnabled = true;
                identityUser.LockoutEnd = DateTimeOffset.MaxValue;
                identityUser.SecurityStamp = Guid.NewGuid().ToString("N");
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var tokens = await _dbContext.PersonalAccessTokens
                    .Where(token => token.UserId == identityUser.Id && token.RevokedAtUtc == null)
                    .ToArrayAsync(cancellationToken);
                foreach (var token in tokens)
                {
                    token.RevokedAtUtc = now;
                    _dbContext.CredentialAuditEvents.Add(new GitCandyCredentialAuditEvent
                    {
                        CredentialKind = "personal-access-token",
                        CredentialId = token.Id,
                        ActorUserId = identityUser.Id,
                        Action = "deprovision-revoke",
                        Outcome = "succeeded",
                        Detail = $"Enterprise connection {connection.Id} deprovisioned the user.",
                        OccurredAtUtc = now
                    });
                }

                var sshKeys = await _dbContext.SshKeys
                    .Where(key => key.UserId == identityUser.Id)
                    .ToArrayAsync(cancellationToken);
                var fingerprints = sshKeys.Select(key => key.Fingerprint).ToArray();
                var claims = await _dbContext.SshFingerprintClaims
                    .Where(claim => fingerprints.Contains(claim.Fingerprint))
                    .ToArrayAsync(cancellationToken);
                _dbContext.SshKeys.RemoveRange(sshKeys);
                _dbContext.SshFingerprintClaims.RemoveRange(claims);
            }

            if (membership is not null)
            {
                _dbContext.UserTeamRoles.Remove(membership);
            }
        }

        AddAudit(connection, created ? "scim.user.create" : "scim.user.update", input.ExternalId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new ScimWriteResult<ScimUserResource>(
            true,
            created,
            ToUserResource(identity));
    }

    private async Task<ScimWriteResult<ScimGroupResource>> ApplyGroupAsync(
        GitCandyEnterpriseConnection connection,
        GitCandyEnterpriseGroup? entity,
        ScimGroupData input,
        CancellationToken cancellationToken)
    {
        var distinctMemberIds = input.MemberIds.Distinct().ToArray();
        var members = await _dbContext.EnterpriseExternalIdentities
            .Where(item => item.ConnectionId == connection.Id && distinctMemberIds.Contains(item.Id))
            .ToArrayAsync(cancellationToken);
        if (members.Length != distinctMemberIds.Length)
        {
            return Failed<ScimGroupResource>("invalidValue");
        }

        var created = entity is null;
        if (entity is null)
        {
            entity = new GitCandyEnterpriseGroup
            {
                ConnectionId = connection.Id,
                ExternalId = input.ExternalId,
                CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            };
            _dbContext.EnterpriseGroups.Add(entity);
        }

        entity.DisplayName = input.DisplayName;
        entity.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _dbContext.SaveChangesAsync(cancellationToken);
        var existingLinks = await _dbContext.EnterpriseGroupMembers
            .Where(item => item.GroupId == entity.Id)
            .ToArrayAsync(cancellationToken);
        _dbContext.EnterpriseGroupMembers.RemoveRange(existingLinks);
        foreach (var member in members)
        {
            _dbContext.EnterpriseGroupMembers.Add(new GitCandyEnterpriseGroupMember
            {
                GroupId = entity.Id,
                ExternalIdentityId = member.Id
            });
        }

        AddAudit(connection, created ? "scim.group.create" : "scim.group.update", input.ExternalId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new ScimWriteResult<ScimGroupResource>(
            true,
            created,
            new ScimGroupResource(
                entity.Id,
                entity.ExternalId,
                entity.DisplayName,
                distinctMemberIds,
                ToDateTimeOffset(entity.CreatedAtUtc),
                ToDateTimeOffset(entity.UpdatedAtUtc)));
    }

    private async Task<GitCandyEnterpriseConnection?> GetActiveConnectionAsync(
        long connectionId,
        CancellationToken cancellationToken) =>
        await _dbContext.EnterpriseConnections
            .Include(connection => connection.Team)
            .SingleOrDefaultAsync(
                connection => connection.Id == connectionId
                    && connection.IsEnabled
                    && connection.ProvisioningEnabled,
                cancellationToken);

    private async Task<ScimGroupResource> ToGroupResourceAsync(
        GitCandyEnterpriseGroup group,
        CancellationToken cancellationToken)
    {
        var memberIds = await _dbContext.EnterpriseGroupMembers.AsNoTracking()
            .Where(item => item.GroupId == group.Id)
            .OrderBy(item => item.ExternalIdentityId)
            .Select(item => item.ExternalIdentityId)
            .ToArrayAsync(cancellationToken);
        return new ScimGroupResource(
            group.Id,
            group.ExternalId,
            group.DisplayName,
            memberIds,
            ToDateTimeOffset(group.CreatedAtUtc),
            ToDateTimeOffset(group.UpdatedAtUtc));
    }

    private void AddAudit(GitCandyEnterpriseConnection connection, string action, string subject)
    {
        _dbContext.TeamAuditEvents.Add(new GitCandyTeamAuditEvent
        {
            TeamId = connection.TeamId,
            TeamName = connection.Team?.Name ?? connection.TeamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ActorName = $"scim:{connection.Id}",
            Action = action,
            Outcome = "succeeded",
            Subject = subject,
            Detail = $"Connection={connection.Id}.",
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        });
    }

    private static bool IsValidUser(ScimUserData user) =>
        !string.IsNullOrWhiteSpace(user.ExternalId)
        && user.ExternalId.Length <= 256
        && !string.IsNullOrWhiteSpace(user.UserName)
        && user.UserName.Length <= 128
        && (user.Email is null || user.Email.Length <= 128)
        && (user.DisplayName is null || user.DisplayName.Length <= 128);

    private static bool IsValidGroup(ScimGroupData group) =>
        !string.IsNullOrWhiteSpace(group.ExternalId)
        && group.ExternalId.Length <= 256
        && !string.IsNullOrWhiteSpace(group.DisplayName)
        && group.DisplayName.Length <= 256
        && group.MemberIds.Count <= 10_000;

    private static ScimUserResource ToUserResource(GitCandyEnterpriseExternalIdentity identity) => new(
        identity.Id,
        identity.ExternalId,
        identity.UserName,
        identity.Email,
        identity.DisplayName,
        identity.IsActive,
        ToDateTimeOffset(identity.FirstSeenAtUtc),
        ToDateTimeOffset(identity.LastSeenAtUtc));

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static ScimWriteResult<T> Failed<T>(string errorCode) => new(false, false, default, errorCode);
}
