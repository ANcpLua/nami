using Grpc.Net.Compression;

namespace Nami.Grpc;

/// <summary>
///     A gRPC compression provider whose decompression stops at the server's receive limit.
///     grpc-dotnet decompresses a request message into an unbounded buffer before any interceptor runs, and
///     <c>MaxReceiveMessageSize</c> only bounds the compressed bytes, so without this a small compressed payload
///     expands without limit ahead of the API-key check. Decompressed messages are held to the same limit gRPC
///     applies on the wire, the way grpc-go and the OpenTelemetry Collector already do.
/// </summary>
internal sealed class BoundedDecompressionProvider(ICompressionProvider inner, int maxDecompressedBytes)
    : ICompressionProvider
{
    /// <summary>grpc-dotnet's default receive limit, used when <c>MaxReceiveMessageSize</c> is unset.</summary>
    public const int DefaultMaxReceiveMessageSize = 4 * 1024 * 1024;

    public string EncodingName => inner.EncodingName;

    public Stream CreateCompressionStream(Stream stream, CompressionLevel? compressionLevel)
    {
        return inner.CreateCompressionStream(stream, compressionLevel);
    }

    public Stream CreateDecompressionStream(Stream stream)
    {
        return new LimitedReadStream(inner.CreateDecompressionStream(stream), maxDecompressedBytes);
    }

    private sealed class LimitedReadStream(Stream inner, int limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Count(inner.Read(buffer, offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            return Count(inner.Read(buffer));
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        private int Count(int read)
        {
            _read += read;
            if (_read > limit)
            {
                throw new RpcException(new Status(
                    StatusCode.ResourceExhausted,
                    $"Decompressed message exceeds the {limit}-byte receive limit."));
            }

            return read;
        }
    }
}
