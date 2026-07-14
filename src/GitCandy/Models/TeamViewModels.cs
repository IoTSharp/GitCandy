using System.ComponentModel.DataAnnotations;
using GitCandy.Application;
using GitCandy.Teams;

namespace GitCandy.Models;

public sealed class TeamIndexViewModel
{
    public string? Query { get; init; }

    public IReadOnlyList<TeamSummary> Teams { get; init; } = [];
}

public sealed class TeamFormViewModel
{
    [Required]
    [StringLength(20, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9_-]+$")]
    public string Name { get; set; } = string.Empty;

    [StringLength(128)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(500)]
    [DataType(DataType.MultilineText)]
    public string Description { get; set; } = string.Empty;
}

public sealed class TeamDetailsViewModel
{
    public required TeamDetails Team { get; init; }

    public TeamRole? CurrentRole { get; init; }

    public bool CanManageMembers { get; init; }

    public bool CanEdit { get; init; }

    public bool CanRename { get; init; }

    public bool CanDelete { get; init; }

    public bool CanViewEnterpriseConnections { get; init; }

    public IReadOnlyList<TeamAuditEventSummary> AuditEvents { get; init; } = [];

    public bool CanManage => CanManageMembers || CanEdit || CanRename || CanDelete;
}

public sealed class TeamMemberCommand
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string User { get; set; } = string.Empty;

    [Required]
    public string Act { get; set; } = string.Empty;
}

public sealed class TeamMemberBatchCommand
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Operation { get; set; } = string.Empty;

    public List<TeamMemberBatchItem> Members { get; set; } = [];
}

public sealed class TeamMemberBatchItem
{
    public bool Selected { get; set; }

    [Required]
    public string User { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;
}
