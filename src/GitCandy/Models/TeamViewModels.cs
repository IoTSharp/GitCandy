using System.ComponentModel.DataAnnotations;
using GitCandy.Application;

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

    [StringLength(500)]
    [DataType(DataType.MultilineText)]
    public string Description { get; set; } = string.Empty;
}

public sealed class TeamDetailsViewModel
{
    public required TeamDetails Team { get; init; }

    public bool CanManage { get; init; }
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
