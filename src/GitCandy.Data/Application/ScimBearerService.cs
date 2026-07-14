using System.Security.Cryptography;
using System.Text;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Enterprise;
using GitCandy.Teams;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class ScimBearerService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    TimeProvider timeProvider) : IScimBearerService
{
    private const int PrefixLength = 12;
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<CreatedScimBearer?> RotateAsync(
        long connectionId,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await dbContext.EnterpriseConnections
            .Include(item => item.Team)
            .SingleOrDefaultAsync(item => item.Id == connectionId, cancellationToken);
        if (connection?.Team is null
            || !connection.ProvisioningEnabled
            || connection.Provider is not (EnterpriseProviderKind.Scim or EnterpriseProviderKind.MicrosoftEntraId))
        {
            return null;
        }

        if (!actorIsSystemAdministrator)
        {
            var actorRole = await dbContext.UserTeamRoles.AsNoTracking()
                .Where(role => role.TeamId == connection.TeamId && role.UserId == actorUserId)
                .Select(role => (TeamRole?)role.Role)
                .SingleOrDefaultAsync(cancellationToken);
            if (actorRole is null
                || !TeamRolePermissions.Allows(actorRole.Value, TeamPermission.ManageEnterpriseConnections))
            {
                return null;
            }
        }

        var token = $"gc_scim_{Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";
        var prefix = token[..PrefixLength];
        var credential = await dbContext.EnterpriseScimCredentials.SingleOrDefaultAsync(
            item => item.ConnectionId == connectionId,
            cancellationToken);
        if (credential is null)
        {
            credential = new GitCandyEnterpriseScimCredential { ConnectionId = connectionId };
            dbContext.EnterpriseScimCredentials.Add(credential);
        }

        var now = _timeProvider.GetUtcNow();
        credential.Prefix = prefix;
        credential.TokenHash = Hash(token);
        credential.CreatedAtUtc = now.UtcDateTime;
        var actorName = await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == actorUserId)
            .Select(user => user.UserName)
            .SingleOrDefaultAsync(cancellationToken);
        dbContext.TeamAuditEvents.Add(new GitCandyTeamAuditEvent
        {
            TeamId = connection.TeamId,
            TeamName = connection.Team.Name,
            ActorUserId = actorUserId,
            ActorName = actorName ?? "system-administrator",
            Action = "enterprise.scim.rotate",
            Outcome = "succeeded",
            Subject = connection.Name,
            Detail = $"SCIM bearer rotated; prefix={prefix}.",
            OccurredAtUtc = now.UtcDateTime
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CreatedScimBearer(connectionId, connection.Team.Name, token, prefix, now);
    }

    public async Task<long?> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length <= PrefixLength)
        {
            return null;
        }

        var prefix = token[..PrefixLength];
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var credential = await (
                from item in dbContext.EnterpriseScimCredentials.AsNoTracking()
                join connection in dbContext.EnterpriseConnections.AsNoTracking()
                    on item.ConnectionId equals connection.Id
                where item.Prefix == prefix
                    && connection.IsEnabled
                    && connection.ProvisioningEnabled
                select item)
            .SingleOrDefaultAsync(cancellationToken);
        if (credential is null)
        {
            return null;
        }

        var suppliedHash = Encoding.ASCII.GetBytes(Hash(token));
        var storedHash = Encoding.ASCII.GetBytes(credential.TokenHash);
        return suppliedHash.Length == storedHash.Length
            && CryptographicOperations.FixedTimeEquals(suppliedHash, storedHash)
                ? credential.ConnectionId
                : null;
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
