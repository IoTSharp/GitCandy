using System.ComponentModel.DataAnnotations;
using GitCandy.Application;

namespace GitCandy.Models.Account;

public sealed class AccountIndexViewModel
{
    public string? Query { get; init; }

    public IReadOnlyList<UserSummary> Users { get; init; } = [];
}

public sealed class AccountDetailsViewModel
{
    public required UserDetails User { get; init; }

    public bool CanEdit { get; init; }
}

public sealed class EditAccountViewModel
{
    [Required]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [StringLength(254)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [StringLength(512)]
    [DataType(DataType.MultilineText)]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "System administrator")]
    public bool IsAdministrator { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Your current password")]
    public string CurrentPassword { get; set; } = string.Empty;
}

public sealed class SshKeysViewModel
{
    public string UserName { get; init; } = string.Empty;

    public IReadOnlyList<SshKeySummary> Keys { get; init; } = [];
}

public sealed class AddSshKeyViewModel
{
    [Required]
    public string User { get; set; } = string.Empty;

    [Required]
    [Display(Name = "OpenSSH public key")]
    public string PublicKey { get; set; } = string.Empty;
}
