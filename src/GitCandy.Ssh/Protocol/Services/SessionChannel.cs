
namespace GitCandy.Ssh.Services
{
    public class SessionChannel : Channel
    {
        public SessionChannel(ConnectionService connectionService,
            uint clientChannelId, uint clientInitialWindowSize, uint clientMaxPacketSize,
            uint serverChannelId)
            : base(connectionService, clientChannelId, clientInitialWindowSize, clientMaxPacketSize, serverChannelId)
        {

        }

        public string GitProtocolVersion { get; internal set; }
    }
}
