using System;
using System.Diagnostics.Contracts;
using System.Security.Cryptography;

namespace GitCandy.Ssh.Algorithms
{
    public class HmacInfo
    {
        public HmacInfo(KeyedHashAlgorithm algorithm, int keySize)
        {
            Contract.Requires(algorithm != null);

            KeySize = keySize;
            Hmac = key => new HmacAlgorithm(algorithm, keySize, key);
        }

        public int KeySize { get; private set; }

        public Func<byte[], HmacAlgorithm> Hmac { get; private set; }
    }
}
