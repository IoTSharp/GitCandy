using System.Text;
using Microsoft.DevTunnels.Ssh.IO;
using Microsoft.DevTunnels.Ssh.Messages;

namespace GitCandy.Ssh;

internal sealed class GitEnvironmentRequestMessage : ChannelRequestMessage
{
    public const string EnvironmentRequestType = "env";

    public string? VariableName { get; private set; }

    public string? VariableValue { get; private set; }

    protected override void OnRead(ref SshDataReader reader)
    {
        base.OnRead(ref reader);
        VariableName = reader.ReadString(Encoding.UTF8);
        VariableValue = reader.ReadString(Encoding.UTF8);
    }
}
