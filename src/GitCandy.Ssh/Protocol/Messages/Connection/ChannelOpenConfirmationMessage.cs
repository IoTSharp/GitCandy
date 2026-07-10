
namespace GitCandy.Ssh.Messages.Connection
{
    [Message("SSH_MSG_CHANNEL_OPEN_CONFIRMATION", MessageNumber)]
    public class ChannelOpenConfirmationMessage : ConnectionServiceMessage
    {
        private const byte MessageNumber = 91;

        public uint RecipientChannel { get; set; }
        public uint SenderChannel { get; set; }
        public uint InitialWindowSize { get; set; }
        public uint MaximumPacketSize { get; set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.Write(RecipientChannel);
            writer.Write(SenderChannel);
            writer.Write(InitialWindowSize);
            writer.Write(MaximumPacketSize);
        }
    }
}
