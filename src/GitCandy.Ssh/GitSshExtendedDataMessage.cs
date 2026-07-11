using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.IO;
using Microsoft.DevTunnels.Ssh.Messages;
using Microsoft.DevTunnels.Ssh.Services;
using SshBuffer = Microsoft.DevTunnels.Ssh.Buffer;

namespace GitCandy.Ssh;

internal sealed class GitSshExtendedDataMessage : ChannelMessage
{
    private const byte ExtendedDataMessageType = 95;

    public override byte MessageType => ExtendedDataMessageType;

    public uint DataTypeCode { get; set; }

    public SshBuffer Data { get; set; } = SshBuffer.From([]);

    protected override void OnRead(ref SshDataReader reader)
    {
        base.OnRead(ref reader);
        DataTypeCode = reader.ReadUInt32();
        Data = reader.ReadBinary();
    }

    protected override void OnWrite(ref SshDataWriter writer)
    {
        base.OnWrite(ref writer);
        writer.Write(DataTypeCode);
        writer.WriteBinary(Data);
    }
}

internal sealed class GitSshExtendedDataSender(SshSession session) : SshService(session)
{
    public Task SendAsync(
        GitSshExtendedDataMessage message,
        CancellationToken cancellationToken)
    {
        return SendMessageAsync(message, cancellationToken);
    }
}
