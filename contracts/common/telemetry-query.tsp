import "./types.tsp";
using TypeSpec.OpenAPI;
namespace Nami.Api.Contracts.Common;

@doc("Absolute RFC 3339 instant or a negative relative duration such as -15m, -2h, -7d or -30s.")
@minLength(1) @maxLength(256)
scalar TimeSpec extends string;

@doc("Opaque, project-scoped identity of an immutable telemetry snapshot.")
@minLength(1) @maxLength(256)
scalar SnapshotId extends string;

@doc("Evidence completeness for the requested window. Unknown counts remain null.")
model Freshness {
  @doc("Ingest is complete through this instant for every signal read.")
  watermark: Timestamp;
  @doc("True only when the requested window ends at or before the watermark.")
  complete: boolean;
  @doc("Records examined, or null when unknown.")
  examined: Count | null;
  @doc("Matching records, or null when the read was cut short.")
  matched: Count | null;
  @doc("Items returned, or null when unknown.")
  returned: Count | null;
  @doc("True when a row limit, page limit or byte budget omitted matching items.")
  truncated: boolean;
}

@doc("Arguments shared by every read-only tool.")
model ToolReadInput {
  @doc("Filter by the producer's service.name resource attribute.")
  @minLength(1) @maxLength(256)
  service?: string;
  @doc("Inclusive window start; defaults to one hour before the snapshot's observation time.")
  since?: TimeSpec;
  @doc("Requested fields. Unknown fields are refused with the allowed field names.")
  @maxItems(128)
  fields?: string[];
  @doc("Maximum UTF-8 bytes in structuredContent, from 1024 to 262144.")
  @minValue(1024) @maxValue(262144)
  @encodedName("application/json", "max_bytes")
  maxBytes?: int32 = 32768;
  @doc("Reuse a snapshot returned by an earlier call for byte-identical reads.")
  @encodedName("application/json", "snapshot_id")
  snapshotId?: SnapshotId;
  @doc("Opaque next_cursor from the preceding page; keep all other arguments unchanged.")
  cursor?: Cursor;
}

@doc("Evidence metadata returned with every successful tool result.")
model ToolResult {
  freshness: Freshness;
  @encodedName("application/json", "snapshot_id")
  snapshotId: SnapshotId;
  @encodedName("application/json", "next_cursor")
  nextCursor?: Cursor;
}
