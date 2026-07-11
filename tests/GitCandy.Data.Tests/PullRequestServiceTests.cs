using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.Sqlite;
using GitCandy.Issues;
using GitCandy.PullRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class PullRequestServiceTests
{
    [TestMethod]
    public async Task CreatePullRequest_WithSharedNumberAndDraft_PersistsSnapshotsTimelineAndHeadRef()
    {
        await using var fixture = await PullRequestFixture.CreateAsync();
        var issueService = fixture.Services.GetRequiredService<IIssueService>();
        var pullRequestService = fixture.Services.GetRequiredService<IPullRequestService>();
        var issue = await issueService.CreateIssueAsync(
            fixture.RepositoryId,
            new CreateIssueCommand("Existing issue", string.Empty, fixture.AuthorId));

        var pullRequest = await pullRequestService.CreatePullRequestAsync(
            fixture.RepositoryId,
            new CreatePullRequestCommand(
                "Add feature",
                "```csharp\nvar safe = true;\n```\n<script>alert(1)</script>",
                fixture.AuthorId,
                "feature",
                "main",
                IsDraft: true));

        Assert.AreEqual(1L, issue.Number);
        Assert.AreEqual(2L, pullRequest.Number, "Issues and Pull Requests must share repository work-item numbers.");
        Assert.IsTrue(pullRequest.IsDraft);
        Assert.AreEqual(FakePullRequestGitRepository.MainSha, pullRequest.OriginalBaseSha);
        Assert.AreEqual(FakePullRequestGitRepository.FeatureSha, pullRequest.OriginalHeadSha);
        Assert.AreEqual(FakePullRequestGitRepository.FeatureSha, fixture.Git.HeadReferences[pullRequest.Number]);
        Assert.IsTrue(pullRequest.Timeline.Any(item => item.Type == PullRequestEventType.Created));
        StringAssert.Contains(pullRequest.BodyHtml, "language-csharp");
        Assert.IsFalse(pullRequest.BodyHtml.Contains("<script", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task PullRequestState_WithDuplicatePair_RejectsDuplicateAndConflictingReopen()
    {
        await using var fixture = await PullRequestFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IPullRequestService>();
        var first = await service.CreatePullRequestAsync(
            fixture.RepositoryId,
            NewCommand(fixture.AuthorId, "feature"));

        var duplicate = await Assert.ThrowsExactlyAsync<PullRequestValidationException>(() =>
            service.CreatePullRequestAsync(
                fixture.RepositoryId,
                NewCommand(fixture.AuthorId, "feature")));
        Assert.AreEqual(PullRequestMutationResult.Duplicate, duplicate.Result);

        Assert.AreEqual(
            PullRequestMutationResult.Succeeded,
            await service.SetDraftAsync(
                fixture.RepositoryId,
                first.Number,
                fixture.AuthorId,
                isOwner: true,
                isDraft: false));
        Assert.AreEqual(
            PullRequestMutationResult.Succeeded,
            await service.SetStateAsync(
                fixture.RepositoryId,
                first.Number,
                fixture.AuthorId,
                isOwner: true,
                PullRequestState.Closed));
        var replacement = await service.CreatePullRequestAsync(
            fixture.RepositoryId,
            NewCommand(fixture.AuthorId, "feature"));
        Assert.AreEqual(2L, replacement.Number);
        Assert.AreEqual(
            PullRequestMutationResult.Duplicate,
            await service.SetStateAsync(
                fixture.RepositoryId,
                first.Number,
                fixture.AuthorId,
                isOwner: true,
                PullRequestState.Open));
    }

    [TestMethod]
    public async Task CreatePullRequest_WithInvalidBranchesOrRevokedWrite_ReturnsExplicitFailure()
    {
        await using var fixture = await PullRequestFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IPullRequestService>();

        var sameBranch = await Assert.ThrowsExactlyAsync<PullRequestValidationException>(() =>
            service.CreatePullRequestAsync(
                fixture.RepositoryId,
                NewCommand(fixture.AuthorId, "main")));
        Assert.AreEqual(PullRequestMutationResult.Invalid, sameBranch.Result);

        var noChanges = await Assert.ThrowsExactlyAsync<PullRequestValidationException>(() =>
            service.CreatePullRequestAsync(
                fixture.RepositoryId,
                NewCommand(fixture.AuthorId, "empty")));
        Assert.AreEqual(PullRequestMutationResult.NoChanges, noChanges.Result);

        await fixture.RevokeWriteAsync();
        var forbidden = await Assert.ThrowsExactlyAsync<PullRequestValidationException>(() =>
            service.CreatePullRequestAsync(
                fixture.RepositoryId,
                NewCommand(fixture.AuthorId, "feature")));
        Assert.AreEqual(PullRequestMutationResult.Forbidden, forbidden.Result);
    }

    [TestMethod]
    public async Task GetPullRequestChanges_WithStoredSnapshot_ReturnsPagedMergeBaseDiff()
    {
        await using var fixture = await PullRequestFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IPullRequestService>();
        var pullRequest = await service.CreatePullRequestAsync(
            fixture.RepositoryId,
            NewCommand(fixture.AuthorId, "feature"));

        var changes = await service.GetPullRequestChangesAsync(
            fixture.RepositoryId,
            pullRequest.Number,
            commitPage: 1,
            commitPageSize: 20,
            includeFiles: true);

        Assert.IsNotNull(changes);
        Assert.AreEqual(FakePullRequestGitRepository.MainSha, changes.BaseSha);
        Assert.AreEqual(FakePullRequestGitRepository.FeatureSha, changes.HeadSha);
        Assert.AreEqual("reviews", fixture.Git.LastStorageName);
        Assert.HasCount(1, changes.Commits);
        Assert.HasCount(1, changes.Files);
    }

    [TestMethod]
    public async Task ReviewThread_WithReplyAndResolve_PersistsSanitizedConversation()
    {
        await using var fixture = await PullRequestFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IPullRequestService>();
        var pullRequest = await service.CreatePullRequestAsync(fixture.RepositoryId, NewCommand(fixture.AuthorId, "feature"));

        Assert.AreEqual(PullRequestMutationResult.Succeeded, await service.AddReviewThreadAsync(
            fixture.RepositoryId, pullRequest.Number, fixture.AuthorId,
            new CreatePullRequestReviewThreadCommand("README.md", PullRequestDiffSide.New, 2, 2, "Check this <script>alert(1)</script>")));
        var thread = (await service.GetReviewThreadsAsync(fixture.RepositoryId, pullRequest.Number)).Single();
        Assert.IsFalse(thread.IsOutdated);
        Assert.IsFalse(thread.Comments[0].BodyHtml.Contains("<script", StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(PullRequestMutationResult.Succeeded, await service.AddReviewReplyAsync(
            fixture.RepositoryId, pullRequest.Number, thread.Id, fixture.AuthorId, "Updated."));
        Assert.AreEqual(PullRequestMutationResult.Succeeded, await service.SetReviewThreadResolvedAsync(
            fixture.RepositoryId, pullRequest.Number, thread.Id, fixture.AuthorId, isOwner: false, resolved: true));
        thread = (await service.GetReviewThreadsAsync(fixture.RepositoryId, pullRequest.Number)).Single();
        Assert.HasCount(2, thread.Comments);
        Assert.IsTrue(thread.IsResolved);
    }

    [TestMethod]
    public async Task RefreshPullRequest_WithUnmatchedContext_MarksThreadOutdatedAndUpdatesHeadRef()
    {
        await using var fixture = await PullRequestFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IPullRequestService>();
        var pullRequest = await service.CreatePullRequestAsync(fixture.RepositoryId, NewCommand(fixture.AuthorId, "feature"));
        await service.AddReviewThreadAsync(
            fixture.RepositoryId, pullRequest.Number, fixture.AuthorId,
            new CreatePullRequestReviewThreadCommand("README.md", PullRequestDiffSide.New, 2, 2, "Check this."));

        fixture.Git.FeatureHeadSha = FakePullRequestGitRepository.UpdatedFeatureSha;
        fixture.Git.CanRemap = false;
        Assert.AreEqual(PullRequestMutationResult.Succeeded, await service.RefreshPullRequestAsync(fixture.RepositoryId, pullRequest.Number));

        var thread = (await service.GetReviewThreadsAsync(fixture.RepositoryId, pullRequest.Number)).Single();
        Assert.IsTrue(thread.IsOutdated);
        Assert.IsNull(thread.CurrentPath);
        Assert.AreEqual(FakePullRequestGitRepository.UpdatedFeatureSha, fixture.Git.HeadReferences[pullRequest.Number]);
    }

    [TestMethod]
    public async Task ReviewStatus_WithSeparateAssigneeAndReviewer_PersistsLifecycleAndStaleApproval()
    {
        await using var fixture = await PullRequestFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IPullRequestService>();
        var pullRequest = await service.CreatePullRequestAsync(
            fixture.RepositoryId,
            NewCommand(fixture.AuthorId, "feature"));

        Assert.AreEqual(
            PullRequestMutationResult.Succeeded,
            await service.SetAssigneeAsync(
                fixture.RepositoryId,
                pullRequest.Number,
                fixture.AuthorId,
                isOwner: false,
                fixture.ReviewerId));
        Assert.AreEqual(
            PullRequestMutationResult.Forbidden,
            await service.SubmitReviewAsync(
                fixture.RepositoryId,
                pullRequest.Number,
                fixture.ReviewerId,
                new SubmitPullRequestReviewCommand(PullRequestReviewState.Commented, "Not requested yet.")));
        Assert.AreEqual(
            PullRequestMutationResult.Succeeded,
            await service.RequestReviewAsync(
                fixture.RepositoryId,
                pullRequest.Number,
                fixture.AuthorId,
                isOwner: false,
                fixture.ReviewerId));
        Assert.AreEqual(
            PullRequestMutationResult.Succeeded,
            await service.SubmitReviewAsync(
                fixture.RepositoryId,
                pullRequest.Number,
                fixture.ReviewerId,
                new SubmitPullRequestReviewCommand(PullRequestReviewState.Approved, "Looks good. <script>alert(1)</script>")));

        var overview = await service.GetReviewOverviewAsync(fixture.RepositoryId, pullRequest.Number);
        Assert.IsNotNull(overview);
        Assert.AreEqual(fixture.ReviewerId, overview.AssigneeUserId);
        Assert.AreEqual(fixture.ReviewerId, overview.Reviewers.Single().UserId);
        Assert.IsFalse(overview.Reviewers.Single().IsReviewRequested);
        Assert.IsTrue(overview.Reviews.Single().IsEffectiveApproval);
        Assert.IsFalse(overview.Reviews.Single().BodyHtml.Contains("<script", StringComparison.OrdinalIgnoreCase));

        fixture.Git.FeatureHeadSha = FakePullRequestGitRepository.UpdatedFeatureSha;
        await service.RefreshPullRequestAsync(fixture.RepositoryId, pullRequest.Number);
        overview = await service.GetReviewOverviewAsync(fixture.RepositoryId, pullRequest.Number);
        Assert.IsNotNull(overview);
        Assert.IsTrue(overview.Reviews.Single().IsStale);
        Assert.IsFalse(overview.Reviews.Single().IsEffectiveApproval);

        Assert.AreEqual(
            PullRequestMutationResult.Succeeded,
            await service.RequestReviewAsync(
                fixture.RepositoryId,
                pullRequest.Number,
                fixture.AuthorId,
                isOwner: true,
                fixture.ReviewerId));
        overview = await service.GetReviewOverviewAsync(fixture.RepositoryId, pullRequest.Number);
        Assert.IsTrue(overview!.Reviewers.Single().IsReviewRequested);

        Assert.AreEqual(
            PullRequestMutationResult.Succeeded,
            await service.SubmitReviewAsync(
                fixture.RepositoryId,
                pullRequest.Number,
                fixture.ReviewerId,
                new SubmitPullRequestReviewCommand(PullRequestReviewState.ChangesRequested, "Please update the tests.")));
        overview = await service.GetReviewOverviewAsync(fixture.RepositoryId, pullRequest.Number);
        var latestReview = overview!.Reviews.Last();
        Assert.AreEqual(PullRequestReviewState.ChangesRequested, latestReview.State);
        Assert.IsFalse(overview.Reviewers.Single().IsReviewRequested);

        Assert.AreEqual(
            PullRequestMutationResult.Succeeded,
            await service.DismissReviewAsync(
                fixture.RepositoryId,
                pullRequest.Number,
                latestReview.Id,
                fixture.AuthorId,
                isOwner: true,
                "Superseded by a later review."));
        overview = await service.GetReviewOverviewAsync(fixture.RepositoryId, pullRequest.Number);
        Assert.AreEqual(PullRequestReviewState.Dismissed, overview!.Reviews.Last().State);
    }

    [TestMethod]
    public async Task ReviewPolicy_WithAuthorApprovalAndRetainedStaleApproval_AppliesExplicitConfiguration()
    {
        await using var defaultFixture = await PullRequestFixture.CreateAsync();
        var defaultService = defaultFixture.Services.GetRequiredService<IPullRequestService>();
        var defaultPullRequest = await defaultService.CreatePullRequestAsync(
            defaultFixture.RepositoryId,
            NewCommand(defaultFixture.AuthorId, "feature"));
        await defaultService.RequestReviewAsync(
            defaultFixture.RepositoryId,
            defaultPullRequest.Number,
            defaultFixture.AuthorId,
            isOwner: true,
            defaultFixture.AuthorId);
        Assert.AreEqual(
            PullRequestMutationResult.Forbidden,
            await defaultService.SubmitReviewAsync(
                defaultFixture.RepositoryId,
                defaultPullRequest.Number,
                defaultFixture.AuthorId,
                new SubmitPullRequestReviewCommand(PullRequestReviewState.Approved, string.Empty)));

        await using var configuredFixture = await PullRequestFixture.CreateAsync(
            allowAuthorApproval: true,
            dismissStaleApprovals: false);
        var configuredService = configuredFixture.Services.GetRequiredService<IPullRequestService>();
        var configuredPullRequest = await configuredService.CreatePullRequestAsync(
            configuredFixture.RepositoryId,
            NewCommand(configuredFixture.AuthorId, "feature"));
        await configuredService.RequestReviewAsync(
            configuredFixture.RepositoryId,
            configuredPullRequest.Number,
            configuredFixture.AuthorId,
            isOwner: true,
            configuredFixture.AuthorId);
        Assert.AreEqual(
            PullRequestMutationResult.Succeeded,
            await configuredService.SubmitReviewAsync(
                configuredFixture.RepositoryId,
                configuredPullRequest.Number,
                configuredFixture.AuthorId,
                new SubmitPullRequestReviewCommand(PullRequestReviewState.Approved, string.Empty)));
        configuredFixture.Git.FeatureHeadSha = FakePullRequestGitRepository.UpdatedFeatureSha;
        await configuredService.RefreshPullRequestAsync(configuredFixture.RepositoryId, configuredPullRequest.Number);
        var overview = await configuredService.GetReviewOverviewAsync(
            configuredFixture.RepositoryId,
            configuredPullRequest.Number);
        Assert.IsTrue(overview!.Policy.AllowAuthorApproval);
        Assert.IsFalse(overview.Policy.DismissStaleApprovals);
        Assert.IsTrue(overview.Reviews.Single().IsStale);
        Assert.IsTrue(overview.Reviews.Single().IsEffectiveApproval);
    }

    [TestMethod]
    public async Task MergePullRequest_WithApproval_UpdatesStateAndClosesReferencedIssue()
    {
        await using var fixture = await PullRequestFixture.CreateAsync();
        var issueService = fixture.Services.GetRequiredService<IIssueService>();
        var service = fixture.Services.GetRequiredService<IPullRequestService>();
        var issue = await issueService.CreateIssueAsync(
            fixture.RepositoryId,
            new CreateIssueCommand("Close after merge", string.Empty, fixture.AuthorId));
        var pullRequest = await service.CreatePullRequestAsync(
            fixture.RepositoryId,
            new CreatePullRequestCommand(
                "Approved feature",
                $"Closes #{issue.Number}",
                fixture.AuthorId,
                "feature",
                "main",
                IsDraft: false));
        await service.RequestReviewAsync(
            fixture.RepositoryId,
            pullRequest.Number,
            fixture.AuthorId,
            isOwner: true,
            fixture.ReviewerId);
        await service.SubmitReviewAsync(
            fixture.RepositoryId,
            pullRequest.Number,
            fixture.ReviewerId,
            new SubmitPullRequestReviewCommand(PullRequestReviewState.Approved, "Approved."));

        var mergeability = await service.GetMergeabilityAsync(fixture.RepositoryId, pullRequest.Number);
        Assert.IsNotNull(mergeability);
        Assert.IsTrue(mergeability.IsMergeable);
        Assert.AreEqual(1, mergeability.EffectiveApprovals);
        var current = await service.GetPullRequestAsync(fixture.RepositoryId, pullRequest.Number);
        Assert.IsNotNull(current);

        var result = await service.MergePullRequestAsync(
            fixture.RepositoryId,
            pullRequest.Number,
            new MergePullRequestCommand(
                PullRequestMergeMethod.Squash,
                fixture.AuthorId,
                "author",
                "author@example.com",
                "Squash approved feature",
                current.Version));

        Assert.AreEqual(PullRequestMutationResult.Succeeded, result.Result);
        Assert.AreEqual(1, result.ClosedIssueCount);
        current = await service.GetPullRequestAsync(fixture.RepositoryId, pullRequest.Number);
        Assert.AreEqual(PullRequestState.Merged, current!.State);
        Assert.AreEqual(result.CommitSha, current.MergeCommitSha);
        Assert.IsTrue(current.Timeline.Any(item => item.Type == PullRequestEventType.Merged));
        var closedIssue = await issueService.GetIssueAsync(
            fixture.RepositoryId,
            issue.Number,
            fixture.AuthorId);
        Assert.AreEqual(IssueState.Closed, closedIssue!.State);
        Assert.IsTrue(closedIssue.Timeline.Any(item => item.Type == IssueEventType.AutoClosed));
    }

    [TestMethod]
    public async Task CreatePullRequest_WithForkSource_UsesStableRepositoryRelationship()
    {
        await using var fixture = await PullRequestFixture.CreateAsync();
        var dbContext = fixture.Services.GetRequiredService<GitCandyDbContext>();
        var fork = new GitCandyRepository
        {
            NamespaceId = GitCandyNamespace.LegacyNamespaceId,
            Name = "reviews-fork",
            StorageName = "reviews-fork",
            Description = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            ForkedFromRepositoryId = fixture.RepositoryId,
            ForkNetworkRootRepositoryId = fixture.RepositoryId,
            ForkedFromRepository = "reviews",
            ForkNetworkRoot = "reviews"
        };
        fork.UserRoles.Add(new GitCandyUserRepositoryRole
        {
            UserId = fixture.AuthorId,
            AllowRead = true,
            AllowWrite = true,
            IsOwner = true
        });
        dbContext.Repositories.Add(fork);
        await dbContext.SaveChangesAsync();
        var service = fixture.Services.GetRequiredService<IPullRequestService>();

        var sources = await service.GetSourceRepositoriesAsync(
            fixture.RepositoryId,
            fixture.AuthorId);
        Assert.IsTrue(sources.Any(item => item.RepositoryId == fixture.RepositoryId));
        Assert.IsTrue(sources.Any(item => item.RepositoryId == fork.Id));

        var pullRequest = await service.CreatePullRequestAsync(
            fixture.RepositoryId,
            new CreatePullRequestCommand(
                "Fork feature",
                "Cross repository change",
                fixture.AuthorId,
                "feature",
                "main",
                IsDraft: true,
                SourceRepositoryId: fork.Id));

        Assert.AreEqual(fork.Id, pullRequest.SourceRepositoryId);
        Assert.AreEqual("reviews-fork", pullRequest.SourceRepository);
        Assert.AreEqual(FakePullRequestGitRepository.FeatureSha, fixture.Git.HeadReferences[pullRequest.Number]);

        dbContext.Repositories.Remove(fork);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        pullRequest = (await service.GetPullRequestAsync(fixture.RepositoryId, pullRequest.Number))!;
        Assert.IsNull(pullRequest.SourceRepositoryId);
        Assert.AreEqual("legacy", pullRequest.SourceNamespace);
        Assert.AreEqual("reviews-fork", pullRequest.SourceRepository);
    }

    [TestMethod]
    public async Task MergePullRequest_WithRejectedPreMergeHook_DoesNotWriteGitOrDatabase()
    {
        await using var fixture = await PullRequestFixture.CreateAsync(rejectMerge: true);
        var service = fixture.Services.GetRequiredService<IPullRequestService>();
        var pullRequest = await service.CreatePullRequestAsync(
            fixture.RepositoryId,
            new CreatePullRequestCommand("Policy check", string.Empty, fixture.AuthorId, "feature", "main", IsDraft: false));
        await service.RequestReviewAsync(
            fixture.RepositoryId, pullRequest.Number, fixture.AuthorId, isOwner: true, fixture.ReviewerId);
        await service.SubmitReviewAsync(
            fixture.RepositoryId,
            pullRequest.Number,
            fixture.ReviewerId,
            new SubmitPullRequestReviewCommand(PullRequestReviewState.Approved, string.Empty));
        pullRequest = (await service.GetPullRequestAsync(fixture.RepositoryId, pullRequest.Number))!;

        var result = await service.MergePullRequestAsync(
            fixture.RepositoryId,
            pullRequest.Number,
            new MergePullRequestCommand(
                PullRequestMergeMethod.MergeCommit,
                fixture.AuthorId,
                "author",
                "author@example.com",
                "Rejected merge",
                pullRequest.Version));

        Assert.AreEqual(PullRequestMutationResult.HookRejected, result.Result);
        Assert.AreEqual(0, fixture.Git.MergeCallCount);
        pullRequest = (await service.GetPullRequestAsync(fixture.RepositoryId, pullRequest.Number))!;
        Assert.AreEqual(PullRequestState.Open, pullRequest.State);
        Assert.IsNull(pullRequest.MergeCommitSha);
    }

    private static CreatePullRequestCommand NewCommand(string authorId, string sourceBranch) =>
        new("PR title", "PR body", authorId, sourceBranch, "main", IsDraft: true);

    private sealed class PullRequestFixture : IAsyncDisposable
    {
        private PullRequestFixture(
            ServiceProvider rootProvider,
            AsyncServiceScope scope,
            string databasePath,
            long repositoryId,
            string authorId,
            string reviewerId,
            FakePullRequestGitRepository git)
        {
            RootProvider = rootProvider;
            Scope = scope;
            DatabasePath = databasePath;
            RepositoryId = repositoryId;
            AuthorId = authorId;
            ReviewerId = reviewerId;
            Git = git;
        }

        private ServiceProvider RootProvider { get; }
        private AsyncServiceScope Scope { get; }
        public IServiceProvider Services => Scope.ServiceProvider;
        private string DatabasePath { get; }
        public long RepositoryId { get; }
        public string AuthorId { get; }
        public string ReviewerId { get; }
        public FakePullRequestGitRepository Git { get; }

        public static async Task<PullRequestFixture> CreateAsync(
            bool allowAuthorApproval = false,
            bool dismissStaleApprovals = true,
            bool rejectMerge = false)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                "GitCandy.Tests",
                $"pull-requests-{Guid.NewGuid():N}.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False;Default Timeout=10"
                }).Build();
            var git = new FakePullRequestGitRepository();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());
            services.AddGitCandyApplicationServices();
            services.Configure<GitCandy.Configuration.GitCandyApplicationOptions>(options =>
            {
                options.AllowAuthorApproval = allowAuthorApproval;
                options.DismissStalePullRequestApprovals = dismissStaleApprovals;
            });
            services.AddSingleton<IPullRequestGitRepository>(git);
            if (rejectMerge)
            {
                services.AddSingleton<IPullRequestMergeHook, RejectingPullRequestMergeHook>();
            }
            var provider = services.BuildServiceProvider(validateScopes: true);
            var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.MigrateAsync();
            var author = NewUser("author");
            var reviewer = NewUser("reviewer");
            dbContext.Users.AddRange(author, reviewer);
            var repository = new GitCandyRepository
            {
                NamespaceId = GitCandyNamespace.LegacyNamespaceId,
                Name = "reviews",
                StorageName = "reviews",
                Description = string.Empty,
                CreatedAtUtc = DateTime.UtcNow,
                WorkItemSequence = new GitCandyWorkItemSequence()
            };
            repository.UserRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = author.Id,
                AllowRead = true,
                AllowWrite = true,
                IsOwner = true
            });
            repository.UserRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = reviewer.Id,
                AllowRead = true,
                AllowWrite = false,
                IsOwner = false
            });
            dbContext.Repositories.Add(repository);
            await dbContext.SaveChangesAsync();
            return new PullRequestFixture(
                provider,
                scope,
                databasePath,
                repository.Id,
                author.Id,
                reviewer.Id,
                git);
        }

        public async Task RevokeWriteAsync()
        {
            var dbContext = Services.GetRequiredService<GitCandyDbContext>();
            var role = await dbContext.UserRepositoryRoles.SingleAsync(
                item => item.RepositoryId == RepositoryId && item.UserId == AuthorId);
            role.AllowWrite = false;
            await dbContext.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            await RootProvider.DisposeAsync();
            if (File.Exists(DatabasePath))
            {
                File.Delete(DatabasePath);
            }
        }

        private static GitCandyUser NewUser(string name) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.com",
            NormalizedEmail = $"{name}@example.com".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
    }

    public sealed class FakePullRequestGitRepository : IPullRequestGitRepository
    {
        public const string MainSha = "1111111111111111111111111111111111111111";
        public const string FeatureSha = "2222222222222222222222222222222222222222";
        public const string UpdatedFeatureSha = "4444444444444444444444444444444444444444";
        private const string EmptySha = "3333333333333333333333333333333333333333";

        public Dictionary<long, string> HeadReferences { get; } = [];
        public string? LastStorageName { get; private set; }
        public string FeatureHeadSha { get; set; } = FeatureSha;
        public bool CanRemap { get; set; } = true;
        public int MergeCallCount { get; private set; }

        public IReadOnlyList<PullRequestBranch> GetBranches(
            string repositoryStorageName,
            CancellationToken cancellationToken = default) =>
            [
                new PullRequestBranch("main", MainSha),
                new PullRequestBranch("feature", FeatureSha),
                new PullRequestBranch("empty", EmptySha)
            ];

        public PullRequestBranchComparison? CompareBranches(
            string repositoryStorageName,
            string sourceBranch,
            string targetBranch,
            CancellationToken cancellationToken = default)
        {
            if (targetBranch != "main")
            {
                return null;
            }

            return sourceBranch switch
            {
                "feature" => new PullRequestBranchComparison(MainSha, FeatureHeadSha, 1, 0),
                "empty" => new PullRequestBranchComparison(MainSha, EmptySha, 0, 1),
                "main" => new PullRequestBranchComparison(MainSha, MainSha, 0, 0),
                _ => null
            };
        }

        public PullRequestBranchComparison? CompareBranches(
            string sourceRepositoryStorageName,
            string sourceBranch,
            string targetRepositoryStorageName,
            string targetBranch,
            CancellationToken cancellationToken = default) =>
            CompareBranches(sourceRepositoryStorageName, sourceBranch, targetBranch, cancellationToken);

        public PullRequestChangeSet? ReadChangeSet(
            string repositoryStorageName,
            string baseSha,
            string headSha,
            int commitPage,
            int commitPageSize,
            bool includeFiles,
            CancellationToken cancellationToken = default)
        {
            LastStorageName = repositoryStorageName;
            return new PullRequestChangeSet(
                MainSha,
                baseSha,
                headSha,
                AheadBy: 1,
                BehindBy: 0,
                commitPage,
                commitPageSize,
                HasNextCommitPage: false,
                [new PullRequestCommit(
                    FeatureSha,
                    "Feature\n",
                    "Feature",
                    "Author",
                    "author@example.com",
                    DateTimeOffset.UtcNow,
                    [MainSha])],
                includeFiles
                    ? [new PullRequestFileChange("README.md", null, "Modified", false, 1, 0, "+feature")]
                    : [],
                DiffTruncated: false);
        }

        public void UpdatePullRequestHead(
            string repositoryStorageName,
            long number,
            string headSha,
            CancellationToken cancellationToken = default) =>
            HeadReferences[number] = headSha;

        public void UpdatePullRequestHead(
            string sourceRepositoryStorageName,
            string sourceBranch,
            string targetRepositoryStorageName,
            long number,
            string expectedHeadSha,
            CancellationToken cancellationToken = default) =>
            UpdatePullRequestHead(targetRepositoryStorageName, number, expectedHeadSha, cancellationToken);

        public PullRequestGitMergeability EvaluateMergeability(
            string sourceRepositoryStorageName,
            string sourceBranch,
            string targetRepositoryStorageName,
            string targetBranch,
            long number,
            string expectedBaseSha,
            string expectedHeadSha,
            CancellationToken cancellationToken = default) =>
            new(true, true, true, true, false, expectedHeadSha, expectedBaseSha);

        public PullRequestGitMergeResult Merge(
            string sourceRepositoryStorageName,
            string sourceBranch,
            string targetRepositoryStorageName,
            string targetBranch,
            long number,
            string expectedBaseSha,
            string expectedHeadSha,
            PullRequestMergeMethod method,
            string message,
            string actorName,
            string actorEmail,
            DateTimeOffset timestamp,
            CancellationToken cancellationToken = default)
        {
            MergeCallCount++;
            return new(PullRequestMutationResult.Succeeded, "cccccccccccccccccccccccccccccccccccccccc");
        }

        public bool RollbackMerge(
            string targetRepositoryStorageName,
            string targetBranch,
            string mergeCommitSha,
            string previousTargetSha,
            CancellationToken cancellationToken = default) => true;

        public PullRequestReviewAnchor? CaptureReviewAnchor(
            string repositoryStorageName,
            string baseSha,
            string headSha,
            string path,
            PullRequestDiffSide side,
            int startLine,
            int endLine,
            CancellationToken cancellationToken = default) =>
            path == "README.md" && side == PullRequestDiffSide.New && startLine == 2 && endLine == 2
                ? new PullRequestReviewAnchor(path, side, startLine, endLine, "fake-context")
                : null;

        public PullRequestReviewAnchor? RemapReviewAnchor(
            string repositoryStorageName,
            string baseSha,
            string headSha,
            PullRequestDiffSide side,
            string context,
            CancellationToken cancellationToken = default) =>
            CanRemap ? new PullRequestReviewAnchor("README.md", side, 2, 2, context) : null;

        public void DeletePullRequestHead(
            string repositoryStorageName,
            long number,
            CancellationToken cancellationToken = default) =>
            HeadReferences.Remove(number);
    }

    private sealed class RejectingPullRequestMergeHook : IPullRequestMergeHook
    {
        public Task<PullRequestMutationResult> ValidateAsync(
            PullRequestMergeContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PullRequestMutationResult.Forbidden);

        public Task OnMergedAsync(
            PullRequestMergedEvent mergedEvent,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
