using GitCandy.Application;
using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.Sqlite;
using GitCandy.Issues;
using GitCandy.PullRequests;
using GitCandy.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class WorkspaceServiceTests
{
    [TestMethod]
    public async Task Dashboard_WithAssignedMentionAndReviewRequest_SeparatesTodoNotificationAndFeed()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IWorkspaceService>();

        await service.RefreshProjectionsAsync();
        var dashboard = await service.GetDashboardAsync(fixture.ReviewerId, false);

        Assert.IsTrue(dashboard.Todos.Value.Any(item => item.Kind == WorkspaceTodoKind.IssueAssignment));
        Assert.IsTrue(dashboard.Todos.Value.Any(item => item.Kind == WorkspaceTodoKind.Mention));
        Assert.IsTrue(dashboard.Todos.Value.Any(item => item.Kind == WorkspaceTodoKind.PullRequestReview));
        Assert.IsTrue(dashboard.Notifications.Value.Any(item => item.Reason == WorkspaceNotificationReason.Mention));
        Assert.IsTrue(dashboard.Notifications.Value.Any(item => item.Reason == WorkspaceNotificationReason.ReviewRequest));
        Assert.IsTrue(dashboard.Feed.Value.Any(item => item.Type == WorkspaceActivityType.IssueCreated));
        Assert.IsTrue(dashboard.Feed.Value.Any(item => item.Type == WorkspaceActivityType.PullRequestCreated));

        var todo = dashboard.Todos.Value[0];
        Assert.IsTrue(await service.CompleteTodoAsync(todo.Id, fixture.ReviewerId, todo.Version));
        var notifications = await service.GetNotificationsAsync(fixture.ReviewerId, false, new WorkspaceNotificationQuery());
        Assert.IsTrue(notifications.Items.Any(item => item.ReadAtUtc is null), "Completing a Todo must not read notifications.");
        var unread = notifications.Items.First(item => item.ReadAtUtc is null);
        Assert.IsTrue(await service.MarkNotificationReadAsync(unread.Id, fixture.ReviewerId));
        var todos = await service.GetTodosAsync(fixture.ReviewerId, 1, 25, cancellationToken: default);
        Assert.IsFalse(todos.Items.Any(item => item.Id == todo.Id), "Reading a notification must not restore a completed Todo.");
    }

    [TestMethod]
    public async Task Workspace_WithPermissionRevoked_RemovesPrivateTodoNotificationAndFeed()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IWorkspaceService>();
        await service.RefreshProjectionsAsync();
        var todo = (await service.GetTodosAsync(fixture.ReviewerId, 1, 25, cancellationToken: default)).Items[0];
        var notification = (await service.GetNotificationsAsync(fixture.ReviewerId, false, new WorkspaceNotificationQuery())).Items[0];

        await fixture.RevokeReviewerAccessAsync();

        Assert.IsFalse(await service.CompleteTodoAsync(todo.Id, fixture.ReviewerId, todo.Version));
        Assert.IsFalse(await service.MarkNotificationReadAsync(notification.Id, fixture.ReviewerId));
        Assert.AreEqual(0, (await service.GetTodosAsync(fixture.ReviewerId, 1, 25, cancellationToken: default)).TotalCount);
        Assert.AreEqual(0, (await service.GetNotificationsAsync(fixture.ReviewerId, false, new WorkspaceNotificationQuery())).TotalCount);
        Assert.AreEqual(0, (await service.GetFeedAsync(fixture.ReviewerId, false, 1, 25)).TotalCount);
    }

    [TestMethod]
    public async Task Feed_WithTeamContextButUnreadableRepository_FiltersRepositoryActivity()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var db = fixture.Services.GetRequiredService<GitCandyDbContext>();
        var team = new GitCandyTeam
        {
            Name = "reviewers",
            NormalizedName = "REVIEWERS",
            DisplayName = "Reviewers",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        db.UserTeamRoles.Add(new GitCandyUserTeamRole { TeamId = team.Id, UserId = fixture.ReviewerId });
        db.ActivityEvents.Add(new GitCandyActivityEvent
        {
            EventId = "team-private-repository",
            RepositoryId = fixture.PrivateRepositoryId,
            TeamId = team.Id,
            ResourceType = WorkspaceResourceType.Repository,
            ResourceId = $"repository:{fixture.PrivateRepositoryId}",
            Type = WorkspaceActivityType.Push,
            Title = "Private repository activity",
            Url = "/author/private-work",
            OccurredAtUtc = DateTime.UtcNow,
            RetainUntilUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var feed = await fixture.Services.GetRequiredService<IWorkspaceService>()
            .GetFeedAsync(fixture.ReviewerId, false, 1, 25);

        Assert.IsFalse(feed.Items.Any(item => item.EventId == "team-private-repository"));
    }

    [TestMethod]
    public async Task StarsAndExplore_WithRepeatedWrites_StayIdempotentAndPublicOnly()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IWorkspaceService>();

        Assert.IsTrue(await service.SetStarAsync(fixture.PublicRepositoryId, fixture.ReviewerId, true));
        Assert.IsTrue(await service.SetStarAsync(fixture.PublicRepositoryId, fixture.ReviewerId, true));
        Assert.AreEqual(1, await fixture.CountStarsAsync(fixture.PublicRepositoryId, fixture.ReviewerId));
        Assert.IsTrue(await service.SetStarAsync(fixture.PublicRepositoryId, fixture.AuthorId, true));
        Assert.IsTrue(await service.SetStarAsync(fixture.PrivateRepositoryId, fixture.AuthorId, true));

        await service.RefreshProjectionsAsync();
        Assert.AreEqual(1, (await fixture.GetTodayMetricAsync(fixture.PublicRepositoryId)).StarCount,
            "The repository owner's own Star must not influence recommendation weighting.");
        var explore = await service.ExploreAsync(new ExploreQuery(PageSize: 50));
        Assert.IsTrue(explore.Repositories.Items.Any(item => item.Id == fixture.PublicRepositoryId));
        Assert.IsFalse(explore.Repositories.Items.Any(item => item.Id == fixture.PrivateRepositoryId));
        Assert.IsFalse(explore.IsFallback);

        var profile = await service.GetPublicProfileAsync("author", PublicProfileTab.Repositories, 1, 50);
        Assert.IsNotNull(profile);
        Assert.IsTrue(profile.Repositories.Items.Any(item => item.Id == fixture.PublicRepositoryId));
        Assert.IsFalse(profile.Repositories.Items.Any(item => item.Id == fixture.PrivateRepositoryId));
    }

    [TestMethod]
    public async Task Metrics_WithRepeatedVisitorOnSameDay_CountsOnePageView()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var recorder = fixture.Services.GetRequiredService<IRepositoryMetricRecorder>();

        await recorder.RecordPageViewAsync(fixture.PublicRepositoryId, new string('A', 48), fixture.ReviewerId);
        await recorder.RecordPageViewAsync(fixture.PublicRepositoryId, new string('A', 48), fixture.ReviewerId);
        await recorder.RecordSuccessfulDownloadAsync(fixture.PublicRepositoryId);
        await recorder.RecordSuccessfulGitFetchAsync(fixture.PublicRepositoryId, fixture.ReviewerId);

        var metric = await fixture.GetTodayMetricAsync(fixture.PublicRepositoryId);
        Assert.AreEqual(1L, metric.UniquePageViewCount);
        Assert.AreEqual(1L, metric.SuccessfulDownloadCount);
        Assert.AreEqual(1L, metric.SuccessfulGitFetchCount);
    }

    [TestMethod]
    public async Task ActivityPublisher_WithSameRepositoryState_PublishesOnePushEvent()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var publisher = fixture.Services.GetRequiredService<IWorkspaceActivityPublisher>();
        var stateId = new string('A', 64);

        await publisher.PublishPushAsync("public-work", "author", stateId);
        await publisher.PublishPushAsync("public-work", "author", stateId);

        var feed = await fixture.Services.GetRequiredService<IWorkspaceService>()
            .GetFeedAsync(fixture.ReviewerId, false, 1, 25);
        Assert.AreEqual(1, feed.Items.Count(item => item.Type == WorkspaceActivityType.Push));
    }

    private sealed class WorkspaceFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly string _databasePath;

        private WorkspaceFixture(ServiceProvider provider, AsyncServiceScope scope, string databasePath, string authorId,
            string reviewerId, long publicRepositoryId, long privateRepositoryId)
        {
            _provider = provider;
            _scope = scope;
            _databasePath = databasePath;
            AuthorId = authorId;
            ReviewerId = reviewerId;
            PublicRepositoryId = publicRepositoryId;
            PrivateRepositoryId = privateRepositoryId;
        }

        public IServiceProvider Services => _scope.ServiceProvider;
        public string AuthorId { get; }
        public string ReviewerId { get; }
        public long PublicRepositoryId { get; }
        public long PrivateRepositoryId { get; }

        public static async Task<WorkspaceFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), "GitCandy.Tests", $"workspace-{Guid.NewGuid():N}.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlite",
                ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());
            services.AddGitCandyApplicationServices();
            var provider = services.BuildServiceProvider(validateScopes: true);
            var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await db.Database.MigrateAsync();
            var author = NewUser("author");
            var reviewer = NewUser("reviewer");
            db.Users.AddRange(author, reviewer);
            var authorNamespace = new GitCandyNamespace
            {
                OwnerType = NamespaceOwnerType.User,
                UserId = author.Id,
                Slug = "author",
                NormalizedSlug = "AUTHOR",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Namespaces.Add(authorNamespace);
            await db.SaveChangesAsync();
            var publicRepository = NewRepository(authorNamespace.Id, "public-work", false);
            var privateRepository = NewRepository(authorNamespace.Id, "private-work", true);
            publicRepository.UserRoles.Add(new GitCandyUserRepositoryRole { UserId = author.Id, AllowRead = true, AllowWrite = true, IsOwner = true });
            publicRepository.UserRoles.Add(new GitCandyUserRepositoryRole { UserId = reviewer.Id, AllowRead = true });
            privateRepository.UserRoles.Add(new GitCandyUserRepositoryRole { UserId = author.Id, AllowRead = true, AllowWrite = true, IsOwner = true });
            db.Repositories.AddRange(publicRepository, privateRepository);
            await db.SaveChangesAsync();
            var now = DateTime.UtcNow;
            var issue = new GitCandyIssue
            {
                RepositoryId = publicRepository.Id,
                Number = 1,
                Title = "Investigate parser",
                BodyMarkdown = string.Empty,
                BodyHtml = string.Empty,
                AuthorUserId = author.Id,
                AssigneeUserId = reviewer.Id,
                State = IssueState.Open,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            issue.Timeline.Add(new GitCandyIssueTimelineEvent { ActorUserId = author.Id, Type = IssueEventType.Created, CreatedAtUtc = now });
            db.Issues.Add(issue);
            await db.SaveChangesAsync();
            db.IssueNotifications.Add(new GitCandyIssueNotification { UserId = reviewer.Id, RepositoryId = publicRepository.Id,
                IssueId = issue.Id, ActorUserId = author.Id, Type = IssueNotificationType.Mention, CreatedAtUtc = now });
            var pullRequest = new GitCandyPullRequest
            {
                RepositoryId = publicRepository.Id,
                Number = 2,
                Title = "Fix parser",
                BodyMarkdown = string.Empty,
                BodyHtml = string.Empty,
                AuthorUserId = author.Id,
                SourceNamespaceSnapshot = "author",
                SourceRepositorySnapshot = "public-work",
                SourceBranch = "fix",
                TargetBranch = "main",
                OriginalBaseSha = new string('a', 40),
                OriginalHeadSha = new string('b', 40),
                CurrentBaseSha = new string('a', 40),
                CurrentHeadSha = new string('b', 40),
                State = PullRequestState.Open,
                ActivePairKey = "1:fix:main",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            pullRequest.Reviewers.Add(new GitCandyPullRequestReviewer { ReviewerUserId = reviewer.Id, RequestedByUserId = author.Id, RequestedAtUtc = now });
            pullRequest.Timeline.Add(new GitCandyPullRequestTimelineEvent { ActorUserId = author.Id, Type = PullRequestEventType.Created, CreatedAtUtc = now });
            db.PullRequests.Add(pullRequest);
            await db.SaveChangesAsync();
            return new WorkspaceFixture(provider, scope, databasePath, author.Id, reviewer.Id, publicRepository.Id, privateRepository.Id);
        }

        public async Task RevokeReviewerAccessAsync()
        {
            var db = Services.GetRequiredService<GitCandyDbContext>();
            var repository = await db.Repositories.SingleAsync(item => item.Id == PublicRepositoryId);
            repository.IsPrivate = true;
            repository.AllowAnonymousRead = false;
            var role = await db.UserRepositoryRoles.SingleAsync(item => item.RepositoryId == PublicRepositoryId && item.UserId == ReviewerId);
            db.UserRepositoryRoles.Remove(role);
            await db.SaveChangesAsync();
        }

        public Task<int> CountStarsAsync(long repositoryId, string userId) => Services.GetRequiredService<GitCandyDbContext>()
            .RepositoryStars.CountAsync(item => item.RepositoryId == repositoryId && item.UserId == userId);

        public Task<GitCandyRepositoryMetricDaily> GetTodayMetricAsync(long repositoryId) => Services.GetRequiredService<GitCandyDbContext>()
            .RepositoryMetricsDaily.SingleAsync(item => item.RepositoryId == repositoryId && item.DayUtc == DateTime.UtcNow.Date);

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }

        private static GitCandyRepository NewRepository(long namespaceId, string name, bool isPrivate) => new()
        {
            NamespaceId = namespaceId,
            Name = name,
            StorageName = name,
            Description = $"{name} description",
            CreatedAtUtc = DateTime.UtcNow,
            IsPrivate = isPrivate,
            AllowAnonymousRead = !isPrivate,
            WorkItemSequence = new GitCandyWorkItemSequence()
        };

        private static GitCandyUser NewUser(string name) => new()
        {
            Id = Guid.NewGuid().ToString("N"), UserName = name, NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.com", NormalizedEmail = $"{name}@example.com".ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString("N")
        };
    }
}
