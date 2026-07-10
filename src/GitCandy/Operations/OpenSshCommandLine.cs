using GitCandy.Configuration;
using GitCandy.Ssh;
using Microsoft.Extensions.Options;

namespace GitCandy.Operations;

internal static class OpenSshCommandLine
{
    private const string AuthorizedKeyOption = "--openssh-authorized-key";
    private const string ForcedCommandOption = "--openssh-forced-command";

    public static bool IsRequested(string[] args)
    {
        return args.Length > 0
            && (string.Equals(args[0], AuthorizedKeyOption, StringComparison.Ordinal)
                || string.Equals(args[0], ForcedCommandOption, StringComparison.Ordinal));
    }

    public static bool TryParse(string[] args, out OpenSshInvocation? invocation)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            invocation = null;
            return false;
        }

        var mode = args[0] switch
        {
            AuthorizedKeyOption => OpenSshInvocationMode.AuthorizedKey,
            ForcedCommandOption => OpenSshInvocationMode.ForcedCommand,
            _ => (OpenSshInvocationMode?)null
        };
        invocation = mode is null
            ? null
            : new OpenSshInvocation(mode.Value, args[1]);
        return invocation is not null;
    }

    public static async Task<int> ExecuteAsync(
        OpenSshInvocation invocation,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(services);

        var options = services.GetRequiredService<IOptions<GitCandyOpenSshOptions>>().Value;
        if (!options.Enabled)
        {
            return 1;
        }

        var adapter = services.GetRequiredService<IOpenSshAdapter>();
        return invocation.Mode switch
        {
            OpenSshInvocationMode.AuthorizedKey => await adapter.WriteAuthorizedKeyAsync(
                invocation.Fingerprint,
                Console.Out,
                cancellationToken),
            OpenSshInvocationMode.ForcedCommand => await adapter.ExecuteForcedCommandAsync(
                invocation.Fingerprint,
                Environment.GetEnvironmentVariable("SSH_ORIGINAL_COMMAND"),
                Environment.GetEnvironmentVariable("GIT_PROTOCOL"),
                Console.OpenStandardInput(),
                Console.OpenStandardOutput(),
                Console.Error,
                cancellationToken),
            _ => 2
        };
    }
}

internal sealed record OpenSshInvocation(OpenSshInvocationMode Mode, string Fingerprint);

internal enum OpenSshInvocationMode
{
    AuthorizedKey,
    ForcedCommand
}
