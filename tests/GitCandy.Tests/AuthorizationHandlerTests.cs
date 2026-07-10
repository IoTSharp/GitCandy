using System.Security.Claims;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Tests;

[TestClass]
public sealed class AuthorizationHandlerTests
{
    private const string AdminUserId = "user-admin";
    private const string OwnerUserId = "user-owner";
    private const string TeamMemberUserId = "user-team-member";
    private const string OtherUserId = "user-other";

    [TestMethod]
    public async Task RepositoryRead_WithAnonymousPrincipal_UsesPublicAndPrivateSemantics()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var publicRead = await authorizationService.AuthorizeAsync(
            anonymous,
            new RepositoryAuthorizationResource("public-read"),
            AuthorizationPolicies.RepositoryRead);
        var publicWrite = await authorizationService.AuthorizeAsync(
            anonymous,
            new RepositoryAuthorizationResource("public-write"),
            AuthorizationPolicies.RepositoryWrite);
        var privateRead = await authorizationService.AuthorizeAsync(
            anonymous,
            new RepositoryAuthorizationResource("private-demo"),
            AuthorizationPolicies.RepositoryRead);

        Assert.IsTrue(publicRead.Succeeded);
        Assert.IsTrue(publicWrite.Succeeded);
        Assert.IsFalse(privateRead.Succeeded, "Private repositories must ignore anonymous flags.");
    }

    [TestMethod]
    public async Task RepositoryPolicies_WithOwnerTeamAdministratorAndOtherUser_ApplyExpectedPermissions()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var resource = new RepositoryAuthorizationResource("private-demo");

        var owner = CreatePrincipal(OwnerUserId, "owner");
        var teamMember = CreatePrincipal(TeamMemberUserId, "team-member");
        var administrator = CreatePrincipal(AdminUserId, "admin", RoleNames.Administrator);
        var other = CreatePrincipal(OtherUserId, "other");

        Assert.IsTrue((await authorizationService.AuthorizeAsync(
            owner,
            resource,
            AuthorizationPolicies.RepositoryOwner)).Succeeded);
        Assert.IsTrue((await authorizationService.AuthorizeAsync(
            teamMember,
            resource,
            AuthorizationPolicies.RepositoryWrite)).Succeeded);
        Assert.IsTrue((await authorizationService.AuthorizeAsync(
            administrator,
            resource,
            AuthorizationPolicies.RepositoryOwner)).Succeeded);
        Assert.IsFalse((await authorizationService.AuthorizeAsync(
            other,
            resource,
            AuthorizationPolicies.RepositoryRead)).Succeeded);
    }

    [TestMethod]
    public async Task TeamAdministrator_WithTeamAdministratorMemberAndSystemAdministrator_AppliesExpectedPermissions()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var resource = new TeamAuthorizationResource("core");

        var owner = CreatePrincipal(OwnerUserId, "owner");
        var teamMember = CreatePrincipal(TeamMemberUserId, "team-member");
        var administrator = CreatePrincipal(AdminUserId, "admin", RoleNames.Administrator);

        Assert.IsTrue((await authorizationService.AuthorizeAsync(
            owner,
            resource,
            AuthorizationPolicies.TeamAdministrator)).Succeeded);
        Assert.IsFalse((await authorizationService.AuthorizeAsync(
            teamMember,
            resource,
            AuthorizationPolicies.TeamAdministrator)).Succeeded);
        Assert.IsTrue((await authorizationService.AuthorizeAsync(
            administrator,
            resource,
            AuthorizationPolicies.TeamAdministrator)).Succeeded);
    }

    [TestMethod]
    public async Task CurrentUser_WithSelfOtherAndSystemAdministrator_AppliesExpectedPermissions()
    {
        await using var fixture = await AuthorizationFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var owner = CreatePrincipal(OwnerUserId, "owner");
        var administrator = CreatePrincipal(AdminUserId, "admin", RoleNames.Administrator);

        Assert.IsTrue((await authorizationService.AuthorizeAsync(
            owner,
            new CurrentUserAuthorizationResource("OWNER"),
            AuthorizationPolicies.CurrentUser)).Succeeded);
        Assert.IsFalse((await authorizationService.AuthorizeAsync(
            owner,
            new CurrentUserAuthorizationResource("other"),
            AuthorizationPolicies.CurrentUser)).Succeeded);
        Assert.IsTrue((await authorizationService.AuthorizeAsync(
            administrator,
            new CurrentUserAuthorizationResource("other"),
            AuthorizationPolicies.CurrentUser)).Succeeded);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, string userName, string? role = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userName)
        };
        if (!string.IsNullOrWhiteSpace(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            IdentityConstants.ApplicationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));
    }

    private sealed class AuthorizationFixture : IAsyncDisposable
    {
        private AuthorizationFixture(ServiceProvider serviceProvider, string tempRoot)
        {
            ServiceProvider = serviceProvider;
            TempRoot = tempRoot;
        }

        private ServiceProvider ServiceProvider { get; }

        private string TempRoot { get; }

        public static async Task<AuthorizationFixture> CreateAsync()
        {
            var tempRoot = TestDirectory.Create();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={Path.Combine(tempRoot, "GitCandy.db")};Pooling=False"
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGitCandyWebShell(configuration);

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var fixture = new AuthorizationFixture(serviceProvider, tempRoot);

            try
            {
                await fixture.SeedAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public AsyncServiceScope CreateScope()
        {
            return ServiceProvider.CreateAsyncScope();
        }

        public async ValueTask DisposeAsync()
        {
            await ServiceProvider.DisposeAsync();
            TestDirectory.Delete(TempRoot);
        }

        private async Task SeedAsync()
        {
            await using var scope = CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var privateRepository = new GitCandyRepository
            {
                Name = "private-demo",
                Description = "Private repository",
                IsPrivate = true,
                AllowAnonymousRead = true,
                AllowAnonymousWrite = true,
                CreatedAtUtc = now
            };
            var coreTeam = new GitCandyTeam
            {
                Name = "core",
                Description = "Core team",
                CreatedAtUtc = now
            };

            dbContext.Users.AddRange(
                NewUser(AdminUserId, "admin"),
                NewUser(OwnerUserId, "owner"),
                NewUser(TeamMemberUserId, "team-member"),
                NewUser(OtherUserId, "other"));
            dbContext.Repositories.AddRange(
                new GitCandyRepository
                {
                    Name = "public-read",
                    Description = "Public read repository",
                    AllowAnonymousRead = true,
                    CreatedAtUtc = now
                },
                new GitCandyRepository
                {
                    Name = "public-write",
                    Description = "Public write repository",
                    AllowAnonymousRead = true,
                    AllowAnonymousWrite = true,
                    CreatedAtUtc = now
                },
                privateRepository);
            dbContext.Teams.Add(coreTeam);
            await dbContext.SaveChangesAsync();

            dbContext.UserRepositoryRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = OwnerUserId,
                RepositoryId = privateRepository.Id,
                AllowRead = true,
                AllowWrite = true,
                IsOwner = true
            });
            dbContext.UserTeamRoles.AddRange(
                new GitCandyUserTeamRole
                {
                    UserId = OwnerUserId,
                    TeamId = coreTeam.Id,
                    IsAdministrator = true
                },
                new GitCandyUserTeamRole
                {
                    UserId = TeamMemberUserId,
                    TeamId = coreTeam.Id,
                    IsAdministrator = false
                });
            dbContext.TeamRepositoryRoles.Add(new GitCandyTeamRepositoryRole
            {
                TeamId = coreTeam.Id,
                RepositoryId = privateRepository.Id,
                AllowRead = true,
                AllowWrite = true
            });
            await dbContext.SaveChangesAsync();
        }

        private static GitCandyUser NewUser(string userId, string userName)
        {
            var normalizedUserName = GitCandyNameNormalizer.Normalize(userName);
            return new GitCandyUser
            {
                Id = userId,
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                Email = $"{userName}@gitcandy.local",
                NormalizedEmail = $"{normalizedUserName}@GITCANDY.LOCAL"
            };
        }
    }
}
