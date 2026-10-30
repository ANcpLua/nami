using System.IO.Compression;
using DuckDB.NET.Data;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore
{
    public Task<TelemetrySnapshot> ReadSnapshotAsync(string projectId, string? snapshotId = null,
        CancellationToken ct = default) =>
        ExecuteWriteAsync<TelemetrySnapshot>(async (connection, token) =>
        {
            var capturedAt = _timeProvider.GetUtcNow();
            var now = NamiTimeConversions.ToUnixNanoUnsigned(capturedAt);
            await using var transaction = connection.BeginTransaction();
            if (snapshotId is not null)
            {
                await using var read = connection.CreateCommand();
                read.Transaction = transaction;
                read.CommandText =
                    "SELECT payload FROM read_snapshots WHERE project_id = $1 AND snapshot_id = $2 AND expires_unix_nano > $3";
                AddParameters(read, projectId, snapshotId, (decimal)now);
                byte[] payload;
                await using (var rows = read.ExecuteReader())
                {
                    if (!rows.Read()) throw new SnapshotUnavailableException();
                    // DuckDB exposes BLOBs as provider-owned streams, not necessarily byte[].
                    // Materialize while the reader is alive; its disposal releases native ownership.
                    payload = DuckDbValueReader.ReadBytes(rows, 0);
                }

                await using var compressed = new MemoryStream(payload, false);
                await using var decoded = new BrotliStream(compressed, CompressionMode.Decompress);
                var restored =
                    await JsonSerializer.DeserializeAsync(decoded,
                        StorageJsonSerializerContext.Default.TelemetrySnapshot, token).ConfigureAwait(false)
                    ?? throw new SnapshotUnavailableException();
                token.ThrowIfCancellationRequested();
                transaction.Commit();
                return restored;
            }

            // This writer-queue barrier observes every earlier accepted ingest job committed. Advance
            // the receipt frontier for signals we have actually seen; last_seen remains unchanged.
            await using (var frontier = connection.CreateCommand())
            {
                frontier.Transaction = transaction;
                frontier.CommandText =
                    "UPDATE signal_watermarks SET watermark_unix_nano = greatest(watermark_unix_nano, $2) WHERE project_id = $1";
                AddParameters(frontier, projectId, (decimal)now);
                frontier.ExecuteNonQuery();
            }

            var snapshot = new TelemetrySnapshot(Guid.NewGuid().ToString("N"), projectId, capturedAt,
                Read("spans", SpanStorageRow.SelectColumnList, "start_time_unix_nano, trace_id, span_id",
                    SpanStorageRow.MapFromReader),
                Read("logs", LogStorageRow.SelectColumnList, "time_unix_nano, log_id", LogStorageRow.MapFromReader),
                Read("metric_series", MetricSeriesRow.SelectColumnList, "series_id", MetricSeriesRow.MapFromReader),
                Read("signal_watermarks", SignalWatermarkRow.SelectColumnList, "signal, service_name",
                    SignalWatermarkRow.MapFromReader),
                Read("resource_observations", ResourceObservationRow.SelectColumnList,
                    "observed_unix_nano, sequence_number", ResourceObservationRow.MapFromReader),
                Read("attribute_stability", AttributeStabilityRow.SelectColumnList, "attribute_key",
                    AttributeStabilityRow.MapFromReader),
                Read("attribute_discovery", AttributeDiscoveryRow.SelectColumnList, "original_key",
                    AttributeDiscoveryRow.MapFromReader));
            await using var compressedPayload = new MemoryStream();
            await using (var encoder = new BrotliStream(compressedPayload, CompressionLevel.Fastest, true))
                await JsonSerializer.SerializeAsync(encoder, snapshot,
                    StorageJsonSerializerContext.Default.TelemetrySnapshot, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var stored = new ReadSnapshotRow
            {
                ProjectId = projectId,
                SnapshotId = snapshot.Id,
                ExpiresUnixNano = NamiTimeConversions.ToUnixNanoUnsigned(capturedAt + _snapshotLifetime),
                Payload = compressedPayload.ToArray()
            };
            await using (var write = connection.CreateCommand())
            {
                write.Transaction = transaction;
                write.CommandText = ReadSnapshotRow.InsertSql;
                ReadSnapshotRow.AddParameters(write, stored);
                write.ExecuteNonQuery();
            }

            await using (var sweep = connection.CreateCommand())
            {
                sweep.Transaction = transaction;
                sweep.CommandText = "DELETE FROM read_snapshots WHERE expires_unix_nano <= $1";
                AddParameters(sweep, (decimal)now);
                sweep.ExecuteNonQuery();
            }

            token.ThrowIfCancellationRequested();
            transaction.Commit();
            return snapshot;

            List<T> Read<T>(string table, string columns, string order, Func<DbDataReader, T> mapper)
            {
                using var read = connection.CreateCommand();
                read.Transaction = transaction;
                read.CommandText = "SELECT " + columns + " FROM " + table + " WHERE project_id = $1 ORDER BY " + order;
                AddParameters(read, projectId);
                using var rows = read.ExecuteReader();
                var result = new List<T>();
                while (rows.Read())
                {
                    token.ThrowIfCancellationRequested();
                    result.Add(mapper(rows));
                }

                return result;
            }
        }, cancelAfterClaim: true, ct);

    private void RecordIngestion(DuckDBConnection connection, DbTransaction transaction, string signal,
        IEnumerable<ArrivingResource> arriving, CancellationToken cancellationToken)
    {
        var received = NamiTimeConversions.ToUnixNanoUnsigned(_timeProvider.GetUtcNow());
        var seenSignals = new HashSet<(string Project, string Service)>();
        var seenAttributes = new HashSet<(string Project, string Key)>();
        var seenRenames = new HashSet<(string Project, string Key)>();
        var lastResources = new Dictionary<(string Project, string Service), string>();
        foreach (var resource in arriving.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var service = resource.ServiceName ?? "unknown";
            var state = new SignalWatermarkRow
            {
                ProjectId = resource.ProjectId,
                Signal = signal,
                ServiceName = service,
                LastSeenUnixNano = received,
                WatermarkUnixNano = received
            };
            if (seenSignals.Add((resource.ProjectId, service)))
            {
                using var write = connection.CreateCommand();
                write.Transaction = transaction;
                write.CommandText = SignalWatermarkRow.InsertSql;
                SignalWatermarkRow.AddParameters(write, state);
                write.ExecuteNonQuery();
            }

            foreach (var content in new[]
                     {
                         resource.AttributesJson, resource.RecordAttributesJson, resource.ScopeAttributesJson
                     })
            {
                if (content is null) continue;
                using var attributes = JsonDocument.Parse(content);
                foreach (var property in attributes.RootElement.EnumerateObject()
                             .Where(property => seenAttributes.Add((resource.ProjectId, property.Name))))
                {
                    var stability = new AttributeStabilityRow
                    {
                        ProjectId = resource.ProjectId,
                        AttributeKey = property.Name,
                        Stability = AttributeStabilityWire.Name(property.Name)
                    };
                    using var write = connection.CreateCommand();
                    write.Transaction = transaction;
                    write.CommandText = AttributeStabilityRow.InsertSql;
                    AttributeStabilityRow.AddParameters(write, stability);
                    write.ExecuteNonQuery();
                }
            }

            foreach (var rename in resource.Renames ?? [])
            {
                if (!seenRenames.Add((resource.ProjectId, rename.OriginalKey))) continue;
                var firstDot = rename.OriginalKey.IndexOf('.', StringComparison.Ordinal);
                var discovery = new AttributeDiscoveryRow
                {
                    ProjectId = resource.ProjectId,
                    OriginalKey = rename.OriginalKey,
                    CanonicalKey = rename.CanonicalKey,
                    Namespace = firstDot < 0 ? rename.OriginalKey : rename.OriginalKey[..firstDot],
                    FirstSeenUnixNano = received,
                    LastSeenUnixNano = received
                };
                using var write = connection.CreateCommand();
                write.Transaction = transaction;
                write.CommandText = AttributeDiscoveryRow.InsertSql;
                AttributeDiscoveryRow.AddParameters(write, discovery);
                write.ExecuteNonQuery();
            }

            var json = resource.AttributesJson ?? "{}";
            var resourceKey = (resource.ProjectId, service);
            if (lastResources.TryGetValue(resourceKey, out var lastResource) && lastResource == json) continue;
            lastResources[resourceKey] = json;
            using var sequenceQuery = connection.CreateCommand();
            sequenceQuery.Transaction = transaction;
            sequenceQuery.CommandText =
                "SELECT coalesce(max(sequence_number), 0) + 1 FROM resource_observations WHERE project_id = $1 AND service_name = $2";
            AddParameters(sequenceQuery, resource.ProjectId, service);
            var sequence = Convert.ToInt64(sequenceQuery.ExecuteScalar(), CultureInfo.InvariantCulture);
            using var query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText =
                "SELECT attributes_json FROM resource_observations WHERE project_id = $1 AND service_name = $2 ORDER BY sequence_number DESC LIMIT 1";
            AddParameters(query, resource.ProjectId, service);
            if (query.ExecuteScalar() as string == json) continue;
            var observation = new ResourceObservationRow
            {
                ProjectId = resource.ProjectId,
                ServiceName = service,
                ObservationId = Guid.NewGuid().ToString("N"),
                ObservedUnixNano = received,
                SequenceNumber = sequence,
                AttributesJson = json
            };
            using var record = connection.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = ResourceObservationRow.InsertSql;
            ResourceObservationRow.AddParameters(record, observation);
            record.ExecuteNonQuery();
        }
    }


}
