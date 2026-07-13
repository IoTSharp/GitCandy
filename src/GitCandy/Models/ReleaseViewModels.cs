using System.ComponentModel.DataAnnotations;
using GitCandy.Application;
using GitCandy.Releases;

namespace GitCandy.Models;

public sealed record ReleaseIndexViewModel(
    RepositoryAddressResolution Repository,
    IReadOnlyList<ReleaseDetails> Releases,
    bool CanWrite);

public sealed record ReleaseDetailViewModel(
    RepositoryAddressResolution Repository,
    ReleaseDetails Release,
    bool CanWrite);

public sealed class ReleaseFormViewModel
{
    [Required, StringLength(255)]
    [Display(Name = "Tag")]
    public string TagName { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(65536), DataType(DataType.MultilineText)]
    public string Body { get; set; } = string.Empty;

    [Display(Name = "Save as draft")]
    public bool IsDraft { get; set; }
}

public sealed class ReleaseAssetFormViewModel
{
    [Required]
    public IFormFile? File { get; set; }
}
