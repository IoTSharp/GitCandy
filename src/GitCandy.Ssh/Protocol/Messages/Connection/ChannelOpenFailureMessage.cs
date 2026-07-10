using System.Text;

namespace GitCandy.Ssh.Messages.Connection
{
    [Message("SSH_MSG_CHANNEL_OPEN_FAILURE", MessageNumber)]
    public class ChannelOpenFailureMessage : ConnectionServiceMessage
    {
        private const byte MessageNumber = 92;

        public uint RecipientChannel { get; set; }
        public ChannelOpenFailureReason ReasonCode { get; set; }
        public string Description { get; set; }
        public string Language { get; set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.Write(RecipientChannel);
            writer.Write((uint)ReasonCode);
            writer.Write(Description, Encoding.ASCII);
            writer.Write(Language ?? "en", Encoding.ASCII);
        }
    }
}
