using System.ComponentModel.DataAnnotations;

namespace GitCandy.Models.Account;

public sealed class ForgotPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public bool Submitted { get; set; }
}

public sealed class ResetPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class AdminAccountRecoveryViewModel
{
    [Required]
    public string UserName { get; set; } = string.Empty;

    [Required, MinLength(10)]
    public string Reason { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm the recovery request.")]
    public bool Confirmed { get; set; }
}
