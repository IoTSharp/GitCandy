using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Enterprise;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class EnterpriseEventReceiptService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    TimeProvider timeProvider) : IEnterpriseEventReceiptService
{
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<bool> TryRecordAsync(
        long connectionId,
        string eventId,
        string payloadHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)
            || eventId.Length > 256
            || payloadHash.Length != 64
            || !payloadHash.All(Uri.IsHexDigit))
        {
            return false;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await dbContext.EnterpriseConnections.AsNoTracking().AnyAsync(
                connection => connection.Id == connectionId && connection.IsEnabled,
                cancellationToken)
            || await dbContext.EnterpriseProviderEvents.AsNoTracking().AnyAsync(
                item => item.ConnectionId == connectionId && item.EventId == eventId,
                cancellationToken))
        {
            return false;
        }

        dbContext.EnterpriseProviderEvents.Add(new GitCandyEnterpriseProviderEvent
        {
            ConnectionId = connectionId,
            EventId = eventId,
            PayloadHash = payloadHash.ToUpperInvariant(),
            ReceivedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}
