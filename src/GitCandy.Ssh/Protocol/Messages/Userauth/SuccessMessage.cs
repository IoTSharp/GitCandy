using System;

namespace GitCandy.Ssh.Messages.Userauth
{
    [Message("SSH_MSG_USERAUTH_SUCCESS", MessageNumber)]
    public class SuccessMessage : UserauthServiceMessage
    {
        private const byte MessageNumber = 52;

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnGetPacket(SshDataWorker writer)
        {
        }
    }
}
