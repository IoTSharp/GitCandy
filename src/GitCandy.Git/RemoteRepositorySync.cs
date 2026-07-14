using LibGit2Sharp;

namespace GitCandy.Git;

/// <summary>受控远程仓库同步支持的 Git 操作。</summary>
public enum RemoteRepositorySyncOperation
{
    Fetch,
    Push
}

/// <summary>远程仓库同步失败的稳定分类。</summary>
public static class RemoteRepositorySyncErrorCodes
{
    /// <summary>远端拒绝或无法使用提供的认证凭据。</summary>
    public const string AuthenticationFailed = "remote_authentication_failed";
    /// <summary>凭据有效，但没有目标仓库操作权限。</summary>
    public const string AuthorizationDenied = "remote_authorization_denied";
    /// <summary>远端仓库不存在或已被删除。</summary>
    public const string RepositoryNotFound = "remote_repository_not_found";
    /// <summary>目标 ref 会发生未显式允许的 non-fast-forward 更新。</summary>
    public const string NonFastForward = "remote_non_fast_forward";
    /// <summary>DNS、连接或其他网络操作失败。</summary>
    public const string NetworkFailed = "remote_network_failed";
    /// <summary>TLS 握手或证书校验失败。</summary>
    public const string TlsFailed = "remote_tls_failed";
    /// <summary>操作超过配置的执行时间。</summary>
    public const string TimedOut = "remote_sync_timed_out";
    /// <summary>凭据类型或 helper 配置不受支持。</summary>
    public const string CredentialUnsupported = "remote_credential_unsupported";
    /// <summary>Git 子进程无法启动。</summary>
    public const string ProcessStartFailed = "remote_process_start_failed";
    /// <summary>Git 子进程以未分类失败结束。</summary>
    public const string ProcessFailed = "remote_process_failed";
}

/// <summary>一个经过结构化验证的 Git refspec。</summary>
public sealed record RemoteRepositorySyncRefSpec
{
    /// <summary>创建更新或创建目标 ref 的 refspec。</summary>
    public RemoteRepositorySyncRefSpec(
        string sourceReference,
        string destinationReference,
        bool force = false)
    {
        SourceReference = NormalizeReference(sourceReference, nameof(sourceReference));
        DestinationReference = NormalizeReference(destinationReference, nameof(destinationReference));
        if (SourceReference.Count(static character => character == '*')
            != DestinationReference.Count(static character => character == '*'))
        {
            throw new ArgumentException("A wildcard refspec must map matching source and destination patterns.");
        }

        Force = force;
    }

    private RemoteRepositorySyncRefSpec(string destinationReference)
    {
        DestinationReference = NormalizeReference(destinationReference, nameof(destinationReference));
        if (DestinationReference.Contains('*', StringComparison.Ordinal))
        {
            throw new ArgumentException("A ref deletion must target one exact reference.", nameof(destinationReference));
        }
    }

    /// <summary>源 ref；删除 refspec 时为空。</summary>
    public string? SourceReference { get; }

    /// <summary>目标 ref。</summary>
    public string DestinationReference { get; }

    /// <summary>是否显式允许非 fast-forward 更新。</summary>
    public bool Force { get; }

    /// <summary>是否删除目标 ref。</summary>
    public bool IsDelete => SourceReference is null;

    /// <summary>创建一个只允许用于 push 的精确 ref 删除。</summary>
    public static RemoteRepositorySyncRefSpec Delete(string destinationReference) =>
        new(destinationReference);

    internal string ToArgument()
    {
        var value = SourceReference is null
            ? $":{DestinationReference}"
            : $"{SourceReference}:{DestinationReference}";
        return Force ? $"+{value}" : value;
    }

    private static string NormalizeReference(string reference, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference, parameterName);
        var value = reference.Trim();
        var wildcardCount = value.Count(static character => character == '*');
        var validationValue = wildcardCount == 1
            ? value.Replace("*", "gitcandy-pattern", StringComparison.Ordinal)
            : value;
        if (!value.StartsWith("refs/", StringComparison.Ordinal)
            || value.Length > 1024
            || wildcardCount > 1
            || !Reference.IsValidName(validationValue)
            || value.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new ArgumentException("A valid fully qualified Git reference is required.", parameterName);
        }

        return value;
    }
}

/// <summary>一次受控远程 fetch 或 push 请求。</summary>
public sealed record RemoteRepositorySyncRequest
{
    /// <summary>创建并验证远程同步请求。</summary>
    public RemoteRepositorySyncRequest(
        GitRepositoryContext repository,
        Remotes.RemoteProviderKind provider,
        Uri providerServerUrl,
        Uri remoteGitUrl,
        RemoteRepositorySyncOperation operation,
        IReadOnlyList<RemoteRepositorySyncRefSpec> refSpecs,
        bool prune = false)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(providerServerUrl);
        ArgumentNullException.ThrowIfNull(remoteGitUrl);
        ArgumentNullException.ThrowIfNull(refSpecs);
        if (string.IsNullOrWhiteSpace(repository.RepositoryName)
            || string.IsNullOrWhiteSpace(repository.RepositoryPath))
        {
            throw new ArgumentException("A repository name and path are required.", nameof(repository));
        }
        if (refSpecs.Count is 0 or > 1024 || refSpecs.Any(static item => item is null))
        {
            throw new ArgumentException("A remote sync requires between 1 and 1024 refspecs.", nameof(refSpecs));
        }
        if (operation == RemoteRepositorySyncOperation.Fetch
            && refSpecs.Any(static item => item.IsDelete))
        {
            throw new ArgumentException("Fetch requests cannot contain ref deletion refspecs.", nameof(refSpecs));
        }
        if (operation == RemoteRepositorySyncOperation.Push && prune)
        {
            throw new ArgumentException("Push pruning must be expressed as explicit ref deletions.", nameof(prune));
        }

        ProviderServerUrl = NormalizeEndpoint(providerServerUrl, nameof(providerServerUrl));
        RemoteGitUrl = NormalizeEndpoint(remoteGitUrl, nameof(remoteGitUrl));
        EnsureRemoteBelongsToProvider(ProviderServerUrl, RemoteGitUrl);

        Repository = repository;
        Provider = provider;
        Operation = operation;
        RefSpecs = refSpecs.ToArray();
        Prune = prune;
    }

    /// <summary>已解析到本地 repository root 的仓库上下文。</summary>
    public GitRepositoryContext Repository { get; }

    /// <summary>用于认证语义和诊断分类的 provider。</summary>
    public Remotes.RemoteProviderKind Provider { get; }

    /// <summary>管理员固定的 provider origin 与可选 base path。</summary>
    public Uri ProviderServerUrl { get; }

    /// <summary>不含凭据的远端 Git HTTPS URL。</summary>
    public Uri RemoteGitUrl { get; }

    /// <summary>要执行的受控 Git 操作。</summary>
    public RemoteRepositorySyncOperation Operation { get; }

    /// <summary>要同步的结构化 refspec。</summary>
    public IReadOnlyList<RemoteRepositorySyncRefSpec> RefSpecs { get; }

    /// <summary>fetch 时是否删除映射范围内远端已不存在的本地 refs。</summary>
    public bool Prune { get; }

    private static Uri NormalizeEndpoint(Uri endpoint, string parameterName)
    {
        if (!endpoint.IsAbsoluteUri
            || (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !(string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && endpoint.IsLoopback))
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(
                "A remote endpoint must be HTTPS (or loopback HTTP) without credentials, query, or fragment.",
                parameterName);
        }

        return endpoint;
    }

    private static void EnsureRemoteBelongsToProvider(Uri providerServerUrl, Uri remoteGitUrl)
    {
        var providerPath = providerServerUrl.AbsolutePath.TrimEnd('/');
        var remotePath = remoteGitUrl.AbsolutePath;
        var pathMatches = providerPath.Length == 0
            || string.Equals(remotePath, providerPath, StringComparison.Ordinal)
            || remotePath.StartsWith(providerPath + "/", StringComparison.Ordinal);
        if (!string.Equals(providerServerUrl.Scheme, remoteGitUrl.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(providerServerUrl.Host, remoteGitUrl.Host, StringComparison.OrdinalIgnoreCase)
            || providerServerUrl.Port != remoteGitUrl.Port
            || !pathMatches)
        {
            throw new ArgumentException(
                "The remote Git URL must remain within the configured provider origin and base path.",
                nameof(remoteGitUrl));
        }
    }
}

/// <summary>一次成功远程同步执行的非敏感结果。</summary>
/// <param name="Operation">已完成的操作。</param>
/// <param name="Duration">子进程执行时长。</param>
public sealed record RemoteRepositorySyncResult(
    RemoteRepositorySyncOperation Operation,
    TimeSpan Duration);

/// <summary>远程同步失败，并只公开稳定错误码和安全消息。</summary>
public sealed class RemoteRepositorySyncException : Exception
{
    public RemoteRepositorySyncException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    /// <summary>可持久化和展示映射的稳定错误码。</summary>
    public string Code { get; }
}

/// <summary>远程同步 Git 子进程的资源边界配置。</summary>
public sealed class RemoteRepositorySyncOptions
{
    /// <summary>与 provider 连接共用的标准配置节。</summary>
    public const string SectionName = "GitCandy:Remotes";

    /// <summary>单次远程 Git 子进程的最长执行时间。</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>排空 stdout/stderr 使用的流式缓冲区大小。</summary>
    public int StreamBufferSize { get; set; } = 81920;

    /// <summary>仅用于错误分类的 stderr 尾部字符上限。</summary>
    public int MaxDiagnosticCharacters { get; set; } = 8192;
}

/// <summary>credential helper 子命令的宿主程序集位置。</summary>
public sealed class RemoteGitCredentialHelperOptions
{
    /// <summary>承载 credential helper 子命令的 GitCandy 主程序集路径。</summary>
    public string CommandAssemblyPath { get; set; } = string.Empty;
}

/// <summary>远程 fetch/push 的唯一受控 Git 进程入口。</summary>
public interface IRemoteRepositorySyncBackend
{
    /// <summary>执行一次经过路径、origin 和 refspec 校验的远程 Git 操作。</summary>
    /// <param name="request">结构化同步请求。</param>
    /// <param name="credential">仅在运行时解析的远端凭据。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    Task<RemoteRepositorySyncResult> ExecuteAsync(
        RemoteRepositorySyncRequest request,
        Remotes.RemoteCredential credential,
        CancellationToken cancellationToken = default);
}
