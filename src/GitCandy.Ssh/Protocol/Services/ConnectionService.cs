using GitCandy.Ssh.Messages;
using GitCandy.Ssh.Messages.Connection;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading;

namespace GitCandy.Ssh.Services
{
    public class ConnectionService : SshService
    {
        private readonly object _locker = new object();
        private readonly List<Channel> _channels = new List<Channel>();
        private readonly UserauthArgs _auth = null;

        private int _serverChannelCounter = -1;

        public ConnectionService(Session session, UserauthArgs auth)
            : base(session)
        {
            Contract.Requires(auth != null);

            _auth = auth;
        }

        public event EventHandler<SessionRequestedArgs> CommandOpened;

        protected internal override void CloseService()
        {
            lock (_locker)
                foreach (var channel in _channels.ToArray())
                {
                    channel.ForceClose();
                }
        }

        internal void HandleMessageCore(ConnectionServiceMessage message)
        {
            Contract.Requires(message != null);

            HandleMessage((dynamic)message);
        }

        private void HandleMessage(ChannelOpenMessage message)
        {
            switch (message.ChannelType)
            {
                case "session":
                    var msg = Message.LoadFrom<SessionOpenMessage>(message);
                    HandleMessage(msg);
                    break;
                default:
                    _session.SendMessage(new ChannelOpenFailureMessage
                    {
                        RecipientChannel = message.SenderChannel,
                        ReasonCode = ChannelOpenFailureReason.UnknownChannelType,
                        Description = string.Format("Unknown channel type: {0}.", message.ChannelType),
                    });
                    throw new SshConnectionException(string.Format("Unknown channel type: {0}.", message.ChannelType));
            }
        }

        private void HandleMessage(ChannelRequestMessage message)
        {
            switch (message.RequestType)
            {
                case "env":
                    var environmentMessage = Message.LoadFrom<EnvironmentRequestMessage>(message);
                    HandleMessage(environmentMessage);
                    break;
                case "exec":
                    var msg = Message.LoadFrom<CommandRequestMessage>(message);
                    HandleMessage(msg);
                    break;
                default:
                    if (message.WantReply)
                        _session.SendMessage(new ChannelFailureMessage
                        {
                            RecipientChannel = FindChannelByServerId<Channel>(message.RecipientChannel).ClientChannelId
                        });
                    throw new SshConnectionException(string.Format("Unknown request type: {0}.", message.RequestType));
            }
        }

        private void HandleMessage(EnvironmentRequestMessage message)
        {
            var channel = FindChannelByServerId<SessionChannel>(message.RecipientChannel);
            var accepted = string.Equals(message.VariableName, "GIT_PROTOCOL", StringComparison.Ordinal)
                && string.Equals(message.VariableValue, "version=2", StringComparison.Ordinal);
            if (accepted)
                channel.GitProtocolVersion = message.VariableValue;

            if (!message.WantReply)
                return;

            if (accepted)
                _session.SendMessage(new ChannelSuccessMessage
                {
                    RecipientChannel = channel.ClientChannelId
                });
            else
                _session.SendMessage(new ChannelFailureMessage
                {
                    RecipientChannel = channel.ClientChannelId
                });
        }

        private void HandleMessage(ChannelDataMessage message)
        {
            var channel = FindChannelByServerId<Channel>(message.RecipientChannel);
            channel.OnData(message.Data);
        }

        private void HandleMessage(ChannelWindowAdjustMessage message)
        {
            var channel = FindChannelByServerId<Channel>(message.RecipientChannel);
            channel.ClientAdjustWindow(message.BytesToAdd);
        }

        private void HandleMessage(ChannelEofMessage message)
        {
            var channel = FindChannelByServerId<Channel>(message.RecipientChannel);
            channel.OnEof();
        }

        private void HandleMessage(ChannelCloseMessage message)
        {
            var channel = FindChannelByServerId<Channel>(message.RecipientChannel);
            channel.OnClose();
        }

        private void HandleMessage(SessionOpenMessage message)
        {
            var channel = new SessionChannel(
                this,
                message.SenderChannel,
                message.InitialWindowSize,
                message.MaximumPacketSize,
                (uint)Interlocked.Increment(ref _serverChannelCounter));

            lock (_locker)
                _channels.Add(channel);

            var msg = new SessionOpenConfirmationMessage();
            msg.RecipientChannel = channel.ClientChannelId;
            msg.SenderChannel = channel.ServerChannelId;
            msg.InitialWindowSize = channel.ServerInitialWindowSize;
            msg.MaximumPacketSize = channel.ServerMaxPacketSize;

            _session.SendMessage(msg);
        }

        private void HandleMessage(CommandRequestMessage message)
        {
            var channel = FindChannelByServerId<SessionChannel>(message.RecipientChannel);

            if (message.WantReply)
                _session.SendMessage(new ChannelSuccessMessage { RecipientChannel = channel.ClientChannelId });

            if (CommandOpened != null)
            {
                var args = new SessionRequestedArgs(channel, message.Command, _auth);
                CommandOpened(this, args);
            }
        }

        private T FindChannelByClientId<T>(uint id) where T : Channel
        {
            lock (_locker)
            {
                var channel = _channels.FirstOrDefault(x => x.ClientChannelId == id) as T;
                if (channel == null)
                    throw new SshConnectionException(string.Format("Invalid client channel id {0}.", id),
                        DisconnectReason.ProtocolError);

                return channel;
            }
        }

        private T FindChannelByServerId<T>(uint id) where T : Channel
        {
            lock (_locker)
            {
                var channel = _channels.FirstOrDefault(x => x.ServerChannelId == id) as T;
                if (channel == null)
                    throw new SshConnectionException(string.Format("Invalid server channel id {0}.", id),
                        DisconnectReason.ProtocolError);

                return channel;
            }
        }

        internal void RemoveChannel(Channel channel)
        {
            lock (_locker)
            {
                _channels.Remove(channel);
            }
        }
    }
}
