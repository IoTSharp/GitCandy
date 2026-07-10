using System.Diagnostics.Contracts;
using System.Security.Cryptography;

namespace GitCandy.Ssh.Algorithms
{
    [ContractClass(typeof(KexAlgorithmContract))]
    public abstract class KexAlgorithm
    {
        protected HashAlgorithm _hashAlgorithm;

        public abstract byte[] CreateKeyExchange();

        public abstract byte[] DecryptKeyExchange(byte[] exchangeData);

        public byte[] ComputeHash(byte[] input)
        {
            Contract.Requires(input != null);

            return _hashAlgorithm.ComputeHash(input);
        }
    }
}
