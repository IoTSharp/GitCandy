using System.ComponentModel.DataAnnotations;
using GitCandy.Enterprise;

namespace GitCandy.Models.Account;

public sealed class LoginViewModel
{
    [Required]
    [Display(Name = "Username or email")]
    public string UserNameOrEmail { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public IReadOnlyList<ExternalLoginProviderViewModel> ExternalProviders { get; set; } = [];

    public IReadOnlyList<EnterpriseLoginOption> EnterpriseProviders { get; set; } = [];
}
