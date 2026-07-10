using System.Diagnostics.Contracts;

namespace GitCandy.Ssh.Services
{
    public class UserauthArgs
    {
        public UserauthArgs(string keyAlgorithm, string fingerprint, byte[] key)
        {
            Contract.Requires(keyAlgorithm != null);
            Contract.Requires(fingerprint != null);
            Contract.Requires(key != null);

            KeyAlgorithm = keyAlgorithm;
            Fingerprint = fingerprint;
            Key = key;
        }

        public string KeyAlgorithm { get; private set; }
        public string Fingerprint { get; private set; }
        public byte[] Key { get; private set; }
        public bool Result { get; set; }
    }
}
