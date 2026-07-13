namespace GitCandy.Git;

/// <summary>
/// 一次受控 Git transport backend 执行请求。
/// </summary>
/// <param name="Repository">已完成根目录边界解析的仓库上下文。</param>
/// <param name="Service">要执行的 Git 服务。</param>
/// <param name="StatelessRpc">是否启用 HTTP 使用的 stateless RPC 模式。</param>
/// <param name="AdvertiseRefs">是否只输出 refs advertisement。</param>
/// <param name="ProtocolVersion">Git 协议版本环境值，例如 <c>version=2</c>。</param>
/// <param name="ActorName">用于审计日志的调用方名称；匿名请求使用 <c>anonymous</c>。</param>
/// <param name="RepositoryId">数据层稳定仓库 ID；非业务测试可为 0。</param>
/// <param name="Actor">push gate 使用的结构化用户或 deploy key 身份。</param>
public sealed record GitTransportRequest(
    GitRepositoryContext Repository,
    GitTransportService Service,
    bool StatelessRpc,
    bool AdvertiseRefs,
    string? ProtocolVersion,
    string ActorName,
    long RepositoryId = 0,
    GitTransportActor? Actor = null);

/// <summary>Git transport 内部传给 push gate 的脱敏机器/用户身份。</summary>
public sealed record GitTransportActor(
    string Name,
    string? UserId = null,
    long? DeployKeyId = null);
