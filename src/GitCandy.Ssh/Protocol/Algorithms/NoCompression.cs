
namespace GitCandy.Ssh.Algorithms
{
    public class NoCompression : CompressionAlgorithm
    {
        public override byte[] Compress(byte[] input)
        {
            return input;
        }

        public override byte[] Decompress(byte[] input)
        {
            return input;
        }
    }
}
