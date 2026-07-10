using System.Security.Cryptography;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>
/// 基于 ASP.NET Core Identity 和 EF Core 的用户管理服务。
/// </summary>
public sealed class UserAdministrationService(
    UserManager<GitCandyUser> userManager,
    GitCandyDbContext dbContext) : IUserAdministrationService
{
    private readonly UserManager<GitCandyUser> _userManager = userManager;
    private readonly GitCandyDbContext _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<GitCandyUser> users = _userManager.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var queryText = query.Trim();
            var normalizedQuery = queryText.ToUpperInvariant();
            var queryPattern = $"%{queryText}%";
            users = users.Where(user =>
                (user.NormalizedUserName != null && user.NormalizedUserName.Contains(normalizedQuery))
                || (user.NormalizedEmail != null && user.NormalizedEmail.Contains(normalizedQuery))
                || (user.DisplayName != null && EF.Functions.Like(user.DisplayName, queryPattern))
                || (user.Description != null && EF.Functions.Like(user.Description, queryPattern)));
        }

        var administratorRoleId = await _dbContext.Roles
            .AsNoTracking()
            .Where(role => role.NormalizedName == RoleNames.Administrator.ToUpperInvariant())
            .Select(role => role.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return await users
            .OrderBy(user => user.NormalizedUserName)
            .Select(user => new UserSummary(
                user.UserName ?? string.Empty,
                user.DisplayName ?? user.UserName ?? string.Empty,
                user.Email ?? string.Empty,
                administratorRoleId != null && _dbContext.UserRoles.Any(role =>
                    role.UserId == user.Id && role.RoleId == administratorRoleId)))
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UserDetails?> GetUserAsync(
        string userName,
        string? viewerUserId,
        bool viewerIsAdministrator,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var teams = await (
                from role in _dbContext.UserTeamRoles.AsNoTracking()
                join team in _dbContext.Teams.AsNoTracking() on role.TeamId equals team.Id
                where role.UserId == user.Id
                orderby team.NormalizedName
                select team.Name)
            .ToArrayAsync(cancellationToken);
        var repositories = await (
                from role in _dbContext.UserRepositoryRoles.AsNoTracking()
                join repository in _dbContext.Repositories.AsNoTracking()
                    on role.RepositoryId equals repository.Id
                where role.UserId == user.Id
                    && ((viewerIsAdministrator && viewerUserId != null)
                        || (!repository.IsPrivate && repository.AllowAnonymousRead)
                        || (viewerUserId != null
                            && (_dbContext.UserRepositoryRoles.Any(viewerRole =>
                                    viewerRole.RepositoryId == repository.Id
                                    && viewerRole.UserId == viewerUserId
                                    && viewerRole.AllowRead)
                                || _dbContext.TeamRepositoryRoles.Any(teamRole =>
                                    teamRole.RepositoryId == repository.Id
                                    && teamRole.AllowRead
                                    && _dbContext.UserTeamRoles.Any(viewerTeamRole =>
                                        viewerTeamRole.TeamId == teamRole.TeamId
                                        && viewerTeamRole.UserId == viewerUserId)))))
                orderby repository.NormalizedName
                select repository.Name)
            .ToArrayAsync(cancellationToken);

        return new UserDetails(
            user.UserName ?? string.Empty,
            user.DisplayName ?? user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.Description ?? string.Empty,
            await _userManager.IsInRoleAsync(user, RoleNames.Administrator),
            teams,
            repositories);
    }

    /// <inheritdoc />
    public async Task<IdentityResult> UpdateUserAsync(
        string userName,
        string displayName,
        string email,
        string description,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return IdentityResult.Failed(new IdentityError { Description = "User was not found." });
        }

        user.DisplayName = displayName.Trim();
        user.Description = description.Trim();
        user.Email = email.Trim();
        user.NormalizedEmail = _userManager.NormalizeEmail(user.Email);

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return updateResult;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var currentlyAdministrator = await _userManager.IsInRoleAsync(user, RoleNames.Administrator);
        if (isAdministrator == currentlyAdministrator)
        {
            return IdentityResult.Success;
        }

        return isAdministrator
            ? await _userManager.AddToRoleAsync(user, RoleNames.Administrator)
            : await _userManager.RemoveFromRoleAsync(user, RoleNames.Administrator);
    }

    /// <inheritdoc />
    public async Task<IdentityResult> DeleteUserAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByNameAsync(userName);
        return user is null
            ? IdentityResult.Failed(new IdentityError { Description = "User was not found." })
            : await _userManager.DeleteAsync(user);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SshKeySummary>?> GetSshKeysAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        return await _dbContext.SshKeys
            .AsNoTracking()
            .Where(key => key.UserId == user.Id)
            .OrderBy(key => key.ImportedAtUtc)
            .Select(key => new SshKeySummary(key.KeyType, key.Fingerprint, key.ImportedAtUtc))
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> AddSshKeyAsync(
        string userName,
        string publicKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);

        var user = await _userManager.FindByNameAsync(userName);
        var parts = publicKey.Trim().Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        if (user is null || parts.Length < 2 || parts[0].Length > 20 || parts[1].Length > 600)
        {
            return null;
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        var fingerprint = Convert.ToBase64String(SHA256.HashData(keyBytes)).TrimEnd('=');
        if (await _dbContext.SshKeys.AnyAsync(key => key.Fingerprint == fingerprint, cancellationToken))
        {
            return null;
        }

        _dbContext.SshKeys.Add(new GitCandySshKey
        {
            UserId = user.Id,
            KeyType = parts[0],
            PublicKey = parts[1],
            Fingerprint = fingerprint,
            ImportedAtUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return fingerprint;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteSshKeyAsync(
        string userName,
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return false;
        }

        var key = await _dbContext.SshKeys.SingleOrDefaultAsync(
            item => item.UserId == user.Id && item.Fingerprint == fingerprint,
            cancellationToken);
        if (key is null)
        {
            return false;
        }

        _dbContext.SshKeys.Remove(key);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
