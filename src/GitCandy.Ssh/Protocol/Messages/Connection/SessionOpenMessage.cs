using System;

namespace GitCandy.Ssh.Messages.Connection
{
    public class SessionOpenMessage : ChannelOpenMessage
    {
        protected override void OnLoad(SshDataWorker reader)
        {
            base.OnLoad(reader);

            if (ChannelType != "session")
                throw new ArgumentException(string.Format("Channel type {0} is not valid.", ChannelType));
        }
    }
}
