using System;
using System.Text;

namespace GitCandy.Ssh.Messages.Userauth
{
    [Message("SSH_MSG_USERAUTH_PK_OK", MessageNumber)]
    public class PublicKeyOkMessage : UserauthServiceMessage
    {
        private const byte MessageNumber = 60;

        public string KeyAlgorithmName { get; set; }
        public byte[] PublicKey { get; set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.Write(KeyAlgorithmName, Encoding.ASCII);
            writer.WriteBinary(PublicKey);
        }
    }
}
