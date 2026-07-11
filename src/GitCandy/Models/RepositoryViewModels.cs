using System.ComponentModel.DataAnnotations;
using GitCandy.Application;
using GitCandy.Git;

namespace GitCandy.Models;

public sealed class RepositoryIndexViewModel
{
    public IReadOnlyList<RepositorySummary> Repositories { get; init; } = [];

    public bool CanCreateRepository { get; init; }
}

public sealed class RepositoryFormViewModel
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    [RegularExpression("(?i)^[a-z][a-z0-9._-]+(?<!\\.git)$")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    [DataType(DataType.MultilineText)]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Private repository")]
    public bool IsPrivate { get; set; }

    [Display(Name = "Allow anonymous read")]
    public bool AllowAnonymousRead { get; set; } = true;

    [Display(Name = "Allow anonymous write")]
    public bool AllowAnonymousWrite { get; set; }

    [Display(Name = "Initialize repository")]
    public RepositoryCreationMode InitializationMode { get; set; }

    [StringLength(2048)]
    [Display(Name = "Remote URL or source repository")]
    public string? Source { get; set; }

    [StringLength(255)]
    [Display(Name = "Default branch")]
    public string? DefaultBranch { get; set; }

    public RepositoryEdit ToCommand()
    {
        return new RepositoryEdit(
            Name,
            Description,
            IsPrivate,
            AllowAnonymousRead,
            AllowAnonymousWrite);
    }
}

public sealed class RepositoryTreeViewModel
{
    public required string RepositoryName { get; init; }

    public RepositoryTreeResult? Tree { get; init; }
}

public sealed class RepositoryBlobViewModel
{
    public required string RepositoryName { get; init; }

    public required RepositoryBlobResult Blob { get; init; }
}

public sealed class RepositoryCommitsViewModel
{
    public required string RepositoryName { get; init; }

    public required RepositoryCommitPage Page { get; init; }
}

public sealed class RepositoryCommitViewModel
{
    public required string RepositoryName { get; init; }

    public required RepositoryCommitResult Commit { get; init; }
}

public sealed class RepositoryBlameViewModel
{
    public required string RepositoryName { get; init; }

    public required RepositoryBlameResult Blame { get; init; }
}

public sealed class RepositoryCompareViewModel
{
    public required string RepositoryName { get; init; }

    public required RepositoryCompareResult Compare { get; init; }
}

public sealed class RepositoryDetailsViewModel
{
    public required RepositoryDetails Repository { get; init; }

    public bool CanManage { get; init; }
}

public sealed class RepositoryUserRoleCommand
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string User { get; set; } = string.Empty;

    [Required]
    public string Act { get; set; } = string.Empty;

    public bool Value { get; set; }
}

public sealed class RepositoryTeamRoleCommand
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Team { get; set; } = string.Empty;

    [Required]
    public string Act { get; set; } = string.Empty;

    public bool Value { get; set; }
}
