using System.Collections.Concurrent;
using GitCandy.Git;
using GitCandy.Remotes;
using Microsoft.Extensions.Options;

namespace GitCandy.Web.Remotes;

/// <summary>并发受限地消费 EF mirror job 租约，并在停止时把未完成工作释放给重启恢复。</summary>
public sealed class RemoteMirrorJobDispatcher(
    IRemoteMirrorJobQueue queue,
    IRemoteMirrorService mirrorService,
    IOptions<RemoteProviderOptions> options,
    ILogger<RemoteMirrorJobDispatcher> logger) : IRemoteMirrorJobDispatcher, IDisposable
{
    private readonly IRemoteMirrorJobQueue _queue = queue;
    private readonly IRemoteMirrorService _mirrorService = mirrorService;
    private readonly RemoteMirrorJobOptions _options = options.Value.Jobs;
    private readonly ILogger<RemoteMirrorJobDispatcher> _logger = logger;
    private readonly string _leaseOwner = CreateLeaseOwner();
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _active = new();
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);

    public async Task<IReadOnlyList<RemoteMirrorOperationResult>> RunReadyAsync(
        CancellationToken cancellationToken = default)
    {
        await _dispatchGate.WaitAsync(cancellationToken);
        try
        {
            var limit = Math.Min(_options.MaxConcurrentJobs, _options.DispatchBatchSize);
            var leases = await _queue.AcquireAsync(
                _leaseOwner,
                limit,
                _options.LeaseDuration,
                cancellationToken);
            if (leases.Count == 0)
            {
                return [];
            }

            return await Task.WhenAll(leases.Select(lease => ExecuteAsync(lease, cancellationToken)));
        }
        finally
        {
            _dispatchGate.Release();
        }
    }

    public bool CancelActive(long jobId) =>
        _active.TryGetValue(jobId, out var cancellation) && TryCancel(cancellation);

    public void Dispose()
    {
        foreach (var cancellation in _active.Values)
        {
            cancellation.Dispose();
        }
        _dispatchGate.Dispose();
    }

    private async Task<RemoteMirrorOperationResult> ExecuteAsync(
        RemoteMirrorJobLease lease,
        CancellationToken stoppingToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (!_active.TryAdd(lease.JobId, cancellation))
        {
            await _queue.ReleaseAsync(lease.JobId, _leaseOwner, CancellationToken.None);
            return Failure(lease.MirrorId, RemoteMirrorErrorCodes.LeaseExpired);
        }

        try
        {
            var result = await _mirrorService.SynchronizeAsync(lease.MirrorId, cancellation.Token);
            if (result.Succeeded && result.Status == RemoteMirrorStatus.Pending)
            {
                await _queue.EnqueueAsync(
                    lease.MirrorId,
                    RemoteMirrorJobTrigger.Push,
                    cancellationToken: cancellation.Token);
            }

            var retry = !result.Succeeded
                && lease.AttemptCount < _options.MaxAttempts
                && IsRetryable(result.ErrorCode);
            await _queue.CompleteAsync(
                lease.JobId,
                _leaseOwner,
                new RemoteMirrorJobCompletion(
                    lease.RequestedGeneration,
                    result.Succeeded,
                    retry,
                    false,
                    result.ErrorCode,
                    retry ? GetRetryDelay(lease.AttemptCount) : TimeSpan.Zero),
                CancellationToken.None);
            return result;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await _queue.ReleaseAsync(lease.JobId, _leaseOwner, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException)
        {
            await _queue.CompleteAsync(
                lease.JobId,
                _leaseOwner,
                new RemoteMirrorJobCompletion(
                    lease.RequestedGeneration,
                    false,
                    false,
                    true,
                    RemoteMirrorErrorCodes.Canceled,
                    TimeSpan.Zero),
                CancellationToken.None);
            return Failure(lease.MirrorId, RemoteMirrorErrorCodes.Canceled, RemoteMirrorStatus.Pending);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Remote mirror job {JobId} failed outside the sync boundary.", lease.JobId);
            var retry = lease.AttemptCount < _options.MaxAttempts;
            await _queue.CompleteAsync(
                lease.JobId,
                _leaseOwner,
                new RemoteMirrorJobCompletion(
                    lease.RequestedGeneration,
                    false,
                    retry,
                    false,
                    RemoteRepositorySyncErrorCodes.ProcessFailed,
                    retry ? GetRetryDelay(lease.AttemptCount) : TimeSpan.Zero),
                CancellationToken.None);
            return Failure(lease.MirrorId, RemoteRepositorySyncErrorCodes.ProcessFailed);
        }
        finally
        {
            _active.TryRemove(lease.JobId, out _);
        }
    }

    private TimeSpan GetRetryDelay(int attemptCount)
    {
        var exponent = Math.Min(Math.Max(attemptCount - 1, 0), 20);
        var baseMilliseconds = Math.Min(
            _options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, exponent),
            _options.MaximumRetryDelay.TotalMilliseconds);
        var jitter = baseMilliseconds * _options.RetryJitterRatio * Random.Shared.NextDouble();
        return TimeSpan.FromMilliseconds(Math.Min(
            baseMilliseconds + jitter,
            _options.MaximumRetryDelay.TotalMilliseconds));
    }

    private static bool IsRetryable(string? errorCode) => errorCode is
        RemoteRepositorySyncErrorCodes.NetworkFailed
        or RemoteRepositorySyncErrorCodes.TimedOut
        or RemoteRepositorySyncErrorCodes.ProcessStartFailed
        or RemoteRepositorySyncErrorCodes.ProcessFailed
        or "network_error"
        or "timeout"
        or "rate_limited"
        or "remote_unavailable"
        or "provider_error";

    private static string CreateLeaseOwner()
    {
        var machine = Environment.MachineName;
        if (machine.Length > 48)
        {
            machine = machine[..48];
        }
        return $"{machine}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    private static bool TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static RemoteMirrorOperationResult Failure(
        long mirrorId,
        string errorCode,
        RemoteMirrorStatus status = RemoteMirrorStatus.Failed) =>
        new(mirrorId, false, status, errorCode);
}
