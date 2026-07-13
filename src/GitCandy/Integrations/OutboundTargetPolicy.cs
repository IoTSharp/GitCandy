using System.Net;
using System.Net.Sockets;
using GitCandy.Configuration;
using GitCandy.Integrations;
using Microsoft.Extensions.Options;

namespace GitCandy.Web.Integrations;

/// <summary>在保存 URL 与实际 socket 连接时阻止不允许的协议和内网地址。</summary>
public sealed class OutboundTargetPolicy(IOptions<WebhookOptions> options) : IOutboundTargetPolicy
{
    private readonly WebhookOptions _options = options.Value;

    public async ValueTask<bool> IsAllowedAsync(
        Uri target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsAbsoluteUri
            || string.IsNullOrWhiteSpace(target.Host)
            || !string.IsNullOrEmpty(target.UserInfo)
            || !string.IsNullOrEmpty(target.Fragment)
            || (!string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && (!_options.AllowHttpTargets
                    || !string.Equals(target.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        try
        {
            var addresses = await ResolveAsync(target.DnsSafeHost, cancellationToken);
            return addresses.Length > 0 && addresses.All(IsAllowedAddress);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return false;
        }
    }

    public async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var addresses = await ResolveAsync(endpoint.Host, cancellationToken);
        var allowed = addresses.Where(IsAllowedAddress).ToArray();
        if (allowed.Length == 0 || (!_options.AllowPrivateNetworkTargets && allowed.Length != addresses.Length))
        {
            throw new HttpRequestException("The webhook target is blocked by the outbound network policy.");
        }

        Exception? lastError = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (exception is OperationCanceledException) throw;
                lastError = exception;
            }
        }
        throw new HttpRequestException("The webhook target could not be reached.", lastError);
    }

    private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address)) return [address];
        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }

    private bool IsAllowedAddress(IPAddress address)
    {
        if (_options.AllowPrivateNetworkTargets) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast)
        {
            return false;
        }
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xfe) != 0xfc;
        }
        return bytes[0] switch
        {
            0 or 10 or 127 => false,
            100 when bytes[1] is >= 64 and <= 127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] is >= 16 and <= 31 => false,
            192 when bytes[1] == 0 => false,
            192 when bytes[1] == 168 => false,
            198 when bytes[1] is 18 or 19 or 51 => false,
            203 when bytes[1] == 0 && bytes[2] == 113 => false,
            >= 224 => false,
            _ => true
        };
    }
}
