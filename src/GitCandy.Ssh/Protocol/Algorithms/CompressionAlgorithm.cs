using System.Diagnostics.Contracts;

namespace GitCandy.Ssh.Algorithms
{
    [ContractClass(typeof(CompressionAlgorithmContract))]
    public abstract class CompressionAlgorithm
    {
        public abstract byte[] Compress(byte[] input);

        public abstract byte[] Decompress(byte[] input);
    }
}
