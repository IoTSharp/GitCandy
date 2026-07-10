using System;

namespace GitCandy.Ssh.Messages
{
    [Message("SSH_MSG_KEXDH_REPLY", MessageNumber)]
    public class KeyExchangeDhReplyMessage : Message
    {
        private const byte MessageNumber = 31;

        public byte[] HostKey { get; set; }
        public byte[] F { get; set; }
        public byte[] Signature { get; set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.WriteBinary(HostKey);
            writer.WriteMpint(F);
            writer.WriteBinary(Signature);
        }
    }
}
