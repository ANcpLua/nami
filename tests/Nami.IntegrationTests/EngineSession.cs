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

    internal EngineSession(IServiceProvider services)
    {
        _input = _toEngine.Reader.AsStream();
        _output = _fromEngine.Writer.AsStream();
        Run = Task.Run(() => EngineProtocol.RunAsync(services, _stopping.Token, _input, _output));
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
}
