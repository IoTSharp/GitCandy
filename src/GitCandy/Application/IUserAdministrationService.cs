using GitCandy.Data.Identity;
using Microsoft.AspNetCore.Identity;

namespace GitCandy.Application;

/// <summary>
/// 用户管理页面所需的 Identity 查询和写入入口。
/// </summary>
public interface IUserAdministrationService
{
    /// <summary>查询用户列表。</summary>
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(string? query, CancellationToken cancellationToken = default);

    /// <summary>读取用户及其团队、仓库关系。</summary>
    Task<UserDetails?> GetUserAsync(
        string userName,
        string? viewerUserId,
        bool viewerIsAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>更新用户资料和管理员角色。</summary>
    Task<IdentityResult> UpdateUserAsync(
        string userName,
        string displayName,
        string email,
        string description,
        bool isAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>删除 Identity 用户及其级联领域关系。</summary>
    Task<IdentityResult> DeleteUserAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>读取用户的 SSH 公钥。</summary>
    Task<IReadOnlyList<SshKeySummary>?> GetSshKeysAsync(
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>导入一个 OpenSSH 格式公钥。</summary>
    Task<string?> AddSshKeyAsync(
        string userName,
        string publicKey,
        CancellationToken cancellationToken = default);

    /// <summary>按指纹删除 SSH 公钥。</summary>
    Task<bool> DeleteSshKeyAsync(
        string userName,
        string fingerprint,
        CancellationToken cancellationToken = default);
}

/// <summary>用户列表摘要。</summary>
public sealed record UserSummary(string UserName, string DisplayName, string Email, bool IsAdministrator);

/// <summary>用户详情。</summary>
public sealed record UserDetails(
    string UserName,
    string DisplayName,
    string Email,
    string Description,
    bool IsAdministrator,
    IReadOnlyList<string> Teams,
    IReadOnlyList<string> Repositories);

/// <summary>SSH 公钥摘要。</summary>
public sealed record SshKeySummary(string KeyType, string Fingerprint, DateTime ImportedAtUtc);
