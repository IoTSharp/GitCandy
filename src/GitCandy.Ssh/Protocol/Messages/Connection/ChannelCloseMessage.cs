
namespace GitCandy.Ssh.Messages.Connection
{
    [Message("SSH_MSG_CHANNEL_CLOSE", MessageNumber)]
    public class ChannelCloseMessage : ConnectionServiceMessage
    {
        private const byte MessageNumber = 97;

        public uint RecipientChannel { get; set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnLoad(SshDataWorker reader)
        {
            RecipientChannel = reader.ReadUInt32();
        }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.Write(RecipientChannel);
        }
    }
}
