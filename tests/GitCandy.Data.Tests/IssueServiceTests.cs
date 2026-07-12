using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.Sqlite;
using GitCandy.Issues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class IssueServiceTests
{
    [TestMethod]
    public async Task CreateIssue_WithMarkdownAndMention_PersistsSafeTimelineAndNotification()
    {
        await using var fixture = await IssueFixture.CreateAsync();
        var issueService = fixture.Services.GetRequiredService<IIssueService>();

        var issue = await issueService.CreateIssueAsync(fixture.RepositoryId, new CreateIssueCommand(
            "Parser fails on input",
            "@reviewer please inspect #1\n\n- [x] reproduced\n\n```csharp\nvar value = 1;\n```\n\n<script>alert(1)</script> [bad](javascript:alert(1))",
            fixture.AuthorId));

        Assert.AreEqual(1L, issue.Number);
        StringAssert.Contains(issue.BodyHtml, "<pre><code");
        StringAssert.Contains(issue.BodyHtml, "type=\"checkbox\"");
        StringAssert.Contains(issue.BodyHtml, "/Account/Detail/reviewer");
        Assert.IsFalse(issue.BodyHtml.Contains("<script", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(issue.BodyHtml.Contains("javascript:", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(issue.IsSubscribed);

        var notifications = await issueService.GetNotificationsAsync(fixture.ReviewerId, false);
        Assert.HasCount(1, notifications);
        Assert.AreEqual(IssueNotificationType.Mention, notifications[0].Type);

        await fixture.SetPrivateWithoutReviewerAccessAsync();
        notifications = await issueService.GetNotificationsAsync(fixture.ReviewerId, false);
        Assert.IsEmpty(notifications, "Notification reads must recheck repository access.");
    }

    [TestMethod]
    public async Task CreateIssue_WithConcurrentRequests_AllocatesUniqueMonotonicNumbers()
    {
        await using var fixture = await IssueFixture.CreateAsync();
        var tasks = Enumerable.Range(0, 8).Select(async index =>
        {
            await using var scope = fixture.RootProvider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IIssueService>().CreateIssueAsync(
                fixture.RepositoryId,
                new CreateIssueCommand($"Concurrent issue {index}", string.Empty, fixture.AuthorId));
        });

        var issues = await Task.WhenAll(tasks);
        CollectionAssert.AreEquivalent(
            Enumerable.Range(1, 8).Select(static value => (long)value).ToArray(),
            issues.Select(static item => item.Number).ToArray());
    }

    [TestMethod]
    public async Task CreateIssue_WithConcurrentRequests_DoesNotExceedDiscussionRateLimit()
    {
        await using var fixture = await IssueFixture.CreateAsync();
        var tasks = Enumerable.Range(0, 24).Select(async index =>
        {
            await using var scope = fixture.RootProvider.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IIssueService>().CreateIssueAsync(
                    fixture.RepositoryId,
                    new CreateIssueCommand($"Rate limited issue {index}", string.Empty, fixture.AuthorId));
                return true;
            }
            catch (IssueRateLimitException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        Assert.AreEqual(20, results.Count(static succeeded => succeeded));
        Assert.AreEqual(4, results.Count(static succeeded => !succeeded));

        await using var verificationScope = fixture.RootProvider.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        Assert.AreEqual(20, await dbContext.Issues.CountAsync());
        Assert.AreEqual(
            20,
            await dbContext.IssueTimelineEvents.CountAsync(item => item.Type == IssueEventType.Created));
    }

    [TestMethod]
    public async Task Discussion_WithMetadataStateAndRelation_PreservesTimeline()
    {
        await using var fixture = await IssueFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IIssueService>();
        var label = await service.SaveLabelAsync(fixture.RepositoryId, null, "bug", "d73a49", "Defect", true);
        var milestone = await service.SaveMilestoneAsync(fixture.RepositoryId, null, "v1", "First release", DateTime.UtcNow.AddDays(7), true);
        Assert.IsNotNull(label);
        Assert.IsNotNull(milestone);
        var first = await service.CreateIssueAsync(fixture.RepositoryId, new CreateIssueCommand("First", "body", fixture.AuthorId));
        var second = await service.CreateIssueAsync(fixture.RepositoryId, new CreateIssueCommand("Second", "body", fixture.AuthorId));
        var autoClosed = await service.CreateIssueAsync(fixture.RepositoryId, new CreateIssueCommand("Auto close", "body", fixture.AuthorId));

        Assert.AreEqual(IssueMutationResult.Succeeded, await service.SetLabelAsync(fixture.RepositoryId, first.Number, fixture.AuthorId, true, label.Id, true));
        Assert.AreEqual(IssueMutationResult.Succeeded, await service.SetMilestoneAsync(fixture.RepositoryId, first.Number, fixture.AuthorId, true, milestone.Id));
        Assert.AreEqual(IssueMutationResult.Succeeded, await service.AddCommentAsync(fixture.RepositoryId, first.Number, fixture.AuthorId, true, "comment"));
        Assert.AreEqual(IssueMutationResult.Succeeded, await service.AddRelationAsync(fixture.RepositoryId, first.Number, second.Number, fixture.AuthorId, true, IssueRelationType.Blocks));
        Assert.AreEqual(IssueMutationResult.Succeeded, await service.SetStateAsync(fixture.RepositoryId, first.Number, fixture.AuthorId, true, IssueState.Closed));
        Assert.AreEqual(1, await service.ApplyClosingReferencesAsync(
            fixture.RepositoryId,
            fixture.AuthorId,
            $"Fixes #{autoClosed.Number}",
            "0123456789abcdef"));
        Assert.AreEqual(0, await service.ApplyClosingReferencesAsync(
            fixture.RepositoryId,
            fixture.AuthorId,
            $"Fixes #{autoClosed.Number}",
            "0123456789abcdef"));

        var details = await service.GetIssueAsync(fixture.RepositoryId, first.Number, fixture.AuthorId);
        Assert.IsNotNull(details);
        Assert.AreEqual(IssueState.Closed, details.State);
        Assert.HasCount(1, details.Labels);
        Assert.AreEqual("v1", details.Milestone);
        Assert.IsTrue(details.Timeline.Any(item => item.Type == IssueEventType.Commented));
        Assert.IsTrue(details.Timeline.Any(item => item.Type == IssueEventType.Related));
        Assert.IsTrue(details.Timeline.Any(item => item.Type == IssueEventType.Closed));
        Assert.AreEqual(IssueState.Closed, (await service.GetIssueAsync(fixture.RepositoryId, autoClosed.Number, fixture.AuthorId))?.State);
    }

    private sealed class IssueFixture : IAsyncDisposable
    {
        private IssueFixture(ServiceProvider rootProvider, AsyncServiceScope scope, string databasePath, long repositoryId, string authorId, string reviewerId)
        {
            RootProvider = rootProvider;
            Scope = scope;
            DatabasePath = databasePath;
            RepositoryId = repositoryId;
            AuthorId = authorId;
            ReviewerId = reviewerId;
        }

        public ServiceProvider RootProvider { get; }
        private AsyncServiceScope Scope { get; }
        public IServiceProvider Services => Scope.ServiceProvider;
        private string DatabasePath { get; }
        public long RepositoryId { get; }
        public string AuthorId { get; }
        public string ReviewerId { get; }

        public static async Task<IssueFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), "GitCandy.Tests", $"issues-{Guid.NewGuid():N}.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlite",
                ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False;Default Timeout=10"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());
            services.AddGitCandyApplicationServices();
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
                Name = "issues",
                StorageName = "issues",
                Description = string.Empty,
                CreatedAtUtc = DateTime.UtcNow,
                AllowAnonymousRead = true,
                WorkItemSequence = new GitCandyWorkItemSequence()
            };
            repository.UserRoles.Add(new GitCandyUserRepositoryRole { UserId = author.Id, AllowRead = true, AllowWrite = true, IsOwner = true });
            dbContext.Repositories.Add(repository);
            await dbContext.SaveChangesAsync();
            return new IssueFixture(provider, scope, databasePath, repository.Id, author.Id, reviewer.Id);
        }

        public async Task SetPrivateWithoutReviewerAccessAsync()
        {
            var dbContext = Services.GetRequiredService<GitCandyDbContext>();
            var repository = await dbContext.Repositories.SingleAsync(item => item.Id == RepositoryId);
            repository.IsPrivate = true;
            repository.AllowAnonymousRead = false;
            await dbContext.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            await RootProvider.DisposeAsync();
            if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
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
}
