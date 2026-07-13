using System.Security.Cryptography;
using System.Text;
using GitCandy.Credentials;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class PersonalAccessTokenService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    TimeProvider timeProvider) : IPersonalAccessTokenService
{
    private const string TokenPrefix = "gcpat_";
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<IReadOnlyList<PersonalAccessTokenSummary>> GetForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tokens = await dbContext.PersonalAccessTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId)
            .OrderByDescending(token => token.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return tokens.Select(ToSummary).ToArray();
    }

    public async Task<CreatedPersonalAccessToken?> CreateAsync(
        string userId,
        string name,
        IEnumerable<string> scopes,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(scopes);

        var normalizedName = name.Trim();
        var normalizedScopes = NormalizeScopes(scopes);
        var now = _timeProvider.GetUtcNow();
        if (normalizedName.Length > SchemaLimits.CredentialName
            || normalizedScopes.Count == 0
            || expiresAt <= now)
        {
            return null;
        }

        var secret = TokenPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var token = new GitCandyPersonalAccessToken
        {
            UserId = userId,
            Name = normalizedName,
            TokenHash = HashToken(secret),
            TokenPrefix = secret[..Math.Min(secret.Length, 14)],
            Scopes = string.Join(' ', normalizedScopes),
            CreatedAtUtc = now.UtcDateTime,
            ExpiresAtUtc = expiresAt?.UtcDateTime
        };

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return null;
        }

        dbContext.PersonalAccessTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);
        AddAudit(dbContext, token.Id, userId, "create", "success", token.Scopes, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CreatedPersonalAccessToken(ToSummary(token), secret);
    }

    public async Task<bool> RevokeAsync(
        string userId,
        long tokenId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var token = await dbContext.PersonalAccessTokens.SingleOrDefaultAsync(
            item => item.Id == tokenId && item.UserId == userId,
            cancellationToken);
        if (token is null || token.RevokedAtUtc is not null)
        {
            return false;
        }

        token.RevokedAtUtc = now.UtcDateTime;
        AddAudit(dbContext, token.Id, userId, "revoke", "success", string.Empty, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PersonalAccessTokenPrincipal?> AuthenticateAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)
            || !token.StartsWith(TokenPrefix, StringComparison.Ordinal)
            || token.Length > 128)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var tokenHash = HashToken(token);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var stored = await dbContext.PersonalAccessTokens
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (stored?.User?.UserName is not string userName
            || stored.RevokedAtUtc is not null
            || stored.ExpiresAtUtc is DateTime expiresAtUtc && expiresAtUtc <= now.UtcDateTime)
        {
            return null;
        }

        var normalizedAdministratorRole = RoleNames.Administrator.ToUpperInvariant();
        var isAdministrator = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == stored.UserId && role.NormalizedName == normalizedAdministratorRole
            select userRole)
            .AnyAsync(cancellationToken);

        stored.LastUsedAtUtc = now.UtcDateTime;
        AddAudit(dbContext, stored.Id, stored.UserId, "authenticate", "success", string.Empty, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new PersonalAccessTokenPrincipal(
            stored.Id,
            stored.UserId,
            userName,
            isAdministrator,
            ParseScopes(stored.Scopes));
    }

    private static IReadOnlyList<string> NormalizeScopes(IEnumerable<string> scopes)
    {
        var normalized = scopes
            .Where(static scope => !string.IsNullOrWhiteSpace(scope))
            .Select(static scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (normalized.Any(scope => !PersonalAccessTokenScopes.Supported.Contains(scope)))
        {
            return [];
        }

        if (normalized.Contains(PersonalAccessTokenScopes.ApiWrite, StringComparer.Ordinal)
            && !normalized.Contains(PersonalAccessTokenScopes.ApiRead, StringComparer.Ordinal))
        {
            normalized.Add(PersonalAccessTokenScopes.ApiRead);
        }
        if (normalized.Contains(PersonalAccessTokenScopes.GitWrite, StringComparer.Ordinal)
            && !normalized.Contains(PersonalAccessTokenScopes.GitRead, StringComparer.Ordinal))
        {
            normalized.Add(PersonalAccessTokenScopes.GitRead);
        }

        normalized.Sort(StringComparer.Ordinal);
        return normalized;
    }

    private static IReadOnlySet<string> ParseScopes(string scopes)
    {
        return scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    private static PersonalAccessTokenSummary ToSummary(GitCandyPersonalAccessToken token)
    {
        return new PersonalAccessTokenSummary(
            token.Id,
            token.Name,
            token.TokenPrefix,
            ParseScopes(token.Scopes).Order(StringComparer.Ordinal).ToArray(),
            new DateTimeOffset(DateTime.SpecifyKind(token.CreatedAtUtc, DateTimeKind.Utc)),
            ToDateTimeOffset(token.ExpiresAtUtc),
            ToDateTimeOffset(token.LastUsedAtUtc),
            ToDateTimeOffset(token.RevokedAtUtc));
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        return value is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static void AddAudit(
        GitCandyDbContext dbContext,
        long credentialId,
        string actorUserId,
        string action,
        string outcome,
        string detail,
        DateTimeOffset occurredAt)
    {
        dbContext.CredentialAuditEvents.Add(new GitCandyCredentialAuditEvent
        {
            CredentialKind = CredentialClaimTypes.PersonalAccessToken,
            CredentialId = credentialId,
            ActorUserId = actorUserId,
            Action = action,
            Outcome = outcome,
            Detail = detail,
            OccurredAtUtc = occurredAt.UtcDateTime
        });
    }
}
