using System.ComponentModel.DataAnnotations;
using GitCandy.Application;

namespace GitCandy.Models;

public sealed class NameChangeViewModel
{
    [Required]
    public string CurrentSlug { get; set; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9._-]+$")]
    [Display(Name = "New URL slug")]
    public string NewSlug { get; set; } = string.Empty;

    [Display(Name = "Disaster recovery override")]
    public bool UseOverride { get; set; }

    [StringLength(500)]
    [Display(Name = "Override reason")]
    public string? OverrideReason { get; set; }

    [Display(Name = "I confirm this audited override")]
    public bool ConfirmOverride { get; set; }

    public NameManagementSnapshot? Snapshot { get; set; }

    public NameSubjectType SubjectType { get; set; }
}
