using System;

namespace GitCandy.Ssh.Messages
{
    [Message("SSH_MSG_NEWKEYS", MessageNumber)]
    public class NewKeysMessage : Message
    {
        private const byte MessageNumber = 21;

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnLoad(SshDataWorker reader)
        {
        }

        protected override void OnGetPacket(SshDataWorker writer)
        {
        }
    }
}
