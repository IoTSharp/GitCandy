using System;

namespace GitCandy.Ssh.Messages
{
    [Message("SSH_MSG_UNIMPLEMENTED", MessageNumber)]
    public class UnimplementedMessage : Message
    {
        private const byte MessageNumber = 3;

        public uint SequenceNumber { get; set; }

        public byte UnimplementedMessageType { get; set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.Write(MessageNumber);
            writer.Write(SequenceNumber);
        }
    }
}
