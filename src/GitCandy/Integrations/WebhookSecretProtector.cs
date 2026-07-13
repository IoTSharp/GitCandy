using GitCandy.Integrations;
using Microsoft.AspNetCore.DataProtection;

namespace GitCandy.Web.Integrations;

internal sealed class WebhookSecretProtector(IDataProtectionProvider dataProtectionProvider)
    : IWebhookSecretProtector
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "GitCandy.Webhooks.Secret.v1");

    public string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return _protector.Protect(secret);
    }

    public string Unprotect(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        return _protector.Unprotect(protectedSecret);
    }
}
