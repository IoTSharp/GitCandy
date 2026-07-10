using System;
using System.Security.Cryptography;
using System.Text;

namespace GitCandy.Ssh.Messages
{
    [Message("SSH_MSG_KEXINIT", MessageNumber)]
    public class KeyExchangeInitMessage : Message
    {
        private const byte MessageNumber = 20;

        private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        public KeyExchangeInitMessage()
        {
            Cookie = new byte[16];
            _rng.GetBytes(Cookie);
        }

        public byte[] Cookie { get; private set; }

        public string[] KeyExchangeAlgorithms { get; set; }

        public string[] ServerHostKeyAlgorithms { get; set; }

        public string[] EncryptionAlgorithmsClientToServer { get; set; }

        public string[] EncryptionAlgorithmsServerToClient { get; set; }

        public string[] MacAlgorithmsClientToServer { get; set; }

        public string[] MacAlgorithmsServerToClient { get; set; }

        public string[] CompressionAlgorithmsClientToServer { get; set; }

        public string[] CompressionAlgorithmsServerToClient { get; set; }

        public string[] LanguagesClientToServer { get; set; }

        public string[] LanguagesServerToClient { get; set; }

        public bool FirstKexPacketFollows { get; set; }

        public uint Reserved { get; set; }

        public override byte MessageType { get { return MessageNumber; } }

        protected override void OnLoad(SshDataWorker reader)
        {
            Cookie = reader.ReadBinary(16);
            KeyExchangeAlgorithms = reader.ReadString(Encoding.ASCII).Split(',');
            ServerHostKeyAlgorithms = reader.ReadString(Encoding.ASCII).Split(',');
            EncryptionAlgorithmsClientToServer = reader.ReadString(Encoding.ASCII).Split(',');
            EncryptionAlgorithmsServerToClient = reader.ReadString(Encoding.ASCII).Split(',');
            MacAlgorithmsClientToServer = reader.ReadString(Encoding.ASCII).Split(',');
            MacAlgorithmsServerToClient = reader.ReadString(Encoding.ASCII).Split(',');
            CompressionAlgorithmsClientToServer = reader.ReadString(Encoding.ASCII).Split(',');
            CompressionAlgorithmsServerToClient = reader.ReadString(Encoding.ASCII).Split(',');
            LanguagesClientToServer = reader.ReadString(Encoding.ASCII).Split(',');
            LanguagesServerToClient = reader.ReadString(Encoding.ASCII).Split(',');
            FirstKexPacketFollows = reader.ReadBoolean();
            Reserved = reader.ReadUInt32();
        }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            writer.Write(Cookie);
            writer.Write(string.Join(",", KeyExchangeAlgorithms), Encoding.ASCII);
            writer.Write(string.Join(",", ServerHostKeyAlgorithms), Encoding.ASCII);
            writer.Write(string.Join(",", EncryptionAlgorithmsClientToServer), Encoding.ASCII);
            writer.Write(string.Join(",", EncryptionAlgorithmsServerToClient), Encoding.ASCII);
            writer.Write(string.Join(",", MacAlgorithmsClientToServer), Encoding.ASCII);
            writer.Write(string.Join(",", MacAlgorithmsServerToClient), Encoding.ASCII);
            writer.Write(string.Join(",", CompressionAlgorithmsClientToServer), Encoding.ASCII);
            writer.Write(string.Join(",", CompressionAlgorithmsServerToClient), Encoding.ASCII);
            writer.Write(string.Join(",", LanguagesClientToServer), Encoding.ASCII);
            writer.Write(string.Join(",", LanguagesServerToClient), Encoding.ASCII);
            writer.Write(FirstKexPacketFollows);
            writer.Write(Reserved);
        }
    }
}
