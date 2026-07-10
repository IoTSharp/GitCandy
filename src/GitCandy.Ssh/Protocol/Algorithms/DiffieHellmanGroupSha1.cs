using System.Diagnostics.Contracts;
using System.Security.Cryptography;

namespace GitCandy.Ssh.Algorithms
{
    public class DiffieHellmanGroupSha1 : KexAlgorithm
    {
        private readonly DiffieHellman _exchangeAlgorithm;

        public DiffieHellmanGroupSha1(DiffieHellman algorithm)
        {
            Contract.Requires(algorithm != null);

            _exchangeAlgorithm = algorithm;
            _hashAlgorithm = SHA1.Create();
        }

        public override byte[] CreateKeyExchange()
        {
            return _exchangeAlgorithm.CreateKeyExchange();
        }

        public override byte[] DecryptKeyExchange(byte[] exchangeData)
        {
            return _exchangeAlgorithm.DecryptKeyExchange(exchangeData);
        }
    }
}
