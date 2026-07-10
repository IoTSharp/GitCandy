
namespace GitCandy.Ssh.Messages.Connection
{
    [Message("SSH_MSG_CHANNEL_FAILURE", MessageNumber)]
    public class ChannelFailureMessage : ConnectionServiceMessage
    {
        private const byte MessageNumber = 100;

        public uint RecipientChannel { get; set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.Write(RecipientChannel);
        }
    }
}
