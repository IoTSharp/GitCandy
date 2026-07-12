using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Configuration;
using GitCandy.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

namespace GitCandy.IdentityServices;

/// <summary>对账号恢复请求执行不保存原始标识的进程级限流。</summary>
public interface IAccountRecoveryThrottle
{
    /// <summary>尝试消费指定分区的一次请求额度。</summary>
    bool TryAcquire(string partition);
}

internal sealed class AccountRecoveryThrottle(
    IOptions<GitCandyIdentityOptions> options,
    TimeProvider timeProvider,
    IMemoryCache memoryCache) : IAccountRecoveryThrottle
{
    private readonly object[] _locks = Enumerable.Range(0, 64).Select(static _ => new object()).ToArray();
    private readonly GitCandyAccountRecoveryOptions _options = options.Value.AccountRecovery;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly IMemoryCache _memoryCache = memoryCache;

    public bool TryAcquire(string partition)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(partition)));
        var lockIndex = (StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % _locks.Length;
        lock (_locks[lockIndex])
        {
            var now = _timeProvider.GetUtcNow();
            var cutoff = now - _options.RequestWindow;
            if (!_memoryCache.TryGetValue(key, out Queue<DateTimeOffset>? requests) || requests is null)
            {
                requests = new Queue<DateTimeOffset>();
            }
            while (requests.TryPeek(out var timestamp) && timestamp <= cutoff)
            {
                requests.Dequeue();
            }

            if (requests.Count >= _options.MaxRequestsPerWindow)
            {
                return false;
            }

            requests.Enqueue(now);
            _memoryCache.Set(key, requests, now + _options.RequestWindow);
            return true;
        }
    }
}

internal sealed class SmtpAccountEmailSender(
    IOptions<GitCandyIdentityOptions> options,
    ILogger<SmtpAccountEmailSender> logger) : IAccountEmailSender
{
    private readonly GitCandySmtpOptions _options = options.Value.AccountRecovery.Smtp;
    private readonly ILogger<SmtpAccountEmailSender> _logger = logger;

    public async Task<bool> SendActionLinkAsync(
        string recipient,
        string subject,
        Uri actionLink,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host)
            || string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            _logger.LogWarning("Account email delivery is disabled because SMTP is not configured.");
            return false;
        }

        using var message = new MailMessage(_options.FromAddress, recipient, subject,
            $"Open this one-time link to continue:\r\n\r\n{actionLink.AbsoluteUri}\r\n\r\nIf you did not request this action, ignore this message.");
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl
        };
        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        try
        {
            await client.SendMailAsync(message, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException)
        {
            _logger.LogError(exception, "Account email delivery failed.");
            return false;
        }
    }
}
