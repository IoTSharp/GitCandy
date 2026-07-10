using GitCandy.Ssh.Services;

namespace GitCandy.Ssh;

internal sealed class SshChannelOutputStream(
    SessionChannel channel,
    CancellationToken cancellationToken) : Stream
{
    private readonly SessionChannel _channel = channel;
    private readonly CancellationToken _cancellationToken = cancellationToken;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _cancellationToken.ThrowIfCancellationRequested();
        _channel.SendData(CopyBuffer(buffer, offset, count), _cancellationToken);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _cancellationToken,
            cancellationToken);
        linkedCancellation.Token.ThrowIfCancellationRequested();
        _channel.SendData(buffer.ToArray(), linkedCancellation.Token);
        return ValueTask.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    private static byte[] CopyBuffer(byte[] buffer, int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The offset and count exceed the buffer length.");
        }

        if (offset == 0 && count == buffer.Length)
        {
            return buffer;
        }

        var copy = new byte[count];
        Buffer.BlockCopy(buffer, offset, copy, 0, count);
        return copy;
    }
}
