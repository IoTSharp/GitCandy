namespace GitCandy.Git;

/// <summary>
/// Git HTTP 与 SSH 共用的受控 Git transport backend 入口。
/// </summary>
public interface IGitTransportBackend
{
    /// <summary>
    /// 验证仓库目录存在且最终路径没有逃逸配置的仓库根目录。
    /// </summary>
    /// <param name="repository">仓库上下文。</param>
    void EnsureRepositoryExists(GitRepositoryContext repository);

    /// <summary>
    /// 流式执行一次 Git transport 请求。
    /// </summary>
    /// <param name="request">Git transport 请求。</param>
    /// <param name="input">传给 Git helper 标准输入的流。</param>
    /// <param name="output">接收 Git helper 标准输出的流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ExecuteAsync(
        GitTransportRequest request,
        Stream input,
        Stream output,
        CancellationToken cancellationToken = default);
}
