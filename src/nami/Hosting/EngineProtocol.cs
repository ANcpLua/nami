using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nami.Api.Contracts;
using Nami.Api.Contracts.Engine;
using Nami.Api.Contracts.Health;
using Nami.ApiKeys;
using Nami.Collector.Storage;
using Nami.Telemetry;
using Nami.SemanticConventions;
using Nami.Tools;

namespace Nami.Hosting;

/// <summary>The private, bounded JSONL connection owned by the TypeScript supervisor.</summary>
internal sealed class EngineProtocol(IServiceProvider services, CancellationToken stopping) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    private readonly SemaphoreSlim _writes = new(1);
    private readonly SemaphoreSlim _execution = new(EngineLimits.WorkersDefault);
    private readonly ConcurrentDictionary<string, PendingCall> _pending = new(StringComparer.Ordinal);
    private readonly List<Task> _tasks = [];
    private readonly PipeReader _reader = PipeReader.Create(Console.OpenStandardInput());
    private readonly PipeWriter _output = PipeWriter.Create(Console.OpenStandardOutput());

    internal static async Task RunAsync(IServiceProvider services, CancellationToken stopping)
    {
        await using var protocol = new EngineProtocol(services, stopping);
        await protocol.ProcessAsync();
    }

    private async Task ProcessAsync()
    {
        try
        {
            await Respond("ready", await HealthAsync(services, _lifetime.Token), false);
            while (!_lifetime.IsCancellationRequested)
            {
                var read = await _reader.ReadAsync(_lifetime.Token);
                var buffer = read.Buffer;
                while (buffer.PositionOf((byte)'\n') is { } end)
                {
                    var frame = buffer.Slice(0, end);
                    if (frame.Length > EngineLimits.MaxFrameBytesDefault) throw new InvalidDataException("Engine frame exceeds its limit.");
                    var request = Decode(frame);
                    buffer = buffer.Slice(buffer.GetPosition(1, end));
                    if (request.Revision != ContractRevision.Value)
                        throw new InvalidDataException("Engine contract revision mismatch.");
                    if (request.Method == "cancel")
                    {
                        if (_pending.TryGetValue(request.Id, out var active)) active.Cancel();
                        continue;
                    }
                    if (_pending.Count >= EngineLimits.MaxPendingDefault || _pending.ContainsKey(request.Id))
                        throw new InvalidDataException("Duplicate or excessive pending engine requests.");
                    var cancellation = new PendingCall(_lifetime.Token);
                    if (!_pending.TryAdd(request.Id, cancellation))
                    {
                        cancellation.Dispose();
                        throw new InvalidDataException("Duplicate engine request.");
                    }
                    _tasks.RemoveAll(static task => task.IsCompletedSuccessfully);
                    _tasks.Add(Execute(request, cancellation));
                }
                if (buffer.Length > EngineLimits.MaxFrameBytesDefault) throw new InvalidDataException("Engine frame exceeds its limit.");
                _reader.AdvanceTo(buffer.Start, buffer.End);
                if (read.IsCompleted)
                {
                    if (!buffer.IsEmpty) throw new InvalidDataException("Incomplete engine frame.");
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally
        {
            await _lifetime.CancelAsync();
            await Task.WhenAll(_tasks);
        }

    }

    private async Task Execute(EngineRequest request, PendingCall cancellation)
    {
        var entered = false;
        try
        {
            await _execution.WaitAsync(cancellation.Token);
            entered = true;
            if (request.Method == "health")
            {
                await Respond(request.Id, await HealthAsync(services, cancellation.Token), false);
                return;
            }
            var options = services.GetRequiredService<IOptions<OtlpApiKeyOptions>>().Value;
            var credential = OtlpApiKeyValidator.Resolve(request.Credential, options);
            if (options.IsApiKeyMode && (credential is null || !credential.Roles.HasFlag(ApiKeyRoles.Read)))
            {
                var denied = ToolDispatcher.Error("unauthorized", "A project-bound read credential is required.");
                await Respond(request.Id, denied.Content, true);
                return;
            }
            var previous = Activity.Current;
            if (!ActivityContext.TryParse(request.Traceparent, request.Tracestate, out var parent)) Activity.Current = null;
            using var activity = NamiTelemetry.Source.StartActivity("tools/call " + request.Name, ActivityKind.Server, parent);
            activity?.SetTag("mcp.method.name", "tools/call");
            activity?.SetTag("gen_ai.tool.name", request.Name);
            var started = Stopwatch.GetTimestamp();
            try
            {
                var result = await services.GetRequiredService<NamiToolService>().InvokeAsync(
                    request.Name ?? "", request.Input, credential?.ProjectId ?? ProjectScope.DefaultProjectId,
                    cancellation.Token);
                if (result.IsError)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    if (result.Content.TryGetProperty("code", out var code)) activity?.SetTag(ErrorAttributes.Type, code.GetString());
                }
                await Respond(request.Id, result.Content, result.IsError);
            }
            finally
            {
                NamiTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                    new KeyValuePair<string, object?>("mcp.method.name", "tools/call"),
                    new KeyValuePair<string, object?>("gen_ai.tool.name", request.Name));
                Activity.Current = previous;
            }
        }
        catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested) { }
        catch (Exception error)
        {
            NamiTelemetry.RecordException(error, services.GetRequiredService<ILoggerFactory>().CreateLogger("nami"));
            var failure = ToolDispatcher.Error("internal_error", "The engine could not complete the request.");
            await Respond(request.Id, failure.Content, true);
        }
        finally
        {
            if (entered) _execution.Release();
            _pending.TryRemove(request.Id, out _);
            cancellation.Dispose();
        }
    }

    private async Task Respond(string id, JsonElement result, bool error)
    {
        await _writes.WaitAsync(_lifetime.Token);
        try
        {
            using var writer = new Utf8JsonWriter(_output);
            JsonSerializer.Serialize(writer, new EngineResponse
            {
                Id = id, Revision = ContractRevision.Value, Result = result, IsError = error
            }, ContractTypes.TypeInfo<EngineResponse>());
            writer.Flush();
            _output.Write("\n"u8);
            await _output.FlushAsync(_lifetime.Token);
        }
        finally { _writes.Release(); }
    }
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

    private sealed class PendingCall(CancellationToken parent) : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly CancellationTokenSource _source = CancellationTokenSource.CreateLinkedTokenSource(parent);
        private bool _disposed;

        internal CancellationToken Token { get { lock (_gate) return _source.Token; } }
        internal void Cancel() { lock (_gate) { if (!_disposed) _source.Cancel(); } }
        public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _source.Dispose(); } }
    }

    private static EngineRequest Decode(ReadOnlySequence<byte> frame)
    {
        using var document = JsonDocument.Parse(frame);
        if (ContractJson.Validate("Engine.EngineRequest", document.RootElement) is { } issue)
            throw new InvalidDataException("Invalid engine request: " + issue.Field);
        return document.RootElement.Deserialize(ContractTypes.TypeInfo<EngineRequest>())
               ?? throw new InvalidDataException("Missing engine request.");
    }

    internal static async Task<JsonElement> HealthAsync(IServiceProvider services, CancellationToken cancellation)
    {
        var started = Stopwatch.GetTimestamp();
        await services.GetRequiredService<INamiStore>().GetStorageStatsAsync(ProjectScope.DefaultProjectId, cancellation);
        return ContractJson.Serialize(new HealthReport
        {
            Status = HealthStatus.Healthy,
            TotalDurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            ContractRevision = ContractRevision.Value,
            Entries = new Dictionary<string, HealthCheckEntry>
            {
                ["duckdb"] = new() { Status = HealthStatus.Healthy, DurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds }
            }
        });
    }
}
