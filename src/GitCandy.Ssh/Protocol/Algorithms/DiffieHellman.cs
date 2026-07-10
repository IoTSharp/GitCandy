using System;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

namespace GitCandy.Ssh.Algorithms
{
    public class DiffieHellman : AsymmetricAlgorithm
    {
        // http://tools.ietf.org/html/rfc2412
        private const string Okley1024 = "00FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22514A08798E3404DDEF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7EDEE386BFB5A899FA5AE9F24117C4B1FE649286651ECE65381FFFFFFFFFFFFFFFF";

        // http://tools.ietf.org/html/rfc3526
        private const string Okley2048 = "00FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22514A08798E3404DDEF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7EDEE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3DC2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F83655D23DCA3AD961C62F356208552BB9ED529077096966D670C354E4ABC9804F1746C08CA18217C32905E462E36CE3BE39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9DE2BCBF6955817183995497CEA956AE515D2261898FA051015728E5A8AACAA68FFFFFFFFFFFFFFFF";
        private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        private BigInteger _p;
        private BigInteger _g;
        private BigInteger _x;

        public DiffieHellman(int bitlen)
        {
            Contract.Requires(bitlen == 1024 || bitlen == 2048);

            if (bitlen == 1024)
            {
                _p = BigInteger.Parse(Okley1024, NumberStyles.HexNumber);
                _g = new BigInteger(2);
            }
            else if (bitlen == 2048)
            {
                _p = BigInteger.Parse(Okley2048, NumberStyles.HexNumber);
                _g = new BigInteger(2);
            }
            else
                throw new ArgumentException("bitlen", "bitlen must equal 1024 or 2048");

            var bytes = new byte[80]; // 80 * 8 = 640 bits
            _rng.GetBytes(bytes);
            _x = BigInteger.Abs(new BigInteger(bytes));
        }

        public byte[] CreateKeyExchange()
        {
            var y = BigInteger.ModPow(_g, _x, _p);
            var bytes = BigintToBytes(y);
            return bytes;
        }

        public byte[] DecryptKeyExchange(byte[] keyEx)
        {
            Contract.Requires(keyEx != null);

            var pvr = BytesToBigint(keyEx);
            var z = BigInteger.ModPow(pvr, _x, _p);
            var bytes = BigintToBytes(z);
            return bytes;
        }

        public override void FromXmlString(string xmlString)
        {
            throw new NotImplementedException();
        }

        public override string KeyExchangeAlgorithm
        {
            get { return "1.2.840.113549.1.3"; }
        }

        public override string SignatureAlgorithm
        {
            get { return null; }
        }

        public override string ToXmlString(bool includePrivateParameters)
        {
            throw new NotImplementedException();
        }

        private BigInteger BytesToBigint(byte[] bytes)
        {
            return new BigInteger(bytes.Reverse().Concat(new byte[] { 0 }).ToArray());
        }

        private byte[] BigintToBytes(BigInteger bigint)
        {
            var bytes = bigint.ToByteArray();
            if (bytes.Length > 1 && bytes[bytes.Length - 1] == 0)
            {
                return bytes.Reverse().Skip(1).ToArray();
            }
            return bytes.Reverse().ToArray();
        }
    }
}
