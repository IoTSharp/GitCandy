
namespace GitCandy.Ssh.Messages.Connection
{
    [Message("SSH_MSG_CHANNEL_SUCCESS", MessageNumber)]
    public class ChannelSuccessMessage : ConnectionServiceMessage
    {
        private const byte MessageNumber = 99;

        public uint RecipientChannel { get; set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.Write(RecipientChannel);
        }
    }
}
