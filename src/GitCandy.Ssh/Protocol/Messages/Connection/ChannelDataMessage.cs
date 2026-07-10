
namespace GitCandy.Ssh.Messages.Connection
{
    [Message("SSH_MSG_CHANNEL_DATA", MessageNumber)]
    public class ChannelDataMessage : ConnectionServiceMessage
    {
        private const byte MessageNumber = 94;

        public uint RecipientChannel { get; set; }
        public byte[] Data { get; set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnLoad(SshDataWorker reader)
        {
            RecipientChannel = reader.ReadUInt32();
            Data = reader.ReadBinary();
        }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.Write(RecipientChannel);
            writer.WriteBinary(Data);
        }
    }
}
