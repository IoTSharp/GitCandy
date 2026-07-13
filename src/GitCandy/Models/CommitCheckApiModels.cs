using System.ComponentModel.DataAnnotations;

namespace GitCandy.Models;

public sealed class CommitStatusRequest
{
    [Required]
    [StringLength(100)]
    public string Context { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string State { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? TargetUrl { get; set; }

    [StringLength(128)]
    public string? ExternalId { get; set; }
}

public sealed class CheckRunRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string State { get; set; } = string.Empty;

    [StringLength(500)]
    public string Summary { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? DetailsUrl { get; set; }

    [StringLength(128)]
    public string? ExternalId { get; set; }
}
