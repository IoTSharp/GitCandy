using System.Diagnostics.Contracts;

namespace GitCandy.Ssh.Services
{
    public class SessionRequestedArgs
    {
        public SessionRequestedArgs(SessionChannel channel, string command, UserauthArgs userauthArgs)
        {
            Contract.Requires(channel != null);
            Contract.Requires(command != null);
            Contract.Requires(userauthArgs != null);

            Channel = channel;
            CommandText = command;
            AttachedUserauthArgs = userauthArgs;
        }

        public SessionChannel Channel { get; private set; }
        public string CommandText { get; private set; }
        public UserauthArgs AttachedUserauthArgs { get; private set; }
    }
}
