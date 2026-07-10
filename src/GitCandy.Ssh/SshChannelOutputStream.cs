using System.Runtime.InteropServices;
using Microsoft.DevTunnels.Ssh;
using SshBuffer = Microsoft.DevTunnels.Ssh.Buffer;

namespace GitCandy.Ssh;

internal sealed class SshChannelOutputStream(
    SshChannel channel,
    CancellationToken cancellationToken) : Stream
{
    private readonly SshChannel _channel = channel;
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
        if (count == 0)
        {
            return;
        }

        _channel.SendAsync(
                SshBuffer.From(buffer, offset, count),
                _cancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        if (cancellationToken == _cancellationToken || !cancellationToken.CanBeCanceled)
        {
            await SendAsync(buffer, _cancellationToken);
            return;
        }

        if (!_cancellationToken.CanBeCanceled)
        {
            await SendAsync(buffer, cancellationToken);
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _cancellationToken,
            cancellationToken);
        await SendAsync(buffer, linkedCancellation.Token);
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

    private Task SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (MemoryMarshal.TryGetArray(buffer, out var segment) && segment.Array is not null)
        {
            return _channel.SendAsync(
                SshBuffer.From(segment.Array, segment.Offset, segment.Count),
                cancellationToken);
        }

        return _channel.SendAsync(SshBuffer.From(buffer.ToArray()), cancellationToken);
    }
}
