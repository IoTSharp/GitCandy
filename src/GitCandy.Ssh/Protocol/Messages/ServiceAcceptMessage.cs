using System;
using System.Text;

namespace GitCandy.Ssh.Messages
{
    [Message("SSH_MSG_SERVICE_ACCEPT", MessageNumber)]
    public class ServiceAcceptMessage : Message
    {
        private const byte MessageNumber = 6;

        public ServiceAcceptMessage(string name)
        {
            ServiceName = name;
        }

        public string ServiceName { get; private set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.Write(ServiceName, Encoding.ASCII);
        }
    }
}
