using GitCandy.Remotes;
using GitCandy.Web.Remotes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class RemoteMirrorJobDispatcherTests
{
    [TestMethod]
    public async Task RunReadyAsync_WithConcurrentWakeups_RespectsInstanceConcurrencyLimit()
    {
        var queue = new RecordingQueue(4);
        var mirrorService = new ConcurrencyRecordingMirrorService();
        using var dispatcher = new RemoteMirrorJobDispatcher(
            queue,
            mirrorService,
            Options.Create(new RemoteProviderOptions
            {
                Jobs = new RemoteMirrorJobOptions
                {
                    MaxConcurrentJobs = 2,
                    DispatchBatchSize = 10,
                    MaxAttempts = 3,
                    LeaseDuration = TimeSpan.FromMinutes(10),
                    RetryJitterRatio = 0
                }
            }),
            NullLogger<RemoteMirrorJobDispatcher>.Instance);

        var first = dispatcher.RunReadyAsync();
        var second = dispatcher.RunReadyAsync();
        var results = (await Task.WhenAll(first, second)).SelectMany(static item => item).ToArray();

        Assert.AreEqual(4, results.Length);
        Assert.AreEqual(2, mirrorService.MaximumActive);
        Assert.AreEqual(4, queue.CompletedCount);
    }

    private sealed class ConcurrencyRecordingMirrorService : IRemoteMirrorService
    {
        private int _active;
        private int _maximumActive;

        public int MaximumActive => _maximumActive;

        public Task<RemoteMirrorOperationResult> RegisterAsync(
            string actorUserId,
            RemoteMirrorRegistration registration,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<RemoteMirrorOperationResult> SynchronizeAsync(
            long mirrorId,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            try
            {
                await Task.Delay(50, cancellationToken);
                return new RemoteMirrorOperationResult(mirrorId, true, RemoteMirrorStatus.Succeeded);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task<bool> UpdateRemoteProfileAsync(
            long mirrorId,
            RemoteRepositoryProfile remoteRepository,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumActive);
                if (value <= current || Interlocked.CompareExchange(ref _maximumActive, value, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class RecordingQueue(int count) : IRemoteMirrorJobQueue
    {
        private readonly Queue<RemoteMirrorJobLease> _leases = new(
            Enumerable.Range(1, count).Select(index => new RemoteMirrorJobLease(
                index,
                index,
                1,
                1,
                RemoteMirrorJobTrigger.Manual,
                DateTimeOffset.UtcNow.AddMinutes(10))));
        private readonly object _gate = new();
        private int _completedCount;

        public int CompletedCount => _completedCount;

        public Task EnqueueAsync(
            long mirrorId,
            RemoteMirrorJobTrigger trigger,
            DateTimeOffset? availableAt = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> EnqueueDuePullMirrorsAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> EnqueueRecoveryCandidatesAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<RemoteMirrorJobLease>> AcquireAsync(
            string leaseOwner,
            int limit,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            var leases = new List<RemoteMirrorJobLease>();
            lock (_gate)
            {
                while (leases.Count < limit && _leases.TryDequeue(out var lease))
                {
                    leases.Add(lease);
                }
            }
            return Task.FromResult<IReadOnlyList<RemoteMirrorJobLease>>(leases);
        }

        public Task CompleteAsync(
            long jobId,
            string leaseOwner,
            RemoteMirrorJobCompletion completion,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _completedCount);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(
            long jobId,
            string leaseOwner,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> RequestCancellationAsync(
            long jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> RetryAsync(
            long jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<RemoteMirrorJobSummary>> GetForRepositoryAsync(
            long repositoryId,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteMirrorJobSummary>>([]);
    }
}
