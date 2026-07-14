using System.ComponentModel.DataAnnotations;
using GitCandy.Enterprise;

namespace GitCandy.Models;

public sealed class EnterpriseConnectionIndexViewModel
{
    public required string TeamName { get; init; }
    public IReadOnlyList<EnterpriseConnectionSummary> Connections { get; init; } = [];
    public bool CanManage { get; init; }
}

public sealed class EnterpriseConnectionFormViewModel
{
    public long? Id { get; set; }

    [Required]
    public string TeamName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public EnterpriseProviderKind Provider { get; set; }

    [Required, StringLength(256)]
    [Display(Name = "External organization ID")]
    public string ExternalOrganizationId { get; set; } = string.Empty;

    [StringLength(2048), Url]
    public string? Authority { get; set; }

    [StringLength(256)]
    [Display(Name = "Client ID")]
    public string? ClientId { get; set; }

    [StringLength(2048), Url]
    [Display(Name = "API base URL")]
    public string? ApiBaseUrl { get; set; }

    [StringLength(8192)]
    [Display(Name = "Non-secret configuration JSON")]
    public string? ConfigurationJson { get; set; }

    [Required, StringLength(512)]
    [Display(Name = "Secret reference")]
    public string SecretReference { get; set; } = string.Empty;

    [StringLength(512)]
    [Display(Name = "Webhook secret reference")]
    public string? WebhookSecretReference { get; set; }

    [Display(Name = "Enable login")]
    public bool LoginEnabled { get; set; }

    [Display(Name = "Enable provisioning")]
    public bool ProvisioningEnabled { get; set; }

    [Display(Name = "Connection enabled")]
    public bool IsEnabled { get; set; } = true;

    public EnterpriseConnectionEdit ToEdit() => new(
        Id,
        Name,
        Provider,
        ExternalOrganizationId,
        Authority,
        ClientId,
        ApiBaseUrl,
        ConfigurationJson,
        SecretReference,
        WebhookSecretReference,
        LoginEnabled,
        ProvisioningEnabled,
        IsEnabled);

    public static EnterpriseConnectionFormViewModel FromSummary(EnterpriseConnectionSummary summary) => new()
    {
        Id = summary.Id,
        TeamName = summary.TeamName,
        Name = summary.Name,
        Provider = summary.Provider,
        ExternalOrganizationId = summary.ExternalOrganizationId,
        Authority = summary.Authority,
        ClientId = summary.ClientId,
        ApiBaseUrl = summary.ApiBaseUrl,
        ConfigurationJson = summary.ConfigurationJson,
        SecretReference = summary.SecretReference,
        WebhookSecretReference = summary.WebhookSecretReference,
        LoginEnabled = summary.LoginEnabled,
        ProvisioningEnabled = summary.ProvisioningEnabled,
        IsEnabled = summary.IsEnabled
    };
}
