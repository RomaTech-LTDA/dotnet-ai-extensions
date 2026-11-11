namespace Romatech.Extensions.Ai.Mcp.Internal;

/// <summary>
/// Write-only buffer that keeps at most <c>limit</c> bytes and silently discards the rest,
/// so a large endpoint response cannot exhaust memory.
/// </summary>
internal sealed class BoundedWriteStream : Stream
{
    private readonly MemoryStream _buffer = new();
    private readonly int _limit;
    private long _written;

    public BoundedWriteStream(int limit)
    {
        _limit = Math.Max(0, limit);
    }

    public bool Truncated => _written > _limit;

    public long TotalBytesWritten => _written;

    public byte[] ToArray() => _buffer.ToArray();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _written;
    public override long Position { get => _written; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var room = _limit - (int)_buffer.Length;
        if (room > 0)
            _buffer.Write(buffer.Slice(0, Math.Min(room, buffer.Length)));
        _written += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return default;
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
