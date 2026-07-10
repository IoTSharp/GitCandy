using System.ComponentModel.DataAnnotations;

namespace GitCandy.Models.Account;

public sealed class CreateAccountViewModel
{
    [Required]
    [StringLength(20, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9_-]+$")]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [StringLength(128)]
    [Display(Name = "Display name")]
    public string? DisplayName { get; set; }

    [Required]
    [StringLength(254)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [StringLength(512)]
    [DataType(DataType.MultilineText)]
    public string? Description { get; set; }
}
