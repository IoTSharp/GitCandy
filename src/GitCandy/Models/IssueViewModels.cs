using System.ComponentModel.DataAnnotations;
using GitCandy.Application;
using GitCandy.Issues;

namespace GitCandy.Models;

public sealed class IssueIndexViewModel
{
    public required RepositoryAddressResolution Repository { get; init; }
    public required IssuePage Issues { get; init; }
    public required IssueQuery Query { get; init; }
    public required IssueRepositoryMetadata Metadata { get; init; }
    public bool CanCreate { get; init; }
    public bool CanManage { get; init; }
}

public sealed class IssueFormViewModel
{
    [Required, StringLength(256)]
    public string Title { get; set; } = string.Empty;

    [StringLength(65536)]
    public string Body { get; set; } = string.Empty;

    public string? AssigneeUserId { get; set; }
    public long? MilestoneId { get; set; }
    public long[] LabelIds { get; set; } = [];
    public long Version { get; set; }
    public IssueRepositoryMetadata? Metadata { get; set; }
}

public sealed class IssueDetailViewModel
{
    public required RepositoryAddressResolution Repository { get; init; }
    public required IssueDetails Issue { get; init; }
    public required IssueRepositoryMetadata Metadata { get; init; }
    public bool CanManage { get; init; }
    public bool CanEdit { get; init; }
    public bool CanChangeState { get; init; }
}

public sealed class IssueMetadataViewModel
{
    public required RepositoryAddressResolution Repository { get; init; }
    public required IssueRepositoryMetadata Metadata { get; init; }
}

public sealed class IssueNotificationIndexViewModel
{
    public required IReadOnlyList<IssueNotificationSummary> Notifications { get; init; }
}
