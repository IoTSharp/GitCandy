using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitCandy.Git;

/// <summary>Git credential helper 的短生命周期子命令入口。</summary>
public static class RemoteGitCredentialHelperCommand
{
    /// <summary>GitCandy 主程序识别的 credential helper 子命令。</summary>
    public const string CommandName = "--git-remote-credential-helper";
    private const int MaxCredentialRequestCharacters = 16384;

    /// <summary>判断命令行是否请求 credential helper 模式。</summary>
    public static bool IsRequested(IReadOnlyList<string> arguments) =>
        arguments.Count > 0
        && string.Equals(arguments[0], CommandName, StringComparison.Ordinal);

    /// <summary>执行一次 Git credential helper 协议交互。</summary>
    public static async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (arguments.Count != 2)
        {
            return 1;
        }

        var operation = arguments[1];
        if (string.Equals(operation, "store", StringComparison.Ordinal)
            || string.Equals(operation, "erase", StringComparison.Ordinal))
        {
            return 0;
        }
        if (!string.Equals(operation, "get", StringComparison.Ordinal))
        {
            return 1;
        }

        try
        {
            var pipeName = Environment.GetEnvironmentVariable(RemoteCredentialPipeServer.PipeNameEnvironmentVariable);
            if (!RemoteCredentialPipeServer.IsValidPipeName(pipeName))
            {
                return 1;
            }

            var request = await ReadCredentialRequestAsync(input, cancellationToken);
            if (request is null)
            {
                return 1;
            }

            await using var pipe = new NamedPipeClientStream(
                ".",
                pipeName!,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            await RemoteCredentialPipeProtocol.WriteAsync(pipe, request, cancellationToken);
            var response = await RemoteCredentialPipeProtocol.ReadAsync<RemoteCredentialPipeResponse>(
                pipe,
                cancellationToken);
            if (response is null
                || !response.Accepted
                || ContainsProtocolControl(response.Username)
                || ContainsProtocolControl(response.Password))
            {
                return 1;
            }

            await output.WriteLineAsync($"username={response.Username}".AsMemory(), cancellationToken);
            await output.WriteLineAsync($"password={response.Password}".AsMemory(), cancellationToken);
            await output.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return 1;
        }
    }

    private static async Task<RemoteCredentialPipeRequest?> ReadCredentialRequestAsync(
        TextReader input,
        CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        var builder = new StringBuilder();
        int charactersRead;
        while ((charactersRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (builder.Length + charactersRead > MaxCredentialRequestCharacters)
            {
                return null;
            }

            builder.Append(buffer, 0, charactersRead);
        }

        string? protocol = null;
        string? host = null;
        string? path = null;
        foreach (var line in builder.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedLine = line.TrimEnd('\r');
            var separator = normalizedLine.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = normalizedLine[..separator];
            var value = normalizedLine[(separator + 1)..];
            if (ContainsProtocolControl(value))
            {
                return null;
            }

            switch (key)
            {
                case "protocol":
                    protocol = value;
                    break;
                case "host":
                    host = value;
                    break;
                case "path":
                    path = value;
                    break;
            }
        }

        return string.IsNullOrWhiteSpace(protocol)
            || string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(path)
            ? null
            : new RemoteCredentialPipeRequest(protocol, host, path);
    }

    private static bool ContainsProtocolControl(string value) =>
        value.Any(static character => character is '\r' or '\n' or '\0');
}

internal sealed class RemoteCredentialPipeServer : IAsyncDisposable
{
    internal const string PipeNameEnvironmentVariable = "GITCANDY_REMOTE_CREDENTIAL_PIPE";
    private readonly NamedPipeServerStream _pipe;
    private readonly Uri _remoteGitUrl;
    private readonly RemoteCredentialPipeResponse _response;

    public RemoteCredentialPipeServer(Uri remoteGitUrl, string username, string password)
    {
        ArgumentNullException.ThrowIfNull(remoteGitUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (username.Any(static character => character is '\r' or '\n' or '\0')
            || password.Any(static character => character is '\r' or '\n' or '\0')
            || Encoding.UTF8.GetByteCount(password) > RemoteCredentialPipeProtocol.MaxPayloadBytes / 2)
        {
            throw new RemoteRepositorySyncException(
                RemoteRepositorySyncErrorCodes.CredentialUnsupported,
                "The remote credential cannot be represented by the controlled credential helper.");
        }

        PipeName = $"gitcandy-remote-{Guid.NewGuid():N}";
        _remoteGitUrl = remoteGitUrl;
        _response = new RemoteCredentialPipeResponse(true, username, password);
        _pipe = new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    public string PipeName { get; }

    public async Task ServeAsync(CancellationToken cancellationToken)
    {
        await _pipe.WaitForConnectionAsync(cancellationToken);
        var request = await RemoteCredentialPipeProtocol.ReadAsync<RemoteCredentialPipeRequest>(
            _pipe,
            cancellationToken);
        var accepted = request is not null
            && string.Equals(request.Protocol, _remoteGitUrl.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.Host, _remoteGitUrl.Authority, StringComparison.OrdinalIgnoreCase)
            && PathsEqual(request.Path, _remoteGitUrl.AbsolutePath);
        var response = accepted
            ? _response
            : new RemoteCredentialPipeResponse(false, string.Empty, string.Empty);
        await RemoteCredentialPipeProtocol.WriteAsync(_pipe, response, cancellationToken);
    }

    public ValueTask DisposeAsync() => _pipe.DisposeAsync();

    internal static bool IsValidPipeName(string? pipeName) =>
        pipeName is not null
        && pipeName.Length == 48
        && pipeName.StartsWith("gitcandy-remote-", StringComparison.Ordinal)
        && pipeName[16..].All(Uri.IsHexDigit);

    private static bool PathsEqual(string requestedPath, string remotePath)
    {
        var requested = requestedPath.TrimStart('/');
        var remote = remotePath.TrimStart('/');
        if (string.Equals(requested, remote, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            return string.Equals(
                Uri.UnescapeDataString(requested),
                Uri.UnescapeDataString(remote),
                StringComparison.Ordinal);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }
}

internal sealed record RemoteCredentialPipeRequest(
    string Protocol,
    string Host,
    string Path);

internal sealed record RemoteCredentialPipeResponse(
    bool Accepted,
    string Username,
    string Password);

internal static class RemoteCredentialPipeProtocol
{
    internal const int MaxPayloadBytes = 65536;

    public static async Task WriteAsync<T>(
        Stream stream,
        T value,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            if (payload.Length > MaxPayloadBytes)
            {
                throw new InvalidDataException("The credential helper payload exceeded its limit.");
            }

            var length = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
            await stream.WriteAsync(length, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static async Task<T?> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is <= 0 or > MaxPayloadBytes)
        {
            throw new InvalidDataException("The credential helper payload length is invalid.");
        }

        var payload = new byte[length];
        try
        {
            await stream.ReadExactlyAsync(payload, cancellationToken);
            return JsonSerializer.Deserialize<T>(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}
