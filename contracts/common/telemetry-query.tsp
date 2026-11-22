import "./types.tsp";
namespace Nami.Api.Contracts.Common;

@doc("Absolute RFC 3339 instant or a negative relative duration such as -15m, -2h, -7d or -30s.")
@minLength(1)
@maxLength(256)
scalar TimeSpec extends string;

@doc("Opaque, project-scoped identity of an immutable telemetry snapshot.")
@minLength(1)
@maxLength(256)
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
  @doc("Only telemetry from the service with exactly this service.name (case-sensitive). sources_status lists the names; an unknown name is refused with the known ones.")
  @minLength(1)
  @maxLength(256)
  service?: string;

  @doc("Start of the window: a negative duration relative to now (-30s, -15m, -2h, -7d) or a past RFC 3339 time (2026-11-22T13:00:00Z). The window ends now (when the snapshot was taken). Default -60m. traces_get reads a trace whole whatever since says.")
  since?: TimeSpec = "-60m";

  @doc("Return only these members of each result row (of the trace for traces_get). Default: all members. Unknown fields are refused with the allowed field names.")
  @maxItems(128)
  fields?: string[];

  @doc("Size budget for one page, in UTF-8 bytes of JSON, 1024 to 262144, default 32768. Rows that do not fit move to the next page (see next_cursor). Raise it only when a single row or span does not fit.")
  @minValue(1024)
  @maxValue(262144)
  @encodedName("application/json", "max_bytes")
  maxBytes?: int32 = 32768;

  @doc("ID of the frozen view of the data an earlier answer returned. Pass it to get byte-identical answers across tools in one investigation. Expires 30 minutes after it was taken; then call without it. With cursor, snapshot_id is optional: the cursor carries its snapshot.")
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
