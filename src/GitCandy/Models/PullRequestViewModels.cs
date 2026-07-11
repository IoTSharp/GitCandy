using System.ComponentModel.DataAnnotations;
using GitCandy.Application;
using GitCandy.Git;
using GitCandy.PullRequests;

namespace GitCandy.Models;

public sealed class PullRequestIndexViewModel
{
    public required RepositoryAddressResolution Repository { get; init; }
    public required PullRequestPage PullRequests { get; init; }
    public required PullRequestQuery Query { get; init; }
    public bool CanCreate { get; init; }
}

public sealed class PullRequestFormViewModel
{
    [Required, StringLength(256)]
    public string Title { get; set; } = string.Empty;

    [StringLength(65536)]
    public string Body { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string SourceBranch { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string TargetBranch { get; set; } = string.Empty;

    public bool IsDraft { get; set; }
    public long Version { get; set; }
    public IReadOnlyList<PullRequestBranch> Branches { get; set; } = [];
}

public sealed class PullRequestDetailViewModel
{
    public required RepositoryAddressResolution Repository { get; init; }
    public required PullRequestDetails PullRequest { get; init; }
    public bool CanEdit { get; init; }
    public bool CanChangeState { get; init; }
    public required PullRequestReviewOverview ReviewOverview { get; init; }
    public bool CanManageReviewers { get; init; }
    public bool CanSubmitReview { get; init; }
    public bool IsOwner { get; init; }
    public string? CurrentUserId { get; init; }
}

public sealed class PullRequestReviewFormViewModel
{
    public PullRequestReviewState State { get; set; } = PullRequestReviewState.Commented;

    [StringLength(65536)]
    public string Body { get; set; } = string.Empty;
}

public sealed class PullRequestReviewDismissFormViewModel
{
    [Required, StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class PullRequestChangesViewModel
{
    public required RepositoryAddressResolution Repository { get; init; }
    public required PullRequestDetails PullRequest { get; init; }
    public required PullRequestChangeSet Changes { get; init; }
    public IReadOnlyList<PullRequestReviewThread> ReviewThreads { get; init; } = [];
    public bool CanReview { get; init; }
    public bool IsOwner { get; init; }
    public string? CurrentUserId { get; init; }
}

public sealed class PullRequestReviewThreadFormViewModel
{
    [Required, StringLength(1024)]
    public string Path { get; set; } = string.Empty;
    [StringLength(1024)]
    public string? OldPath { get; set; }
    public PullRequestDiffSide Side { get; set; } = PullRequestDiffSide.New;
    [Range(1, int.MaxValue)]
    public int StartLine { get; set; }
    [Range(1, int.MaxValue)]
    public int EndLine { get; set; }
    [Required, StringLength(65536)]
    public string Body { get; set; } = string.Empty;
}

public sealed class PullRequestReviewReplyFormViewModel
{
    [Required, StringLength(65536)]
    public string Body { get; set; } = string.Empty;
}

public sealed class PullRequestCommitViewModel
{
    public required RepositoryAddressResolution Repository { get; init; }
    public required PullRequestDetails PullRequest { get; init; }
    public required RepositoryCommitResult Commit { get; init; }
}
