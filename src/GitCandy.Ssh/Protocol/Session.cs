using GitCandy.Ssh.Algorithms;
using GitCandy.Ssh.Messages;
using GitCandy.Ssh.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace GitCandy.Ssh
{
    public class Session
    {
        private const byte CarriageReturn = 0x0d;
        private const byte LineFeed = 0x0a;
        internal const int MaximumSshPacketSize = LocalChannelDataPacketSize;
        internal const int InitialLocalWindowSize = LocalChannelDataPacketSize * 32;
        internal const int LocalChannelDataPacketSize = 1024 * 32;

        private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        private static readonly Dictionary<byte, Type> _messagesMetadata;
        internal static readonly Dictionary<string, Func<KexAlgorithm>> _keyExchangeAlgorithms =
            new Dictionary<string, Func<KexAlgorithm>>();
        internal static readonly Dictionary<string, Func<string, PublicKeyAlgorithm>> _publicKeyAlgorithms =
            new Dictionary<string, Func<string, PublicKeyAlgorithm>>();
        internal static readonly Dictionary<string, Func<CipherInfo>> _encryptionAlgorithms =
            new Dictionary<string, Func<CipherInfo>>();
        internal static readonly Dictionary<string, Func<HmacInfo>> _hmacAlgorithms =
            new Dictionary<string, Func<HmacInfo>>();
        internal static readonly Dictionary<string, Func<CompressionAlgorithm>> _compressionAlgorithms =
            new Dictionary<string, Func<CompressionAlgorithm>>();

        private readonly object _locker = new object();
        private readonly Socket _socket;
#if DEBUG
        private readonly TimeSpan _timeout = TimeSpan.FromDays(1);
#else
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
#endif
        private readonly Dictionary<string, string> _hostKey;

        private uint _outboundPacketSequence;
        private uint _inboundPacketSequence;
        private uint _outboundFlow;
        private uint _inboundFlow;
        private Algorithms _algorithms = null;
        private ExchangeContext _exchangeContext = null;
        private List<SshService> _services = new List<SshService>();
        private ConcurrentQueue<Message> _blockedMessages = new ConcurrentQueue<Message>();
        private EventWaitHandle _hasBlockedMessagesWaitHandle = new ManualResetEvent(true);

        public string ServerVersion { get; private set; }
        public string ClientVersion { get; private set; }
        public byte[] SessionId { get; private set; }
        public T GetService<T>() where T : SshService
        {
            return (T)_services.FirstOrDefault(x => x is T);
        }

        static Session()
        {
            _keyExchangeAlgorithms.Add("diffie-hellman-group14-sha1", () => new DiffieHellmanGroupSha1(new DiffieHellman(2048)));

            _publicKeyAlgorithms.Add("ssh-rsa", x => new RsaKey(x));

            _encryptionAlgorithms.Add("aes128-ctr", () => new CipherInfo(Aes.Create(), 128, CipherModeEx.CTR));
            _encryptionAlgorithms.Add("aes192-ctr", () => new CipherInfo(Aes.Create(), 192, CipherModeEx.CTR));
            _encryptionAlgorithms.Add("aes256-ctr", () => new CipherInfo(Aes.Create(), 256, CipherModeEx.CTR));

            _hmacAlgorithms.Add("hmac-sha1", () => new HmacInfo(new HMACSHA1(), 160));

            _compressionAlgorithms.Add("none", () => new NoCompression());

            _messagesMetadata = (from t in typeof(Message).Assembly.GetTypes()
                                 let attrib = (MessageAttribute)t.GetCustomAttributes(typeof(MessageAttribute), false).FirstOrDefault()
                                 where attrib != null
                                 select new { attrib.Number, Type = t })
                                 .ToDictionary(x => x.Number, x => x.Type);
        }

        public Session(Socket socket, Dictionary<string, string> hostKey)
        {
            Contract.Requires(socket != null);
            Contract.Requires(hostKey != null);

            _socket = socket;
            _hostKey = hostKey.ToDictionary(s => s.Key, s => s.Value);
            ServerVersion = "SSH-2.0-FxSsh";
        }

        public event EventHandler<EventArgs> Disconnected;

        public event EventHandler<SshService> ServiceRegistered;

        internal void EstablishConnection()
        {
            SetSocketOptions();

            SocketWriteProtocolVersion();
            ClientVersion = SocketReadProtocolVersion();
            if (!Regex.IsMatch(ClientVersion, "SSH-2.0-.+"))
            {
                throw new SshConnectionException(
                    string.Format("Not supported for client SSH version {0}. This server only supports SSH v2.0.", ClientVersion),
                    DisconnectReason.ProtocolVersionNotSupported);
            }

            ConsiderReExchange(true);

            try
            {
                while (_socket != null && _socket.Connected)
                {
                    var message = ReceiveMessage();
                    HandleMessageCore(message);
                }
            }
            finally
            {
                foreach (var service in _services)
                {
                    service.CloseService();
                }
            }
        }

        public void Disconnect()
        {
            Disconnect(DisconnectReason.ByApplication, "Connection terminated by the server.");
        }

        public void Disconnect(DisconnectReason reason, string description)
        {
            if (reason == DisconnectReason.ByApplication)
            {
                var message = new DisconnectMessage(reason, description);
                TrySendMessage(message);
            }

            try
            {
                _socket.Disconnect(true);
                _socket.Dispose();
            }
            catch { }

            if (Disconnected != null)
                Disconnected(this, EventArgs.Empty);
        }

        #region Socket operations
        private void SetSocketOptions()
        {
            const int socketBufferSize = 2 * MaximumSshPacketSize;
            _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, socketBufferSize);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, socketBufferSize);
        }

        private string SocketReadProtocolVersion()
        {
            // http://tools.ietf.org/html/rfc4253#section-4.2
            var buffer = new byte[255];
            var dummy = new byte[255];
            var pos = 0;
            var len = 0;

            while (pos < buffer.Length)
            {
                var ar = _socket.BeginReceive(buffer, pos, buffer.Length - pos, SocketFlags.Peek, null, null);
                WaitHandle(ar);
                len = _socket.EndReceive(ar);

                for (var i = 0; i < len; i++, pos++)
                {
                    if (pos > 0 && buffer[pos - 1] == CarriageReturn && buffer[pos] == LineFeed)
                    {
                        _socket.Receive(dummy, 0, i + 1, SocketFlags.None);
                        return Encoding.ASCII.GetString(buffer, 0, pos - 1);
                    }
                }
                _socket.Receive(dummy, 0, len, SocketFlags.None);
            }
            throw new SshConnectionException("Could't read the protocal version", DisconnectReason.ProtocolError);
        }

        private void SocketWriteProtocolVersion()
        {
            SocketWrite(Encoding.ASCII.GetBytes(ServerVersion + "\r\n"));
        }

        private byte[] SocketRead(int length)
        {
            var pos = 0;
            var buffer = new byte[length];

            while (pos < length)
            {
                try
                {
                    var ar = _socket.BeginReceive(buffer, pos, length - pos, SocketFlags.None, null, null);
                    WaitHandle(ar);
                    var len = _socket.EndReceive(ar);
                    if (len == 0 && _socket.Available == 0)
                        Thread.Sleep(50);

                    pos += len;
                }
                catch (SocketException exp)
                {
                    if (exp.SocketErrorCode == SocketError.WouldBlock ||
                        exp.SocketErrorCode == SocketError.IOPending ||
                        exp.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
                    {
                        Thread.Sleep(30);
                    }
                    else
                        throw new SshConnectionException("Connection lost", DisconnectReason.ConnectionLost);
                }
            }

            return buffer;
        }

        private void SocketWrite(byte[] data)
        {
            var pos = 0;
            var length = data.Length;

            while (pos < length)
            {
                try
                {
                    var ar = _socket.BeginSend(data, pos, length - pos, SocketFlags.None, null, null);
                    WaitHandle(ar);
                    pos += _socket.EndSend(ar);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.WouldBlock ||
                        ex.SocketErrorCode == SocketError.IOPending ||
                        ex.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
                    {
                        Thread.Sleep(30);
                    }
                    else
                        throw new SshConnectionException("Connection lost", DisconnectReason.ConnectionLost);
                }
            }
        }

        private void WaitHandle(IAsyncResult ar)
        {
            if (!ar.AsyncWaitHandle.WaitOne(_timeout))
                throw new SshConnectionException(string.Format("Socket operation has timed out after {0:F0} milliseconds.",
                    _timeout.TotalMilliseconds),
                    DisconnectReason.ConnectionLost);
        }
        #endregion

        #region Message operations
        private Message ReceiveMessage()
        {
            var useAlg = _algorithms != null;

            var blockSize = (byte)(useAlg ? Math.Max(8, _algorithms.ClientEncryption.BlockBytesSize) : 8);
            var firstBlock = SocketRead(blockSize);
            if (useAlg)
                firstBlock = _algorithms.ClientEncryption.Transform(firstBlock);

            var packetLength = firstBlock[0] << 24 | firstBlock[1] << 16 | firstBlock[2] << 8 | firstBlock[3];
            var paddingLength = firstBlock[4];
            var bytesToRead = packetLength - blockSize + 4;

            var followingBlocks = SocketRead(bytesToRead);
            if (useAlg)
                followingBlocks = _algorithms.ClientEncryption.Transform(followingBlocks);

            var fullPacket = firstBlock.Concat(followingBlocks).ToArray();
            var data = fullPacket.Skip(5).Take(packetLength - paddingLength).ToArray();
            if (useAlg)
            {
                var clientMac = SocketRead(_algorithms.ClientHmac.DigestLength);
                var mac = ComputeHmac(_algorithms.ClientHmac, fullPacket, _inboundPacketSequence);
                if (!clientMac.SequenceEqual(mac))
                {
                    throw new SshConnectionException("Invalid MAC", DisconnectReason.MacError);
                }

                data = _algorithms.ClientCompression.Decompress(data);
            }

            var typeNumber = data[0];
            var implemented = _messagesMetadata.ContainsKey(typeNumber);
            var message = implemented
                ? (Message)Activator.CreateInstance(_messagesMetadata[typeNumber])
                : new UnimplementedMessage { SequenceNumber = _inboundPacketSequence, UnimplementedMessageType = typeNumber };

            if (implemented)
                message.Load(data);

            lock (_locker)
            {
                _inboundPacketSequence++;
                _inboundFlow += (uint)packetLength;
            }

            ConsiderReExchange();

            return message;
        }

        internal void SendMessage(Message message)
        {
            Contract.Requires(message != null);

            if (_exchangeContext != null
                && message.MessageType > 4 && (message.MessageType < 20 || message.MessageType > 49))
            {
                _blockedMessages.Enqueue(message);
                return;
            }

            _hasBlockedMessagesWaitHandle.WaitOne();
            SendMessageInternal(message);
        }

        private void SendMessageInternal(Message message)
        {
            var useAlg = _algorithms != null;

            var blockSize = (byte)(useAlg ? Math.Max(8, _algorithms.ServerEncryption.BlockBytesSize) : 8);
            var payload = message.GetPacket();
            if (useAlg)
                payload = _algorithms.ServerCompression.Compress(payload);

            // http://tools.ietf.org/html/rfc4253
            // 6.  Binary Packet Protocol
            // the total length of (packet_length || padding_length || payload || padding)
            // is a multiple of the cipher block size or 8,
            // padding length must between 4 and 255 bytes.
            var paddingLength = (byte)(blockSize - (payload.Length + 5) % blockSize);
            if (paddingLength < 4)
                paddingLength += blockSize;

            var packetLength = (uint)payload.Length + paddingLength + 1;

            var padding = new byte[paddingLength];
            _rng.GetBytes(padding);

            using (var worker = new SshDataWorker())
            {
                worker.Write(packetLength);
                worker.Write(paddingLength);
                worker.Write(payload);
                worker.Write(padding);

                payload = worker.ToByteArray();
            }

            if (useAlg)
            {
                var mac = ComputeHmac(_algorithms.ServerHmac, payload, _outboundPacketSequence);
                payload = _algorithms.ServerEncryption.Transform(payload).Concat(mac).ToArray();
            }

            SocketWrite(payload);

            lock (_locker)
            {
                _outboundPacketSequence++;
                _outboundFlow += packetLength;
            }

            ConsiderReExchange();
        }

        private void ConsiderReExchange(bool force = false)
        {
            var kex = false;
            lock (_locker)
                if (_exchangeContext == null
                    && (force || _inboundFlow + _outboundFlow > 1024 * 1024 * 512)) // 0.5 GiB
                {
                    _exchangeContext = new ExchangeContext();
                    kex = true;
                }

            if (kex)
            {
                var kexInitMessage = LoadKexInitMessage();
                _exchangeContext.ServerKexInitPayload = kexInitMessage.GetPacket();

                SendMessage(kexInitMessage);
            }
        }

        private void ContinueSendBlockedMessages()
        {
            if (_blockedMessages.Count > 0)
            {
                Message message;
                while (_blockedMessages.TryDequeue(out message))
                {
                    SendMessageInternal(message);
                }
            }
        }

        internal bool TrySendMessage(Message message)
        {
            Contract.Requires(message != null);

            try
            {
                SendMessage(message);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Message LoadKexInitMessage()
        {
            var message = new KeyExchangeInitMessage();
            message.KeyExchangeAlgorithms = _keyExchangeAlgorithms.Keys.ToArray();
            message.ServerHostKeyAlgorithms = _publicKeyAlgorithms.Keys.ToArray();
            message.EncryptionAlgorithmsClientToServer = _encryptionAlgorithms.Keys.ToArray();
            message.EncryptionAlgorithmsServerToClient = _encryptionAlgorithms.Keys.ToArray();
            message.MacAlgorithmsClientToServer = _hmacAlgorithms.Keys.ToArray();
            message.MacAlgorithmsServerToClient = _hmacAlgorithms.Keys.ToArray();
            message.CompressionAlgorithmsClientToServer = _compressionAlgorithms.Keys.ToArray();
            message.CompressionAlgorithmsServerToClient = _compressionAlgorithms.Keys.ToArray();
            message.LanguagesClientToServer = new[] { "" };
            message.LanguagesServerToClient = new[] { "" };
            message.FirstKexPacketFollows = false;
            message.Reserved = 0;

            return message;
        }
        #endregion

        #region Handle messages
        private void HandleMessageCore(Message message)
        {
            HandleMessage((dynamic)message);
        }

        private void HandleMessage(DisconnectMessage message)
        {
            Disconnect(message.ReasonCode, message.Description);
        }

        private void HandleMessage(KeyExchangeInitMessage message)
        {
            ConsiderReExchange(true);

            _exchangeContext.KeyExchange = ChooseAlgorithm(_keyExchangeAlgorithms.Keys.ToArray(), message.KeyExchangeAlgorithms);
            _exchangeContext.PublicKey = ChooseAlgorithm(_publicKeyAlgorithms.Keys.ToArray(), message.ServerHostKeyAlgorithms);
            _exchangeContext.ClientEncryption = ChooseAlgorithm(_encryptionAlgorithms.Keys.ToArray(), message.EncryptionAlgorithmsClientToServer);
            _exchangeContext.ServerEncryption = ChooseAlgorithm(_encryptionAlgorithms.Keys.ToArray(), message.EncryptionAlgorithmsServerToClient);
            _exchangeContext.ClientHmac = ChooseAlgorithm(_hmacAlgorithms.Keys.ToArray(), message.MacAlgorithmsClientToServer);
            _exchangeContext.ServerHmac = ChooseAlgorithm(_hmacAlgorithms.Keys.ToArray(), message.MacAlgorithmsServerToClient);
            _exchangeContext.ClientCompression = ChooseAlgorithm(_compressionAlgorithms.Keys.ToArray(), message.CompressionAlgorithmsClientToServer);
            _exchangeContext.ServerCompression = ChooseAlgorithm(_compressionAlgorithms.Keys.ToArray(), message.CompressionAlgorithmsServerToClient);

            _exchangeContext.ClientKexInitPayload = message.GetPacket();
        }

        private void HandleMessage(KeyExchangeDhInitMessage message)
        {
            var kexAlg = _keyExchangeAlgorithms[_exchangeContext.KeyExchange]();
            var hostKeyAlg = _publicKeyAlgorithms[_exchangeContext.PublicKey](_hostKey[_exchangeContext.PublicKey].ToString());
            var clientCipher = _encryptionAlgorithms[_exchangeContext.ClientEncryption]();
            var serverCipher = _encryptionAlgorithms[_exchangeContext.ServerEncryption]();
            var serverHmac = _hmacAlgorithms[_exchangeContext.ServerHmac]();
            var clientHmac = _hmacAlgorithms[_exchangeContext.ClientHmac]();

            var clientExchangeValue = message.E;
            var serverExchangeValue = kexAlg.CreateKeyExchange();
            var sharedSecret = kexAlg.DecryptKeyExchange(clientExchangeValue);
            var hostKeyAndCerts = hostKeyAlg.CreateKeyAndCertificatesData();
            var exchangeHash = ComputeExchangeHash(kexAlg, hostKeyAndCerts, clientExchangeValue, serverExchangeValue, sharedSecret);

            if (SessionId == null)
                SessionId = exchangeHash;

            var clientCipherIV = ComputeEncryptionKey(kexAlg, exchangeHash, clientCipher.BlockSize >> 3, sharedSecret, 'A');
            var serverCipherIV = ComputeEncryptionKey(kexAlg, exchangeHash, serverCipher.BlockSize >> 3, sharedSecret, 'B');
            var clientCipherKey = ComputeEncryptionKey(kexAlg, exchangeHash, clientCipher.KeySize >> 3, sharedSecret, 'C');
            var serverCipherKey = ComputeEncryptionKey(kexAlg, exchangeHash, serverCipher.KeySize >> 3, sharedSecret, 'D');
            var clientHmacKey = ComputeEncryptionKey(kexAlg, exchangeHash, clientHmac.KeySize >> 3, sharedSecret, 'E');
            var serverHmacKey = ComputeEncryptionKey(kexAlg, exchangeHash, serverHmac.KeySize >> 3, sharedSecret, 'F');

            _exchangeContext.NewAlgorithms = new Algorithms
            {
                KeyExchange = kexAlg,
                PublicKey = hostKeyAlg,
                ClientEncryption = clientCipher.Cipher(clientCipherKey, clientCipherIV, false),
                ServerEncryption = serverCipher.Cipher(serverCipherKey, serverCipherIV, true),
                ClientHmac = clientHmac.Hmac(clientHmacKey),
                ServerHmac = serverHmac.Hmac(serverHmacKey),
                ClientCompression = _compressionAlgorithms[_exchangeContext.ClientCompression](),
                ServerCompression = _compressionAlgorithms[_exchangeContext.ServerCompression](),
            };

            var reply = new KeyExchangeDhReplyMessage
            {
                HostKey = hostKeyAndCerts,
                F = serverExchangeValue,
                Signature = hostKeyAlg.CreateSignatureData(exchangeHash),
            };

            SendMessage(reply);
            SendMessage(new NewKeysMessage());
        }

        private void HandleMessage(NewKeysMessage message)
        {
            _hasBlockedMessagesWaitHandle.Reset();

            lock (_locker)
            {
                _inboundFlow = 0;
                _outboundFlow = 0;
                _algorithms = _exchangeContext.NewAlgorithms;
                _exchangeContext = null;
            }

            ContinueSendBlockedMessages();
            _hasBlockedMessagesWaitHandle.Set();
        }

        private void HandleMessage(UnimplementedMessage message)
        {
            SendMessage(message);
        }

        private void HandleMessage(ServiceRequestMessage message)
        {
            SshService service = RegisterService(message.ServiceName);
            if (service != null)
            {
                SendMessage(new ServiceAcceptMessage(message.ServiceName));
                return;
            }
            throw new SshConnectionException(string.Format("Service \"{0}\" not available.", message.ServiceName),
                DisconnectReason.ServiceNotAvailable);
        }

        private void HandleMessage(UserauthServiceMessage message)
        {
            var service = GetService<UserauthService>();
            if (service != null)
                service.HandleMessageCore(message);
        }

        private void HandleMessage(ConnectionServiceMessage message)
        {
            var service = GetService<ConnectionService>();
            if (service != null)
                service.HandleMessageCore(message);
        }
        #endregion

        private string ChooseAlgorithm(string[] serverAlgorithms, string[] clientAlgorithms)
        {
            foreach (var client in clientAlgorithms)
                foreach (var server in serverAlgorithms)
                    if (client == server)
                        return client;

            throw new SshConnectionException("Failed to negotiate algorithm.", DisconnectReason.KeyExchangeFailed);
        }

        private byte[] ComputeExchangeHash(KexAlgorithm kexAlg, byte[] hostKeyAndCerts, byte[] clientExchangeValue, byte[] serverExchangeValue, byte[] sharedSecret)
        {
            using (var worker = new SshDataWorker())
            {
                worker.Write(ClientVersion, Encoding.ASCII);
                worker.Write(ServerVersion, Encoding.ASCII);
                worker.WriteBinary(_exchangeContext.ClientKexInitPayload);
                worker.WriteBinary(_exchangeContext.ServerKexInitPayload);
                worker.WriteBinary(hostKeyAndCerts);
                worker.WriteMpint(clientExchangeValue);
                worker.WriteMpint(serverExchangeValue);
                worker.WriteMpint(sharedSecret);

                return kexAlg.ComputeHash(worker.ToByteArray());
            }
        }

        private byte[] ComputeEncryptionKey(KexAlgorithm kexAlg, byte[] exchangeHash, int blockSize, byte[] sharedSecret, char letter)
        {
            var keyBuffer = new byte[blockSize];
            var keyBufferIndex = 0;
            var currentHashLength = 0;
            byte[] currentHash = null;

            while (keyBufferIndex < blockSize)
            {
                using (var worker = new SshDataWorker())
                {
                    worker.WriteMpint(sharedSecret);
                    worker.Write(exchangeHash);

                    if (currentHash == null)
                    {
                        worker.Write((byte)letter);
                        worker.Write(SessionId);
                    }
                    else
                    {
                        worker.Write(currentHash);
                    }

                    currentHash = kexAlg.ComputeHash(worker.ToByteArray());
                }

                currentHashLength = Math.Min(currentHash.Length, blockSize - keyBufferIndex);
                Array.Copy(currentHash, 0, keyBuffer, keyBufferIndex, currentHashLength);

                keyBufferIndex += currentHashLength;
            }

            return keyBuffer;
        }

        private byte[] ComputeHmac(HmacAlgorithm alg, byte[] payload, uint seq)
        {
            using (var worker = new SshDataWorker())
            {
                worker.Write(seq);
                worker.Write(payload);

                return alg.ComputeHash(worker.ToByteArray());
            }
        }

        internal SshService RegisterService(string serviceName, UserauthArgs auth = null)
        {
            Contract.Requires(serviceName != null);

            SshService service = null;
            switch (serviceName)
            {
                case "ssh-userauth":
                    if (GetService<UserauthService>() == null)
                        service = new UserauthService(this);
                    break;
                case "ssh-connection":
                    if (auth != null && GetService<ConnectionService>() == null)
                        service = new ConnectionService(this, auth);
                    break;
            }
            if (service != null)
            {
                if (ServiceRegistered != null)
                    ServiceRegistered(this, service);

                _services.Add(service);
            }
            return service;
        }

        private class Algorithms
        {
            public KexAlgorithm KeyExchange;
            public PublicKeyAlgorithm PublicKey;
            public EncryptionAlgorithm ClientEncryption;
            public EncryptionAlgorithm ServerEncryption;
            public HmacAlgorithm ClientHmac;
            public HmacAlgorithm ServerHmac;
            public CompressionAlgorithm ClientCompression;
            public CompressionAlgorithm ServerCompression;
        }

        private class ExchangeContext
        {
            public string KeyExchange;
            public string PublicKey;
            public string ClientEncryption;
            public string ServerEncryption;
            public string ClientHmac;
            public string ServerHmac;
            public string ClientCompression;
            public string ServerCompression;

            public byte[] ClientKexInitPayload;
            public byte[] ServerKexInitPayload;

            public Algorithms NewAlgorithms;
        }
    }
}
