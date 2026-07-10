using System.Security.Cryptography;
using System.Text;

namespace GitCandy.Ssh.Algorithms
{
    public class RsaKey : PublicKeyAlgorithm
    {
        protected readonly RSACryptoServiceProvider _algorithm = new RSACryptoServiceProvider();

        public RsaKey(string xml)
            : base(xml)
        {
        }

        public override string Name
        {
            get { return "ssh-rsa"; }
        }

        protected override void ImportKey(string xml)
        {
            _algorithm.FromXmlString(xml);
        }

        public override void LoadKeyAndCertificatesData(byte[] data)
        {
            using (var worker = new SshDataWorker(data))
            {
                if (worker.ReadString(Encoding.ASCII) != this.Name)
                    throw new CryptographicException("Key and certificates were not created with this algorithm.");

                var args = new RSAParameters();
                args.Exponent = worker.ReadMpint();
                args.Modulus = worker.ReadMpint();

                _algorithm.ImportParameters(args);
            }
        }

        public override byte[] CreateKeyAndCertificatesData()
        {
            using (var worker = new SshDataWorker())
            {
                var args = _algorithm.ExportParameters(false);

                worker.Write(this.Name, Encoding.ASCII);
                worker.WriteMpint(args.Exponent);
                worker.WriteMpint(args.Modulus);

                return worker.ToByteArray();
            }
        }

        public override bool VerifyData(byte[] data, byte[] signature)
        {
            return _algorithm.VerifyData(data, "SHA1", signature);
        }

        public override bool VerifyHash(byte[] hash, byte[] signature)
        {
            return _algorithm.VerifyHash(hash, "SHA1", signature);
        }

        public override byte[] SignData(byte[] data)
        {
            return _algorithm.SignData(data, "SHA1");
        }

        public override byte[] SignHash(byte[] hash)
        {
            return _algorithm.SignHash(hash, "SHA1");
        }
    }
}
