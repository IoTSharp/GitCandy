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
            FakePullRequestGitRepository git)
        {
            RootProvider = rootProvider;
            Scope = scope;
            DatabasePath = databasePath;
            RepositoryId = repositoryId;
            AuthorId = authorId;
            Git = git;
        }

        private ServiceProvider RootProvider { get; }
        private AsyncServiceScope Scope { get; }
        public IServiceProvider Services => Scope.ServiceProvider;
        private string DatabasePath { get; }
        public long RepositoryId { get; }
        public string AuthorId { get; }
        public FakePullRequestGitRepository Git { get; }

        public static async Task<PullRequestFixture> CreateAsync()
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
            services.AddSingleton<IPullRequestGitRepository>(git);
            var provider = services.BuildServiceProvider(validateScopes: true);
            var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.MigrateAsync();
            var author = NewUser("author");
            dbContext.Users.Add(author);
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
            dbContext.Repositories.Add(repository);
            await dbContext.SaveChangesAsync();
            return new PullRequestFixture(
                provider,
                scope,
                databasePath,
                repository.Id,
                author.Id,
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
        private const string EmptySha = "3333333333333333333333333333333333333333";

        public Dictionary<long, string> HeadReferences { get; } = [];
        public string? LastStorageName { get; private set; }

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
                "feature" => new PullRequestBranchComparison(MainSha, FeatureSha, 1, 0),
                "empty" => new PullRequestBranchComparison(MainSha, EmptySha, 0, 1),
                "main" => new PullRequestBranchComparison(MainSha, MainSha, 0, 0),
                _ => null
            };
        }

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

        public void DeletePullRequestHead(
            string repositoryStorageName,
            long number,
            CancellationToken cancellationToken = default) =>
            HeadReferences.Remove(number);
    }
}
