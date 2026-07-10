namespace GitCandy.Git;

/// <summary>
/// 受控 Git transport backend 支持的 Git 服务。
/// </summary>
public enum GitTransportService
{
    /// <summary>
    /// clone 和 fetch 使用的 upload-pack 服务。
    /// </summary>
    UploadPack,

    /// <summary>
    /// push 使用的 receive-pack 服务。
    /// </summary>
    ReceivePack,

    /// <summary>
    /// SSH archive 使用的 upload-archive 服务。
    /// </summary>
    UploadArchive
}
