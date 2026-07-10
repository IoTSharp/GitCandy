using System.ComponentModel.DataAnnotations;

namespace GitCandy.Models.Account;

public sealed record ExternalLoginProviderViewModel(string Scheme, string DisplayName);

public sealed record UserExternalLoginViewModel(string LoginProvider, string ProviderDisplayName);

public sealed class TwoFactorLoginViewModel
{
    [Required]
    [Display(Name = "Authenticator code")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Remember this browser")]
    public bool RememberMachine { get; set; }

    public bool RememberMe { get; set; }
}

public sealed class RecoveryCodeLoginViewModel
{
    [Required]
    [Display(Name = "Recovery code")]
    public string RecoveryCode { get; set; } = string.Empty;
}

public sealed class EnableAuthenticatorViewModel
{
    public string SharedKey { get; set; } = string.Empty;

    public string AuthenticatorUri { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Verification code")]
    public string Code { get; set; } = string.Empty;
}

public sealed class RecoveryCodesViewModel
{
    public IReadOnlyList<string> RecoveryCodes { get; set; } = [];
}

public sealed class AccountSecurityViewModel
{
    public string? StatusMessage { get; set; }

    public bool HasPassword { get; set; }

    public bool HasAuthenticator { get; set; }

    public bool IsTwoFactorEnabled { get; set; }

    public int RecoveryCodesLeft { get; set; }

    public bool IsMachineRemembered { get; set; }

    public IReadOnlyList<UserExternalLoginViewModel> CurrentLogins { get; set; } = [];

    public IReadOnlyList<ExternalLoginProviderViewModel> OtherLogins { get; set; } = [];
}

public sealed class SetPasswordViewModel
{
    [Required]
    [StringLength(100)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword))]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ExternalLoginConfirmationViewModel
{
    [Required]
    [StringLength(20, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9_-]+$")]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [StringLength(254)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
