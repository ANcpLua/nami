using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Nami.Hosting;

namespace Nami.IntegrationTests;

/// <summary>An engine served over in-memory pipes, as the TypeScript host serves it over the child's stdio.</summary>
internal sealed class EngineSession : IAsyncDisposable
{
    private readonly Pipe _fromEngine = new();
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly List<byte> _pending = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly Pipe _toEngine = new();

    /// <param name="services">The engine's services.</param>
    /// <param name="diagnostics">Receives the lines the engine would write to standard error.</param>
    /// <param name="holdReplies">
    ///     When given, every reply stays "being written" after its bytes are readable here, until the task it returns
    ///     completes: the state in which a host has read a reply that the engine's write has not returned from.
    /// </param>
    internal EngineSession(IServiceProvider services, TextWriter? diagnostics = null, Func<Task>? holdReplies = null)
    {
        _input = _toEngine.Reader.AsStream();
        _output = holdReplies is null
            ? _fromEngine.Writer.AsStream()
            : new HeldWrites(_fromEngine.Writer.AsStream(), holdReplies);
        Run = Task.Run(() => EngineProtocol.RunAsync(services, _stopping.Token, _input, _output, diagnostics));
    }

    /// <summary>The engine's lifetime: it completes when the connection ends, and faults when the engine stops on an error.</summary>
    internal Task Run { get; }

    public async ValueTask DisposeAsync()
    {
        await _toEngine.Writer.CompleteAsync();
        await _stopping.CancelAsync();
        try
        {
            await Run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The outcome was judged before disposal; a stopped engine's exception is no further evidence.
        }

        await _fromEngine.Reader.CompleteAsync();
        await _input.DisposeAsync();
        await _output.DisposeAsync();
        _stopping.Dispose();
    }

    internal async Task WriteAsync(string text, Random random, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var offset = 0;
        while (offset < bytes.Length)
        {
            // Fragmented writes: the reader must assemble frames from any split.
            var length = random.Next(4) == 0 ? Math.Min(bytes.Length - offset, 1 + random.Next(64)) : bytes.Length - offset;
            await _toEngine.Writer.WriteAsync(bytes.AsMemory(offset, length), ct);
            offset += length;
        }
    }

    /// <summary>One write, as the host writes a frame or a burst of frames.</summary>
    internal async Task WriteAsync(string text, CancellationToken ct)
    {
        await _toEngine.Writer.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
    }

    internal ValueTask CloseInputAsync() => _toEngine.Writer.CompleteAsync();

    /// <summary>The next line the engine wrote, or null when the engine stopped writing or the timeout passed.</summary>
    internal async Task<string?> ReadLineAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        while (true)
        {
            var newline = _pending.IndexOf((byte)'\n');
            if (newline >= 0)
            {
                var line = Encoding.UTF8.GetString(_pending.GetRange(0, newline).ToArray());
                _pending.RemoveRange(0, newline + 1);
                return line;
            }

            ReadResult read;
            try
            {
                read = await _fromEngine.Reader.ReadAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }

            _pending.AddRange(read.Buffer.ToArray());
            _fromEngine.Reader.AdvanceTo(read.Buffer.End);
            if (read.IsCompleted && _pending.IndexOf((byte)'\n') < 0) return null;
        }
    }

    /// <summary>Passes every write through, then holds the writer until the gate completes.</summary>
    private sealed class HeldWrites(Stream inner, Func<Task> gate) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            await inner.FlushAsync(cancellationToken);
            // A reply is complete at its newline; only then can a reader have taken it.
            if (buffer.Length > 0 && buffer.Span[^1] == (byte)'\n') await gate().WaitAsync(cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
