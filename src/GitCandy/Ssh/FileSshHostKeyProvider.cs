using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using GitCandy.Configuration;

namespace GitCandy.Ssh;

/// <summary>
/// 从独立文件加载 SSH host key；首次启动时生成并持久化 RSA key。
/// </summary>
public sealed class FileSshHostKeyProvider(
    IGitCandyApplicationPaths applicationPaths,
    ILogger<FileSshHostKeyProvider> logger) : ISshHostKeyProvider
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly IGitCandyApplicationPaths _applicationPaths = applicationPaths;
    private readonly ILogger<FileSshHostKeyProvider> _logger = logger;
    private IReadOnlyList<SshHostKey>? _cachedKeys;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SshHostKey>> GetHostKeysAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cachedKeys is not null)
        {
            return _cachedKeys;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedKeys is not null)
            {
                return _cachedKeys;
            }

            var keyPath = _applicationPaths.SshHostKeyPath;
            if (File.Exists(keyPath))
            {
                _cachedKeys = await ReadKeysAsync(keyPath, cancellationToken);
                _logger.LogInformation("Loaded built-in SSH host key from {SshHostKeyPath}.", keyPath);
                return _cachedKeys;
            }

            var legacyKeys = await TryReadLegacyKeysAsync(cancellationToken);
            _cachedKeys = legacyKeys.Count > 0 ? legacyKeys : [CreateRsaHostKey()];
            await WriteKeysAsync(keyPath, _cachedKeys, cancellationToken);

            _logger.LogInformation(
                legacyKeys.Count > 0
                    ? "Imported legacy built-in SSH host key to {SshHostKeyPath}."
                    : "Generated built-in SSH host key at {SshHostKeyPath}.",
                keyPath);
            return _cachedKeys;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<IReadOnlyList<SshHostKey>> TryReadLegacyKeysAsync(
        CancellationToken cancellationToken)
    {
        var legacyPath = _applicationPaths.UserConfigurationPath;
        if (!File.Exists(legacyPath)
            || string.Equals(
                Path.GetFullPath(legacyPath),
                Path.GetFullPath(_applicationPaths.SshHostKeyPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        try
        {
            return await ReadKeysAsync(legacyPath, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            _logger.LogWarning(
                exception,
                "Legacy user configuration at {LegacyConfigurationPath} does not contain a usable SSH host key.",
                legacyPath);
            return [];
        }
    }

    private static async Task<IReadOnlyList<SshHostKey>> ReadKeysAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);

        var keys = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "HostKey", StringComparison.OrdinalIgnoreCase))
            .Select(ParseHostKey)
            .Where(key => key is not null)
            .Cast<SshHostKey>()
            .Where(key => string.Equals(key.KeyType, "ssh-rsa", StringComparison.Ordinal))
            .ToArray();

        return keys.Length > 0
            ? keys
            : throw new InvalidDataException("The SSH host key file contains no supported RSA host key.");
    }

    private static SshHostKey? ParseHostKey(XElement element)
    {
        var keyType = element.Attribute("KeyType")?.Value
            ?? element.Elements().FirstOrDefault(child =>
                string.Equals(child.Name.LocalName, "KeyType", StringComparison.OrdinalIgnoreCase))?.Value;
        var keyXml = element.Attribute("KeyXml")?.Value
            ?? element.Elements().FirstOrDefault(child =>
                string.Equals(child.Name.LocalName, "KeyXml", StringComparison.OrdinalIgnoreCase))?.Value;

        return string.IsNullOrWhiteSpace(keyType) || string.IsNullOrWhiteSpace(keyXml)
            ? null
            : new SshHostKey(keyType.Trim(), keyXml.Trim());
    }

    private static SshHostKey CreateRsaHostKey()
    {
        using var rsa = RSA.Create(3072);
        return new SshHostKey("ssh-rsa", rsa.ToXmlString(includePrivateParameters: true));
    }

    private static async Task WriteKeysAsync(
        string path,
        IReadOnlyList<SshHostKey> keys,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The SSH host key path has no parent directory.");
        Directory.CreateDirectory(directory);

        var document = new XDocument(
            new XElement(
                "GitCandySshHostKeys",
                keys.Select(key => new XElement(
                    "HostKey",
                    new XAttribute("KeyType", key.KeyType),
                    new XElement("KeyXml", key.PrivateKeyXml)))));

        var fileOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 4096,
            Options = FileOptions.Asynchronous
        };
        if (!OperatingSystem.IsWindows())
        {
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = new FileStream(path, fileOptions);
        await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
    }
}
