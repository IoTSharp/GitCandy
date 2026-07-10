using System.Net;

namespace GitCandy.Ssh
{
    public class StartingInfo
    {
        public const int DefaultPort = 22;

        public StartingInfo()
            : this(IPAddress.IPv6Any, DefaultPort)
        {
        }

        public StartingInfo(IPAddress localAddress, int port)
        {
            LocalAddress = localAddress;
            Port = port;
        }

        public IPAddress LocalAddress { get; private set; }
        public int Port { get; private set; }
    }
}
