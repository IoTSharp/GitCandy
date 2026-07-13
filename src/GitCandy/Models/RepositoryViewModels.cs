using System.ComponentModel.DataAnnotations;
using GitCandy.Application;
using GitCandy.Credentials;
using GitCandy.Git;
using GitCandy.Governance;
using GitCandy.Audit;

namespace GitCandy.Models;

public sealed class RepositoryIndexViewModel
{
    public IReadOnlyList<RepositorySummary> Repositories { get; init; } = [];

    public bool CanCreateRepository { get; init; }
}

public sealed class RepositoryFormViewModel
{
    [StringLength(50, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9._-]+$")]
    [Display(Name = "Namespace")]
    public string? NamespaceSlug { get; set; }

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
            AllowAnonymousWrite,
            NamespaceSlug: NamespaceSlug);
    }
}

public sealed class RepositoryTreeViewModel
{
    public required string RepositoryName { get; init; }

    public string? NamespaceSlug { get; init; }

    public string? Description { get; init; }

    public bool CanManage { get; init; }

    public bool CanStar { get; init; }

    public bool IsStarred { get; init; }

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

public sealed record RepositoryBranchesViewModel(string NamespaceSlug, string RepositoryName, IReadOnlyList<RepositoryBranchSummary> Branches, bool CanWrite);

public sealed record RepositoryTagsViewModel(string NamespaceSlug, string RepositoryName, IReadOnlyList<RepositoryTagSummary> Tags, bool CanWrite);

public sealed record RepositoryContributorsViewModel(string NamespaceSlug, string RepositoryName, RepositoryStatisticsResult? Statistics);

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

public sealed class RepositoryDeployKeysViewModel
{
    public required string NamespaceSlug { get; init; }
    public required string RepositoryName { get; init; }
    public IReadOnlyList<DeployKeySummary> Keys { get; init; } = [];
    public AddDeployKeyViewModel Create { get; init; } = new();

    public string CanonicalRepositoryPath =>
        $"/{Uri.EscapeDataString(NamespaceSlug)}/{Uri.EscapeDataString(RepositoryName)}";
}

public sealed class AddDeployKeyViewModel
{
    [Required]
    public string RepositoryName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "OpenSSH public key")]
    public string PublicKey { get; set; } = string.Empty;

    [Display(Name = "Allow push")]
    public bool CanWrite { get; set; }

    [Display(Name = "Expires at (UTC)")]
    [DataType(DataType.DateTime)]
    public DateTime? ExpiresAtUtc { get; set; }
}

public sealed class RepositoryBranchProtectionsViewModel
{
    public required string NamespaceSlug { get; init; }
    public required string RepositoryName { get; init; }
    public IReadOnlyList<BranchProtectionSummary> Rules { get; init; } = [];
    public BranchProtectionFormViewModel Rule { get; init; } = new();

    public string CanonicalRepositoryPath =>
        $"/{Uri.EscapeDataString(NamespaceSlug)}/{Uri.EscapeDataString(RepositoryName)}";
}

public sealed record RepositoryAuditViewModel(
    string NamespaceSlug,
    string RepositoryName,
    IReadOnlyList<AuditEvent> Events);

public sealed class BranchProtectionFormViewModel
{
    public long? Id { get; set; }

    [Required]
    public string RepositoryName { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    [Display(Name = "Branch pattern")]
    public string Pattern { get; set; } = string.Empty;

    [Display(Name = "Direct push access")]
    public BranchAccessLevel PushAccess { get; set; } = BranchAccessLevel.RepositoryWrite;

    [Display(Name = "Web merge access")]
    public BranchAccessLevel MergeAccess { get; set; } = BranchAccessLevel.RepositoryWrite;

    [Display(Name = "Allow force pushes")]
    public bool AllowForcePushes { get; set; }

    [Display(Name = "Allow branch deletion")]
    public bool AllowDeletions { get; set; }

    [Display(Name = "Allow administrators to bypass this rule")]
    public bool AllowAdministratorBypass { get; set; }

    [Display(Name = "Required checks")]
    public string RequiredChecks { get; set; } = string.Empty;

    [Range(0, 20)]
    [Display(Name = "Required approvals")]
    public int RequiredApprovals { get; set; }

    [Display(Name = "Require changed-path code owner review")]
    public bool RequireCodeOwnerReviews { get; set; }

    [Display(Name = "Dismiss approvals after the Pull Request head changes")]
    public bool DismissStaleApprovals { get; set; } = true;

    public BranchProtectionEdit ToEdit()
    {
        return new BranchProtectionEdit(
            Id,
            Pattern,
            PushAccess,
            MergeAccess,
            AllowForcePushes,
            AllowDeletions,
            AllowAdministratorBypass,
            RequiredChecks.Split([',', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            RequiredApprovals,
            RequireCodeOwnerReviews,
            DismissStaleApprovals);
    }
}
