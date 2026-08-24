using System.ComponentModel.DataAnnotations;
using GitCandy.Remotes;

namespace GitCandy.Models;

public sealed class RepositoryMirrorIndexViewModel
{
    public required string NamespaceSlug { get; init; }
    public required string RepositoryName { get; init; }
    public IReadOnlyList<RemoteMirrorSummary> Mirrors { get; init; } = [];
    public IReadOnlyList<RemoteMirrorJobSummary> Jobs { get; init; } = [];
    public IReadOnlyList<RemoteConnectionSummary> Connections { get; init; } = [];
    public RepositoryMirrorFormViewModel Form { get; init; } = new();

    public string CanonicalRepositoryPath =>
        $"/{Uri.EscapeDataString(NamespaceSlug)}/{Uri.EscapeDataString(RepositoryName)}";

    public RemoteMirrorJobSummary? GetJob(long mirrorId) =>
        Jobs.FirstOrDefault(item => item.MirrorId == mirrorId);
}

public sealed class RepositoryMirrorFormViewModel
{
    [Range(1, long.MaxValue)]
    [Display(Name = "Remote account")]
    public long ConnectionId { get; set; }

    [Required, StringLength(256)]
    [Display(Name = "Stable repository ID")]
    public string RemoteRepositoryId { get; set; } = string.Empty;

    [Required, StringLength(256)]
    [Display(Name = "Remote owner")]
    public string RemoteOwner { get; set; } = string.Empty;

    [Required, StringLength(256)]
    [Display(Name = "Remote repository")]
    public string RemoteRepositoryName { get; set; } = string.Empty;

    [Required, StringLength(2048)]
    [Url]
    [Display(Name = "Remote web URL")]
    public string RemoteWebUrl { get; set; } = string.Empty;

    public RemoteMirrorDirection Direction { get; set; } = RemoteMirrorDirection.Pull;

    [Display(Name = "Ref filter")]
    public RemoteMirrorRefFilterKind RefFilterKind { get; set; } = RemoteMirrorRefFilterKind.AllRefs;

    [StringLength(2000)]
    [Display(Name = "Ref patterns")]
    public string? RefFilterPattern { get; set; }

    [Range(5, 10080)]
    [Display(Name = "Reconcile interval (minutes)")]
    public int ScheduleIntervalMinutes { get; set; } = 15;

    [Display(Name = "Divergence policy")]
    public RemoteMirrorDivergencePolicy DivergencePolicy { get; set; } = RemoteMirrorDivergencePolicy.Stop;

    [Display(Name = "Propagate ref deletions")]
    public bool Prune { get; set; }

    [Display(Name = "I understand force overwrite can discard target refs")]
    public bool ConfirmForce { get; set; }
}
