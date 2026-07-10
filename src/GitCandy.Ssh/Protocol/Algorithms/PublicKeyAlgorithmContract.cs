using System;
using System.Diagnostics.Contracts;

namespace GitCandy.Ssh.Algorithms
{
    [ContractClassFor(typeof(PublicKeyAlgorithm))]
    abstract class PublicKeyAlgorithmContract : PublicKeyAlgorithm
    {
        public PublicKeyAlgorithmContract()
            : base(null)
        {
        }

        public override string Name
        {
            get { throw new NotImplementedException(); }
        }

        protected override void ImportKey(string xml)
        {
            throw new NotImplementedException();
        }

        public override void LoadKeyAndCertificatesData(byte[] data)
        {
            Contract.Requires(data != null);

            throw new NotImplementedException();
        }

        public override byte[] CreateKeyAndCertificatesData()
        {
            throw new NotImplementedException();
        }

        public override bool VerifyData(byte[] data, byte[] signature)
        {
            Contract.Requires(data != null);
            Contract.Requires(signature != null);

            throw new NotImplementedException();
        }

        public override bool VerifyHash(byte[] hash, byte[] signature)
        {
            Contract.Requires(hash != null);
            Contract.Requires(signature != null);

            throw new NotImplementedException();
        }

        public override byte[] SignData(byte[] data)
        {
            Contract.Requires(data != null);

            throw new NotImplementedException();
        }

        public override byte[] SignHash(byte[] hash)
        {
            Contract.Requires(hash != null);

            throw new NotImplementedException();
        }
    }
}
