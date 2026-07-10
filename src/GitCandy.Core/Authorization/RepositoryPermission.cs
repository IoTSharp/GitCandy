namespace GitCandy.Authorization;

/// <summary>
/// 仓库资源授权操作。
/// </summary>
public enum RepositoryPermission
{
    /// <summary>
    /// 读取仓库。
    /// </summary>
    Read,

    /// <summary>
    /// 写入仓库。
    /// </summary>
    Write,

    /// <summary>
    /// 管理 owner 专属仓库设置。
    /// </summary>
    Owner
}
