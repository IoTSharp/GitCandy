using System;
using System.Text;

namespace GitCandy.Ssh.Messages.Userauth
{
    [Message("SSH_MSG_USERAUTH_REQUEST", MessageNumber)]
    public class RequestMessage : UserauthServiceMessage
    {
        protected const byte MessageNumber = 50;

        public string Username { get; protected set; }
        public string ServiceName { get; protected set; }
        public string MethodName { get; protected set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnLoad(SshDataWorker reader)
        {
            Username = reader.ReadString(Encoding.UTF8);
            ServiceName = reader.ReadString(Encoding.ASCII);
            MethodName = reader.ReadString(Encoding.ASCII);
        }
    }
}
