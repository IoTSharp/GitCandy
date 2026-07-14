using System.Text.Json;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Enterprise;
using GitCandy.Teams;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class EnterpriseConnectionService(
    GitCandyDbContext dbContext,
    ITeamAuthorizationService teamAuthorizationService,
    IEnterpriseSecretResolver secretResolver,
    IEnumerable<IEnterpriseProvider> providers,
    TimeProvider timeProvider) : IEnterpriseConnectionService
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly ITeamAuthorizationService _teamAuthorizationService = teamAuthorizationService;
    private readonly IEnterpriseSecretResolver _secretResolver = secretResolver;
    private readonly IReadOnlyDictionary<EnterpriseProviderKind, IEnterpriseProvider> _providers = providers
        .ToDictionary(provider => provider.Kind);
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<IReadOnlyList<EnterpriseConnectionSummary>?> GetForTeamAsync(
        string teamName,
        string? actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccessAsync(
                teamName,
                actorUserId,
                actorIsSystemAdministrator,
                TeamPermission.ViewEnterpriseConnections,
                cancellationToken))
        {
            return null;
        }

        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        var connections = await (
                from connection in _dbContext.EnterpriseConnections.AsNoTracking()
                join team in _dbContext.Teams.AsNoTracking() on connection.TeamId equals team.Id
                where team.NormalizedName == normalizedName
                orderby connection.NormalizedName
                select new { Connection = connection, TeamName = team.Name })
            .ToArrayAsync(cancellationToken);
        return connections
            .Select(item => ToSummary(item.Connection, item.TeamName))
            .ToArray();
    }

    public async Task<EnterpriseConnectionSummary?> GetAsync(
        string teamName,
        long connectionId,
        string? actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccessAsync(
                teamName,
                actorUserId,
                actorIsSystemAdministrator,
                TeamPermission.ViewEnterpriseConnections,
                cancellationToken))
        {
            return null;
        }

        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        var result = await (
                from connection in _dbContext.EnterpriseConnections.AsNoTracking()
                join team in _dbContext.Teams.AsNoTracking() on connection.TeamId equals team.Id
                where connection.Id == connectionId && team.NormalizedName == normalizedName
                select new { Connection = connection, TeamName = team.Name })
            .SingleOrDefaultAsync(cancellationToken);
        return result is null ? null : ToSummary(result.Connection, result.TeamName);
    }

    public async Task<EnterpriseConnectionSummary?> SaveAsync(
        string teamName,
        EnterpriseConnectionEdit edit,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!await CanAccessAsync(
                teamName,
                actorUserId,
                actorIsSystemAdministrator,
                TeamPermission.ManageEnterpriseConnections,
                cancellationToken)
            || !IsValid(edit))
        {
            return null;
        }

        var normalizedTeamName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        var team = await _dbContext.Teams.SingleOrDefaultAsync(
            item => item.NormalizedName == normalizedTeamName,
            cancellationToken);
        if (team is null)
        {
            return null;
        }

        var normalizedConnectionName = edit.Name.Trim().ToUpperInvariant();
        var duplicate = await _dbContext.EnterpriseConnections.AsNoTracking().AnyAsync(
            item => item.TeamId == team.Id
                && item.Id != edit.Id
                && (item.NormalizedName == normalizedConnectionName
                    || (item.Provider == edit.Provider
                        && item.ExternalOrganizationId == edit.ExternalOrganizationId.Trim())),
            cancellationToken);
        if (duplicate)
        {
            return null;
        }

        GitCandyEnterpriseConnection connection;
        if (edit.Id is long connectionId)
        {
            var existingConnection = await _dbContext.EnterpriseConnections.SingleOrDefaultAsync(
                item => item.Id == connectionId && item.TeamId == team.Id,
                cancellationToken);
            if (existingConnection is null)
            {
                return null;
            }

            connection = existingConnection;
            if ((connection.Provider != edit.Provider
                    || !string.Equals(
                        connection.ExternalOrganizationId,
                        edit.ExternalOrganizationId.Trim(),
                        StringComparison.Ordinal))
                && await _dbContext.EnterpriseExternalIdentities.AsNoTracking()
                    .AnyAsync(item => item.ConnectionId == connection.Id, cancellationToken))
            {
                return null;
            }
        }
        else
        {
            connection = new GitCandyEnterpriseConnection
            {
                TeamId = team.Id,
                CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                Status = EnterpriseConnectionStatus.NotTested
            };
            _dbContext.EnterpriseConnections.Add(connection);
        }

        connection.Name = edit.Name.Trim();
        connection.NormalizedName = normalizedConnectionName;
        connection.Provider = edit.Provider;
        connection.ExternalOrganizationId = edit.ExternalOrganizationId.Trim();
        connection.Authority = NullIfWhiteSpace(edit.Authority);
        connection.ClientId = NullIfWhiteSpace(edit.ClientId);
        connection.ApiBaseUrl = NullIfWhiteSpace(edit.ApiBaseUrl);
        connection.ConfigurationJson = NormalizeJson(edit.ConfigurationJson);
        connection.SecretReference = edit.SecretReference.Trim();
        connection.WebhookSecretReference = NullIfWhiteSpace(edit.WebhookSecretReference);
        connection.LoginEnabled = edit.LoginEnabled;
        connection.ProvisioningEnabled = edit.ProvisioningEnabled;
        connection.IsEnabled = edit.IsEnabled;
        connection.Status = edit.IsEnabled ? connection.Status : EnterpriseConnectionStatus.Disabled;
        connection.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await AddAuditAsync(
            team,
            actorUserId,
            edit.Id is null ? "enterprise.connection.create" : "enterprise.connection.update",
            connection.Name,
            $"Provider={connection.Provider}; organization={connection.ExternalOrganizationId}.",
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(connection, team.Name);
    }

    public async Task<bool> DeleteAsync(
        string teamName,
        long connectionId,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccessAsync(
                teamName,
                actorUserId,
                actorIsSystemAdministrator,
                TeamPermission.ManageEnterpriseConnections,
                cancellationToken))
        {
            return false;
        }

        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        var team = await _dbContext.Teams.SingleOrDefaultAsync(
            item => item.NormalizedName == normalizedName,
            cancellationToken);
        var connection = team is null ? null : await _dbContext.EnterpriseConnections.SingleOrDefaultAsync(
            item => item.Id == connectionId && item.TeamId == team.Id,
            cancellationToken);
        if (team is null || connection is null)
        {
            return false;
        }

        await AddAuditAsync(
            team,
            actorUserId,
            "enterprise.connection.delete",
            connection.Name,
            $"Provider={connection.Provider}; organization={connection.ExternalOrganizationId}.",
            cancellationToken);
        _dbContext.EnterpriseConnections.Remove(connection);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<EnterpriseProviderDiagnostic?> TestAsync(
        string teamName,
        long connectionId,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccessAsync(
                teamName,
                actorUserId,
                actorIsSystemAdministrator,
                TeamPermission.ManageEnterpriseConnections,
                cancellationToken))
        {
            return null;
        }

        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        var connection = await (
                from item in _dbContext.EnterpriseConnections
                join team in _dbContext.Teams on item.TeamId equals team.Id
                where item.Id == connectionId && team.NormalizedName == normalizedName
                select item)
            .SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return null;
        }

        EnterpriseProviderDiagnostic diagnostic;
        var secret = await _secretResolver.ResolveAsync(connection.SecretReference, cancellationToken);
        if (secret is null)
        {
            diagnostic = new EnterpriseProviderDiagnostic(false, "secret_unavailable", "The configured secret reference could not be resolved.");
        }
        else if (!_providers.TryGetValue(connection.Provider, out var provider))
        {
            diagnostic = new EnterpriseProviderDiagnostic(false, "provider_unavailable", "The provider adapter is not registered.");
        }
        else
        {
            try
            {
                diagnostic = await provider.TestAsync(ToContext(connection, teamName), secret, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                diagnostic = new EnterpriseProviderDiagnostic(false, "provider_error", "The provider test failed without exposing the remote response.");
            }
        }

        connection.LastTestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        connection.LastErrorCode = diagnostic.Succeeded ? null : diagnostic.Code;
        connection.Status = diagnostic.Succeeded
            ? EnterpriseConnectionStatus.Healthy
            : EnterpriseConnectionStatus.Failed;
        connection.UpdatedAtUtc = connection.LastTestedAtUtc.Value;
        var teamEntity = await _dbContext.Teams.SingleAsync(item => item.Id == connection.TeamId, cancellationToken);
        await AddAuditAsync(
            teamEntity,
            actorUserId,
            "enterprise.connection.test",
            connection.Name,
            $"Result={(diagnostic.Succeeded ? "succeeded" : "failed")}; code={diagnostic.Code}.",
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return diagnostic;
    }

    public async Task<EnterpriseConnectionContext?> GetRuntimeContextAsync(
        long connectionId,
        CancellationToken cancellationToken = default)
    {
        var result = await (
                from connection in _dbContext.EnterpriseConnections.AsNoTracking()
                join team in _dbContext.Teams.AsNoTracking() on connection.TeamId equals team.Id
                where connection.Id == connectionId
                select new { Connection = connection, TeamName = team.Name })
            .SingleOrDefaultAsync(cancellationToken);
        return result is null ? null : ToContext(result.Connection, result.TeamName);
    }

    public async Task<IReadOnlyList<EnterpriseLoginOption>> GetLoginOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await (
                from connection in _dbContext.EnterpriseConnections.AsNoTracking()
                join team in _dbContext.Teams.AsNoTracking() on connection.TeamId equals team.Id
                where connection.IsEnabled && connection.LoginEnabled
                orderby team.NormalizedName, connection.NormalizedName
                select new EnterpriseLoginOption(
                    connection.Id,
                    team.Name,
                    connection.Name,
                    connection.Provider))
            .Take(100)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EnterpriseConnectionContext>> GetProvisioningContextsAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await (
                from connection in _dbContext.EnterpriseConnections.AsNoTracking()
                join team in _dbContext.Teams.AsNoTracking() on connection.TeamId equals team.Id
                where connection.IsEnabled && connection.ProvisioningEnabled
                orderby connection.Id
                select new { Connection = connection, TeamName = team.Name })
            .ToArrayAsync(cancellationToken);
        return records.Select(item => ToContext(item.Connection, item.TeamName)).ToArray();
    }

    public async Task UpdateSyncStateAsync(
        long connectionId,
        string? cursor,
        EnterpriseConnectionStatus status,
        string? errorCode,
        bool completed,
        CancellationToken cancellationToken = default)
    {
        var connection = await _dbContext.EnterpriseConnections.SingleOrDefaultAsync(
            item => item.Id == connectionId,
            cancellationToken);
        if (connection is null) return;
        connection.SyncCursor = cursor;
        connection.Status = status;
        connection.LastErrorCode = errorCode;
        connection.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (completed)
        {
            connection.LastSynchronizedAtUtc = connection.UpdatedAtUtc;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> CanAccessAsync(
        string teamName,
        string? actorUserId,
        bool actorIsSystemAdministrator,
        TeamPermission permission,
        CancellationToken cancellationToken) => _teamAuthorizationService.IsAllowedAsync(
            teamName,
            actorUserId,
            actorIsSystemAdministrator,
            permission,
            cancellationToken);

    private async Task AddAuditAsync(
        GitCandyTeam team,
        string actorUserId,
        string action,
        string subject,
        string detail,
        CancellationToken cancellationToken)
    {
        var actorName = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == actorUserId)
            .Select(user => user.UserName)
            .SingleOrDefaultAsync(cancellationToken);
        _dbContext.TeamAuditEvents.Add(new GitCandyTeamAuditEvent
        {
            TeamId = team.Id,
            TeamName = team.Name,
            ActorUserId = actorUserId,
            ActorName = actorName ?? "system-administrator",
            Action = action,
            Outcome = "succeeded",
            Subject = subject,
            Detail = detail,
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        });
    }

    private static bool IsValid(EnterpriseConnectionEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.Name)
            || edit.Name.Trim().Length > SchemaLimits.EnterpriseConnectionName
            || string.IsNullOrWhiteSpace(edit.ExternalOrganizationId)
            || edit.ExternalOrganizationId.Trim().Length > SchemaLimits.EnterpriseExternalId
            || string.IsNullOrWhiteSpace(edit.SecretReference)
            || edit.SecretReference.Trim().Length > SchemaLimits.SecretReference
            || !edit.SecretReference.Contains(':', StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(edit.WebhookSecretReference)
                && (!edit.WebhookSecretReference.Contains(':', StringComparison.Ordinal)
                    || edit.WebhookSecretReference.Length > SchemaLimits.SecretReference))
            || !IsOptionalHttpsUrl(edit.Authority)
            || !IsOptionalHttpsUrl(edit.ApiBaseUrl))
        {
            return false;
        }

        try
        {
            _ = NormalizeJson(edit.ConfigurationJson);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string? NormalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        if (json.Length > SchemaLimits.EnterpriseConfiguration)
        {
            throw new InvalidOperationException("Enterprise configuration is too large.");
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || ContainsSecretProperty(document.RootElement))
        {
            throw new InvalidOperationException("Enterprise configuration must be an object without secret fields.");
        }

        return JsonSerializer.Serialize(document.RootElement);
    }

    private static bool ContainsSecretProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var normalizedName = property.Name.Replace("_", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal)
                    .ToUpperInvariant();
                if (normalizedName.Contains("SECRET", StringComparison.Ordinal)
                    || normalizedName.Contains("PASSWORD", StringComparison.Ordinal)
                    || normalizedName.Contains("ACCESSTOKEN", StringComparison.Ordinal)
                    || normalizedName.Contains("REFRESHTOKEN", StringComparison.Ordinal)
                    || normalizedName.Contains("PRIVATEKEY", StringComparison.Ordinal))
                {
                    return true;
                }

                if (ContainsSecretProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(ContainsSecretProperty);
        }

        return false;
    }

    private static bool IsOptionalHttpsUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && uri.IsLoopback));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static EnterpriseConnectionSummary ToSummary(
        GitCandyEnterpriseConnection connection,
        string teamName) => new(
            connection.Id,
            teamName,
            connection.Name,
            connection.Provider,
            connection.ExternalOrganizationId,
            connection.Authority,
            connection.ClientId,
            connection.ApiBaseUrl,
            connection.ConfigurationJson,
            connection.SecretReference,
            connection.WebhookSecretReference,
            connection.LoginEnabled,
            connection.ProvisioningEnabled,
            connection.IsEnabled,
            connection.Status,
            connection.LastErrorCode,
            ToDateTimeOffset(connection.LastTestedAtUtc),
            ToDateTimeOffset(connection.LastSynchronizedAtUtc),
            ToDateTimeOffset(connection.CreatedAtUtc)!.Value,
            ToDateTimeOffset(connection.UpdatedAtUtc)!.Value);

    private static EnterpriseConnectionContext ToContext(
        GitCandyEnterpriseConnection connection,
        string teamName) => new(
            connection.Id,
            connection.TeamId,
            teamName,
            connection.Name,
            connection.Provider,
            connection.ExternalOrganizationId,
            connection.Authority,
            connection.ClientId,
            connection.ApiBaseUrl,
            connection.ConfigurationJson,
            connection.SecretReference,
            connection.WebhookSecretReference,
            connection.SyncCursor,
            connection.LoginEnabled,
            connection.ProvisioningEnabled,
            connection.IsEnabled);

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
