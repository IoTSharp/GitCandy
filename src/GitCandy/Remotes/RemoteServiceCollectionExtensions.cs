using GitCandy.Remotes;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GitCandy.Web.Remotes;

/// <summary>注册远程账号 provider、HTTP 边界和加密凭据保险库。</summary>
public static class RemoteServiceCollectionExtensions
{
    internal const string HttpClientName = "GitCandy.RemoteProviders";

    /// <summary>注册由固定管理员配置驱动的远程账号连接基础设施。</summary>
    public static IServiceCollection AddGitCandyRemoteProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(RemoteProviderOptions.SectionName);
        var configured = section.Get<RemoteProviderOptions>() ?? new RemoteProviderOptions();
        services.AddOptions<RemoteProviderOptions>()
            .Bind(section)
            .Validate(ValidateOptions, "Remote provider endpoints and timeout are invalid.")
            .ValidateOnStart();
        services.AddHttpClient(HttpClientName, static (serviceProvider, client) =>
            {
                client.Timeout = serviceProvider.GetRequiredService<IOptions<RemoteProviderOptions>>()
                    .Value
                    .RequestTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectTimeout = TimeSpan.FromSeconds(10)
            })
            .RedactLoggedHeaders(static _ => true);

        if (configured.GitHub.Enabled)
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Transient<IRemoteRepositoryProvider, GitHubRemoteRepositoryProvider>());
        }

        if (configured.GitLab.Enabled)
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Transient<IRemoteRepositoryProvider, GitLabRemoteRepositoryProvider>());
        }

        if (configured.Gitee.Enabled)
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Transient<IRemoteRepositoryProvider, GiteeRemoteRepositoryProvider>());
        }

        services.TryAddSingleton<IRemoteCredentialVault, DataProtectionRemoteCredentialVault>();
        services.TryAddSingleton<IRemoteMirrorPushEventSink, RemoteMirrorPushEventSink>();
        services.TryAddSingleton<IRemoteMirrorService, RemoteMirrorService>();
        return services;
    }

    internal static IServiceCollection AddGitCandyRemoteMirrorEventSink(this IServiceCollection services)
    {
        services.TryAddSingleton<IRemoteMirrorPushEventSink, RemoteMirrorPushEventSink>();
        return services;
    }

    private static bool ValidateOptions(RemoteProviderOptions options) =>
        options.RequestTimeout >= TimeSpan.FromSeconds(1)
        && options.RequestTimeout <= TimeSpan.FromMinutes(2)
        && ValidateEndpoint(options.GitHub)
        && ValidateEndpoint(options.GitLab)
        && ValidateEndpoint(options.Gitee);

    private static bool ValidateEndpoint(RemoteProviderEndpointOptions? endpoint)
    {
        if (endpoint is null)
        {
            return false;
        }

        return !endpoint.Enabled
            || (!string.IsNullOrWhiteSpace(endpoint.ServerUrl)
                && !string.IsNullOrWhiteSpace(endpoint.ApiBaseUrl)
                && endpoint.ServerUrl.Length <= 512
                && endpoint.ApiBaseUrl.Length <= 2048
                && TryValidateUri(endpoint.ServerUrl)
                && TryValidateUri(endpoint.ApiBaseUrl));
    }

    private static bool TryValidateUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && uri.IsLoopback))
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);
}
