using System;

namespace GitCandy.Ssh.Messages
{
    [Message("SSH_MSG_KEXDH_INIT", MessageNumber)]
    public class KeyExchangeDhInitMessage : Message
    {
        private const byte MessageNumber = 30;

        public byte[] E { get; private set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnLoad(SshDataWorker reader)
        {
            E = reader.ReadMpint();
        }
    }
}
