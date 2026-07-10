using System;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Security.Cryptography;

namespace GitCandy.Ssh.Algorithms
{
    public class CipherInfo
    {
        public CipherInfo(SymmetricAlgorithm algorithm, int keySize, CipherModeEx mode)
        {
            Contract.Requires(algorithm != null);
            Contract.Requires(algorithm.LegalKeySizes.Any(x =>
                x.MinSize <= keySize && keySize <= x.MaxSize && keySize % x.SkipSize == 0));

            algorithm.KeySize = keySize;
            KeySize = algorithm.KeySize;
            BlockSize = algorithm.BlockSize;
            Cipher = (key, vi, isEncryption) => new EncryptionAlgorithm(algorithm, keySize, mode, key, vi, isEncryption);
        }

        public int KeySize { get; private set; }

        public int BlockSize { get; private set; }

        public Func<byte[], byte[], bool, EncryptionAlgorithm> Cipher { get; private set; }
    }
}
