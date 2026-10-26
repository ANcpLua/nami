using System.IO.Compression;
using Grpc.Core;
using Grpc.Net.Compression;

namespace Nami.Tests;

internal sealed class BoundedGrpcCompressionProvider(ICompressionProvider inner, int maximumBytes)
    : ICompressionProvider
{
    public string EncodingName => inner.EncodingName;

    public Stream CreateCompressionStream(Stream stream, CompressionLevel? compressionLevel)
    {
        return inner.CreateCompressionStream(stream, compressionLevel);
    }

    public Stream CreateDecompressionStream(Stream stream)
    {
        return new BoundedReadStream(inner.CreateDecompressionStream(stream), maximumBytes);
    }

    // grpc-dotnet 2.84 checks the frame length, then copies the decompression stream to memory.
    // Bound that copy before it can allocate an arbitrarily large decompressed message.
    // https://github.com/grpc/grpc-dotnet/blob/4c6997a214601422dd66c5c74c1969db749479e9/src/Grpc.AspNetCore.Server/Internal/PipeExtensions.cs#L341-L415
    private sealed class BoundedReadStream(Stream inner, int maximumBytes) : Stream
    {
        private long _read;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override void Flush()
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

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var count = inner.Read(buffer[..(int)Math.Min(buffer.Length, maximumBytes - _read + 1)]);
            _read += count;
            if (_read > maximumBytes)
            {
                throw new RpcException(new Status(StatusCode.ResourceExhausted,
                    $"The decompressed OTLP payload exceeds the {maximumBytes}-byte receiver limit."));
            }

            return count;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
