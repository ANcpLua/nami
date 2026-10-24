using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nami.Collector.Ingestion;

/// <summary>
///     Bounds encoded HTTP bytes before gzip consumes them. Headers and empty gzip members
///     consume the wire budget even when they produce no decoded bytes.
/// </summary>
internal sealed class OtlpWireLimitStream(Stream inner, int limit) : Stream
{
    private long _read;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer[..Bound(buffer.Length)]));

    public override async ValueTask<int>
        ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer[..Bound(buffer.Length)], cancellationToken).ConfigureAwait(false));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Bound(int count) => (int)Math.Min(count, limit - _read + 1);

    private int Count(int count)
    {
        _read += count;
        if (_read > limit) throw new OtlpPayloadTooLargeException();
        return count;
    }

    // Kestrel owns the underlying request body; disposing this wrapper must leave it open.
}
