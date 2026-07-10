using System.ComponentModel.DataAnnotations;

namespace GitCandy.Models;

public sealed class SettingViewModel
{
    public bool IsPublicServer { get; init; }

    public bool ForceSsl { get; init; }

    public int SslPort { get; init; }

    public bool AllowRegisterUser { get; init; }

    public bool AllowRepositoryCreation { get; init; }

    public string RepositoryPath { get; init; } = string.Empty;

    public string CachePath { get; init; } = string.Empty;

    public string GitCorePath { get; init; } = string.Empty;

    public int NumberOfCommitsPerPage { get; init; }

    public int NumberOfItemsPerList { get; init; }

    public int NumberOfRepositoryContributors { get; init; }

    public int SshPort { get; init; }

    public bool EnableSsh { get; init; }
}
