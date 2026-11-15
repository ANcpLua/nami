using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using Microsoft.Extensions.Options;
using Nami.Api.Contracts;
using Nami.Api.Contracts.Engine;
using Nami.Api.Contracts.Health;
using Nami.ApiKeys;
using Nami.Collector.Storage;
using Nami.SemanticConventions;
using Nami.Telemetry;
using Nami.Tools;

namespace Nami.Hosting;

/// <summary>
///     The private, bounded JSONL connection owned by the TypeScript supervisor, over the process's standard input and
///     output unless other streams are given. Only the end of the input ends it: a frame it cannot serve is refused for
///     its id, or dropped with one line on <paramref name="diagnostics" /> when it names none.
/// </summary>
internal sealed class EngineProtocol(
    IServiceProvider services,
    CancellationToken stopping,
    Stream? input = null,
    Stream? output = null,
    TextWriter? diagnostics = null) : IAsyncDisposable
{
    /// <summary>The longest id a refusal echoes; a longer one is the host's fault and is dropped like a frame without id.</summary>
    private const int MaxRefusedIdLength = 128;

    private static readonly byte[] s_lineTerminator = [10];

    private readonly TextWriter _diagnostics = diagnostics ?? Console.Error;
    private readonly SemaphoreSlim _execution = new(EngineLimits.WorkersDefault);
    private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    private readonly PipeWriter _output = PipeWriter.Create(output ?? Console.OpenStandardOutput());
    private readonly ConcurrentDictionary<string, PendingCall> _pending = new(StringComparer.Ordinal);
    private readonly PipeReader _reader = PipeReader.Create(input ?? Console.OpenStandardInput());
    private readonly List<Task> _tasks = [];
    private readonly SemaphoreSlim _writes = new(1);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _reader.CompleteAsync();
            await _output.CompleteAsync();
        }
        finally
        {
            _lifetime.Dispose();
            _writes.Dispose();
            _execution.Dispose();
        }
    }

    internal static async Task RunAsync(IServiceProvider services, CancellationToken stopping, Stream? input = null,
        Stream? output = null, TextWriter? diagnostics = null)
    {
        await using var protocol = new EngineProtocol(services, stopping, input, output, diagnostics);
        await protocol.ProcessAsync();
    }

    private async Task ProcessAsync()
    {
        try
        {
            await Respond("ready", await HealthAsync(services, _lifetime.Token), false);
            // Inside a frame longer than the limit: its bytes are discarded as they arrive, up to the next newline.
            var skipping = false;
            while (!_lifetime.IsCancellationRequested)
            {
                var read = await _reader.ReadAsync(_lifetime.Token);
                var buffer = read.Buffer;
                while (buffer.PositionOf((byte)'\n') is { } end)
                {
                    var frame = buffer.Slice(0, end);
                    buffer = buffer.Slice(buffer.GetPosition(1, end));
                    if (skipping)
                        skipping = false;
                    else if (frame.Length > EngineLimits.MaxFrameBytesDefault)
                        Drop("a frame longer than the frame limit");
                    else
                        await Accept(frame);
                }

                if (skipping || buffer.Length > EngineLimits.MaxFrameBytesDefault)
                {
                    if (!skipping) Drop("a frame longer than the frame limit");
                    skipping = true;
                    buffer = buffer.Slice(buffer.End);
                }

                _reader.AdvanceTo(buffer.Start, buffer.End);
                if (!read.IsCompleted)
                    continue;
                if (!buffer.IsEmpty) throw new InvalidDataException("Incomplete engine frame.");
                break;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            await _lifetime.CancelAsync();
            await Task.WhenAll(_tasks);
        }
    }

    /// <summary>
    ///     One complete frame within the limit. The host frees a request's slot when it reads the reply or when its
    ///     caller goes away, and may fill that slot with its next frame at once; so the entry of a cancelled call
    ///     leaves <see cref="_pending" /> here, when the cancel is read, and the entry of an answered call before its
    ///     reply is written (<see cref="Reply" />). With both, no frame of a host that keeps the shared limit meets a
    ///     full table (eng/proofs, Design.fixed_noRefusal; either change alone is refuted there). A frame that does,
    ///     or that repeats a live id or names another contract revision, is refused for its id and the loop reads on.
    /// </summary>
    private async ValueTask Accept(ReadOnlySequence<byte> frame)
    {
        var request = Decode(frame, out var refused);
        if (request is null)
        {
            if (refused is null)
                Drop("a frame that is not an engine request");
            else
                await Respond(refused, ToolDispatcher.Error("invalid_argument",
                    "The engine could not decode this request.").Content, true);
            return;
        }

        if (request.Method == "cancel")
        {
            if (request.Revision != ContractRevision.Value)
                Drop("a cancel frame for another contract revision");
            else if (_pending.TryGetValue(request.Id, out var active) &&
                     _pending.TryRemove(KeyValuePair.Create(request.Id, active)))
                active.Cancel();
            return;
        }

        if (request.Revision != ContractRevision.Value)
        {
            await Respond(request.Id, ToolDispatcher.Error("collector_contract_mismatch",
                "The request names a different contract revision than this engine serves.").Content, true);
            return;
        }

        if (_pending.Count >= EngineLimits.MaxPendingDefault)
        {
            await Respond(request.Id, ToolDispatcher.Error("collector_unavailable",
                "The engine already serves its limit of pending requests.", retryable: true).Content, true);
            return;
        }

        var call = new PendingCall(_lifetime.Token);
        if (!_pending.TryAdd(request.Id, call))
        {
            call.Dispose();
            await Respond(request.Id, ToolDispatcher.Error("invalid_argument",
                "The engine already serves a request with this id.", "id").Content, true);
            return;
        }

        _tasks.RemoveAll(static task => task.IsCompletedSuccessfully);
        _tasks.Add(Execute(request, call));
    }

    /// <summary>One line on the diagnostics stream for a frame that names no request to refuse.</summary>
    private void Drop(string what)
    {
        _diagnostics.WriteLine("nami engine: dropped " + what + ".");
    }

    private async Task Execute(EngineRequest request, PendingCall cancellation)
    {
        var entered = false;
        var previous = Activity.Current;
        Activity? activity = null;
        try
        {
            await _execution.WaitAsync(cancellation.Token);
            entered = true;
            if (request.Method == "health")
            {
                await Reply(request.Id, cancellation, await HealthAsync(services, cancellation.Token), false);
                return;
            }

            var options = services.GetRequiredService<IOptions<OtlpApiKeyOptions>>().Value;
            var credential = OtlpApiKeyValidator.Resolve(request.Credential.Value, options);
            if (options.IsApiKeyMode && (credential is null || !credential.Roles.HasFlag(ApiKeyRoles.Read)))
            {
                var denied = ToolDispatcher.Error("unauthorized", "A project-bound read credential is required.");
                await Reply(request.Id, cancellation, denied.Content, true);
                return;
            }

            if (!ActivityContext.TryParse(request.Traceparent.Value, request.Tracestate.Value, out var parent))
                Activity.Current = null;
            activity = NamiTelemetry.Source.StartActivity("tools/call " + request.Name.Value, ActivityKind.Server, parent);
            activity?.SetTag("mcp.method.name", "tools/call");
            activity?.SetTag("gen_ai.tool.name", request.Name.Value);
            var started = Stopwatch.GetTimestamp();
            try
            {
                var result = await services.GetRequiredService<NamiToolService>().InvokeAsync(
                    request.Name.Value ?? "", request.Input.GetValueOrDefault(),
                    credential?.ProjectId ?? ProjectScope.DefaultProjectId,
                    cancellation.Token);
                if (result.IsError)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    if (result.Content.TryGetProperty("code", out var code))
                        activity?.SetTag(ErrorAttributes.Type, code.GetString());
                }

                await Reply(request.Id, cancellation, result.Content, result.IsError);
            }
            finally
            {
                NamiTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                    new KeyValuePair<string, object?>("mcp.method.name", "tools/call"),
                    new KeyValuePair<string, object?>("gen_ai.tool.name", request.Name.Value));
            }
        }
        catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            NamiTelemetry.RecordException(error, services.GetRequiredService<ILoggerFactory>().CreateLogger("nami"));
            var failure = ToolDispatcher.Error("internal_error", "The engine could not complete the request.");
            await Reply(request.Id, cancellation, failure.Content, true);
        }
        finally
        {
            activity?.Dispose();
            Activity.Current = previous;
            if (entered) _execution.Release();
            // By value: after a cancel freed this id, the same id may name a newer call.
            _pending.TryRemove(KeyValuePair.Create(request.Id, cancellation));
            cancellation.Dispose();
        }
    }

    /// <summary>
    ///     Frees the call's slot, then writes its reply: the host frees its own slot when it reads the reply, so the
    ///     entry must be gone before the reply can arrive. A call whose entry a cancel frame already removed is not
    ///     answered; the host stopped waiting for it when it wrote the cancel.
    /// </summary>
    private async Task Reply(string id, PendingCall call, JsonElement result, bool error)
    {
        if (_pending.TryRemove(KeyValuePair.Create(id, call))) await Respond(id, result, error);
    }

    private async Task Respond(string id, JsonElement result, bool error)
    {
        await _writes.WaitAsync(_lifetime.Token);
        try
        {
            await using var writer = new Utf8JsonWriter(_output);
            JsonSerializer.Serialize(writer, new EngineResponse
            {
                Id = id,
                Revision = ContractRevision.Value,
                Result = result,
                IsError = error
            }, ContractTypes.TypeInfo<EngineResponse>());
            await writer.FlushAsync(_lifetime.Token);
            await _output.WriteAsync(s_lineTerminator, _lifetime.Token);
            await _output.FlushAsync(_lifetime.Token);
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <summary>
    ///     Decodes one frame. The host forwards an agent's tool arguments and <c>_meta</c> trace context unchanged, so a
    ///     frame can name its request and still not decode: arguments nested past the reader's depth, a string holding an
    ///     unpaired UTF-16 surrogate. That request is refused alone, by the id in <paramref name="refused" />; a frame
    ///     whose id cannot be read leaves it null.
    /// </summary>
    private static EngineRequest? Decode(ReadOnlySequence<byte> frame, out string? refused)
    {
        refused = null;
        try
        {
            using var document = JsonDocument.Parse(frame);
            if (ContractJson.Validate("Engine.EngineRequest", document.RootElement) is { } issue)
                throw new InvalidDataException("Invalid engine request: " + issue.Field);
            return document.RootElement.Deserialize(ContractTypes.TypeInfo<EngineRequest>())
                   ?? throw new InvalidDataException("Missing engine request.");
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
        {
            refused = RequestId(frame) is { Length: <= MaxRefusedIdLength } id ? id : null;
            return null;
        }
    }

    /// <summary>The frame's request id, read without a depth bound, or null when the frame names none.</summary>
    private static string? RequestId(ReadOnlySequence<byte> frame)
    {
        var reader = new Utf8JsonReader(frame, new JsonReaderOptions { MaxDepth = EngineLimits.MaxFrameBytesDefault });
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isId = reader.ValueTextEquals("id"u8);
                if (!reader.Read()) return null;
                if (isId) return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                reader.Skip();
            }

            return null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<JsonElement> HealthAsync(IServiceProvider services, CancellationToken cancellation)
    {
        var started = Stopwatch.GetTimestamp();
        await services.GetRequiredService<INamiStore>()
            .GetStorageStatsAsync(ProjectScope.DefaultProjectId, cancellation);
        return ContractJson.Serialize(new HealthReport
        {
            Status = HealthStatus.Healthy,
            TotalDurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            ContractRevision = ContractRevision.Value,
            Entries = new Dictionary<string, HealthCheckEntry>
            {
                ["duckdb"] = new()
                { Status = HealthStatus.Healthy, DurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds }
            }
        });
    }

    private sealed class PendingCall(CancellationToken parent) : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly CancellationTokenSource _source = CancellationTokenSource.CreateLinkedTokenSource(parent);
        private bool _disposed;

        internal CancellationToken Token
        {
            get
            {
                lock (_gate) return _source.Token;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _source.Dispose();
            }
        }

        internal void Cancel()
        {
            lock (_gate)
            {
                if (!_disposed) _source.Cancel();
            }
        }
    }
}
