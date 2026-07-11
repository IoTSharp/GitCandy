using GitCandy.Application;
using GitCandy.Ssh;
using GitCandy.Issues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GitCandy.Data.Configuration;

/// <summary>
/// 注册由 EF Core 和 ASP.NET Core Identity 实现的 GitCandy 应用服务。
/// </summary>
public static class GitCandyApplicationServiceCollectionExtensions
{
    /// <summary>
    /// 注册仓库、团队、成员与用户管理用例的持久化实现。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>同一个服务集合。</returns>
    public static IServiceCollection AddGitCandyApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IMembershipService, MembershipService>();
        services.TryAddScoped<IUserAdministrationService, UserAdministrationService>();
        services.TryAddScoped<ITeamService, TeamService>();
        services.TryAddScoped<IRepositoryService, RepositoryService>();
        services.TryAddScoped<IRepositoryManagementService, RepositoryManagementService>();
        services.TryAddScoped<IRepositoryAddressResolver, RepositoryAddressResolver>();
        services.TryAddScoped<INamespaceProvisioningService, NamespaceProvisioningService>();
        services.TryAddScoped<INameManagementService, NameManagementService>();
        services.TryAddSingleton<IIssueMarkdownRenderer, IssueMarkdownRenderer>();
        services.TryAddScoped<IIssueService, IssueService>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISshAccessService, SshAccessService>();

        return services;
    }
}
