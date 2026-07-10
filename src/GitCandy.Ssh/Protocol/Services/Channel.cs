using GitCandy.Ssh.Messages.Connection;
using System;
using System.Diagnostics.Contracts;
using System.Threading;

namespace GitCandy.Ssh.Services
{
    public abstract class Channel
    {
        protected ConnectionService _connectionService;
        protected EventWaitHandle _sendingWindowWaitHandle = new ManualResetEvent(false);

        public Channel(ConnectionService connectionService,
            uint clientChannelId, uint clientInitialWindowSize, uint clientMaxPacketSize,
            uint serverChannelId)
        {
            Contract.Requires(connectionService != null);

            _connectionService = connectionService;

            ClientChannelId = clientChannelId;
            ClientInitialWindowSize = clientInitialWindowSize;
            ClientWindowSize = clientInitialWindowSize;
            ClientMaxPacketSize = clientMaxPacketSize;

            ServerChannelId = serverChannelId;
            ServerInitialWindowSize = Session.InitialLocalWindowSize;
            ServerWindowSize = Session.InitialLocalWindowSize;
            ServerMaxPacketSize = Session.LocalChannelDataPacketSize;
        }

        public uint ClientChannelId { get; private set; }
        public uint ClientInitialWindowSize { get; private set; }
        public uint ClientWindowSize { get; protected set; }
        public uint ClientMaxPacketSize { get; private set; }

        public uint ServerChannelId { get; private set; }
        public uint ServerInitialWindowSize { get; private set; }
        public uint ServerWindowSize { get; protected set; }
        public uint ServerMaxPacketSize { get; private set; }

        public bool ClientClosed { get; private set; }
        public bool ClientMarkedEof { get; private set; }
        public bool ServerClosed { get; private set; }
        public bool ServerMarkedEof { get; private set; }

        public event EventHandler<byte[]> DataReceived;
        public event EventHandler EofReceived;
        public event EventHandler CloseReceived;

        public void SendData(byte[] data)
        {
            SendData(data, CancellationToken.None);
        }

        public void SendData(byte[] data, CancellationToken cancellationToken)
        {
            Contract.Requires(data != null);

            var msg = new ChannelDataMessage();
            msg.RecipientChannel = ClientChannelId;

            var total = (uint)data.Length;
            var offset = 0L;
            byte[] buf = null;
            do
            {
                var packetSize = Math.Min(Math.Min(ClientWindowSize, ClientMaxPacketSize), total);
                if (packetSize == 0)
                {
                    _ = WaitHandle.WaitAny(new[]
                    {
                        _sendingWindowWaitHandle,
                        cancellationToken.WaitHandle
                    });
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                if (buf == null || packetSize != buf.Length)
                    buf = new byte[packetSize];
                Array.Copy(data, offset, buf, 0, packetSize);

                msg.Data = buf;
                _connectionService._session.SendMessage(msg);

                ClientWindowSize -= packetSize;
                total -= packetSize;
                offset += packetSize;
            } while (total > 0);
        }

        public void SendEof()
        {
            if (ServerMarkedEof)
                return;

            ServerMarkedEof = true;
            var msg = new ChannelEofMessage { RecipientChannel = ClientChannelId };
            _connectionService._session.SendMessage(msg);
        }

        public void SendClose(uint? exitCode = null)
        {
            if (ServerClosed)
                return;

            ServerClosed = true;
            if (exitCode.HasValue)
                _connectionService._session.SendMessage(new ExitStatusMessage { RecipientChannel = ClientChannelId, ExitStatus = exitCode.Value });
            _connectionService._session.SendMessage(new ChannelCloseMessage { RecipientChannel = ClientChannelId });

            CheckBothClosed();
        }

        internal void OnData(byte[] data)
        {
            Contract.Requires(data != null);

            ServerAttemptAdjustWindow((uint)data.Length);

            if (DataReceived != null)
                DataReceived(this, data);
        }

        internal void OnEof()
        {
            ClientMarkedEof = true;

            if (EofReceived != null)
                EofReceived(this, EventArgs.Empty);
        }

        internal void OnClose()
        {
            ClientClosed = true;

            if (CloseReceived != null)
                CloseReceived(this, EventArgs.Empty);

            CheckBothClosed();
        }

        internal void ClientAdjustWindow(uint bytesToAdd)
        {
            ClientWindowSize += bytesToAdd;

            // pulse multithreadings in same time and unsignal until thread switched
            // don't try to use AutoResetEvent
            _sendingWindowWaitHandle.Set();
            Thread.Sleep(1);
            _sendingWindowWaitHandle.Reset();
        }

        private void ServerAttemptAdjustWindow(uint messageLength)
        {
            ServerWindowSize -= messageLength;
            if (ServerWindowSize <= ServerMaxPacketSize)
            {
                _connectionService._session.SendMessage(new ChannelWindowAdjustMessage
                {
                    RecipientChannel = ClientChannelId,
                    BytesToAdd = ServerInitialWindowSize - ServerWindowSize
                });
                ServerWindowSize = ServerInitialWindowSize;
            }
        }

        private void CheckBothClosed()
        {
            if (ClientClosed && ServerClosed)
            {
                ForceClose();
            }
        }

        internal void ForceClose()
        {
            _connectionService.RemoveChannel(this);
            _sendingWindowWaitHandle.Set();
            _sendingWindowWaitHandle.Close();
        }
    }
}
