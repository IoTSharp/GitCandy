using System;

namespace GitCandy.Ssh
{
    public class SshConnectionException : Exception
    {
        public SshConnectionException()
        {
        }

        public SshConnectionException(string message, DisconnectReason disconnectReason = DisconnectReason.None)
            : base(message)
        {
            DisconnectReason = disconnectReason;
        }

        public DisconnectReason DisconnectReason { get; private set; }

        public override string ToString()
        {
            return string.Format("SSH connection disconnected bacause {0}: {1}");
        }
    }
}
