# Workstream 7: storage and ingestion

`INamiStore.ReadSnapshotAsync(projectId, snapshotId, cancellationToken)` captures spans, logs, metric
series, receipt-time signal states, resource history and attribute stability in one DuckDB transaction.
A new snapshot is streamed through the storage source-generated JSON context into Brotli-compressed BLOB storage and saved under a random
opaque handle. Existing handles reload the same immutable rows and captured clock, including after a
process restart. Lookup always includes the verified project. Handles expire after 30 minutes by default;
`DuckDbStore` accepts a `TimeProvider` and optional snapshot lifetime for deterministic testing.

Ingestion writes signal watermarks and last-seen receipt times in the same transaction as telemetry.
Late or future-dated producer timestamps cannot advance receipt-time last-seen. On a new snapshot, its writer-queue barrier advances the watermark to the captured clock for signals
actually observed, after all earlier accepted ingest writes have committed; it leaves last-seen unchanged.
The watermark is the committed receipt frontier, not an assertion that a producer will never send a late record. Tools retain
that distinction through `freshness.complete`. Resource changes are ordered by a persisted per-service
sequence even when several resources arrive at the same clock tick.

The generated registry owns persistence, deprecation, attribute keys and stability. Pre-normalization
keys are retained only as provenance in project-scoped `attribute_discovery` rows, with their canonical
successor (or null for a dropped deprecated key); telemetry attributes never contain a duplicate old key.
Snapshots expose these rows as `AttributeDiscoveries`, including after restart. The decoder carries
provenance through resource, scope, span/event/link, log and metric/exemplar/instrument attributes.
 The former generated
NuGet catalog and old annotation/saved-query/idempotency write surfaces have been removed. Scope metadata,
OTLP values and the existing exception sanitizer remain in the existing storage encoding. Missing
`service.name` increments an explicit counter and retains the record as `unknown`.

`attribute_stability` persists stability next to discovered attributes; traces expose the optional
contract attribute `stability` value. Resource history supports revision/version/environment/pipeline
change queries without relying on the removed annotation or failure-status write APIs.

Validation and tests run after generated semconv output is available. No test files were written during
the implementation round.

## Integration verification

The coordinator built `src/nami/nami.csproj` in Release successfully with zero warnings and zero errors
after integrating the generated contracts and registry (host build 7). The sequential runtime and
conformance test phase follows this implementation pass.
