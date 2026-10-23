// =============================================================================
// Shared write base for user-created collector resources
// =============================================================================
// Every resource a client creates through the collector API (annotations, saved
// queries) extends `ResourceBase`, and every write operation uses the headers
// below. Server-assigned members carry `Lifecycle.Read` visibility, so the
// create and update request models never accept them.
//
// Concurrency: a read returns `etag`; PATCH and DELETE send it back in
// `If-Match`. A missing `If-Match` is refused with 428, a stale one with 412.
// Retries: a POST that creates a resource takes `Idempotency-Key`; a repeat
// with the same key and body answers the resource it created, as it is now,
// instead of a duplicate, and creates it again once it was deleted.
// =============================================================================

import "@typespec/http";
import "./types.tsp";

using TypeSpec.Http;

namespace Nami.Api.Contracts.Common;

@doc("Opaque, server-assigned identifier of a user-created collector resource.")
@minLength(1)
@maxLength(128)
scalar ResourceId extends string;

@doc("""
  Opaque entity tag of one resource version. It changes on every successful
  update; send it back in `If-Match` to update or delete that version.
  """)
@minLength(1)
@maxLength(128)
scalar ETag extends string;

@doc("""
  Collector API key roles. Every key sent in `x-otlp-api-key` carries one or
  more roles: `ingest` accepts OTLP, `read` serves the query routes, `write`
  creates and updates user resources, and `delete` deletes them. Roles are
  independent, so a leaked ingest key can neither read, change nor delete
  anything.
  """)
enum CollectorKeyRole {
  @doc("Send OTLP traces, logs and metrics.")
  ingest: "ingest",

  @doc("Read telemetry and user resources.")
  read: "read",

  @doc("Create and update annotations and saved queries.")
  write: "write",

  @doc("Delete annotations and saved queries. Never deletes telemetry; retention does.")
  delete: "delete",
}

@doc("Base of every user-created collector resource.")
model ResourceBase {
  @doc("Server-assigned identifier.")
  @visibility(Lifecycle.Read)
  id: ResourceId;

  @doc("When the resource was created.")
  @visibility(Lifecycle.Read)
  @encodedName("application/json", "created_at")
  createdAt: Nami.Api.Contracts.Common.Timestamp;

  @doc("When the resource was last changed.")
  @visibility(Lifecycle.Read)
  @encodedName("application/json", "updated_at")
  updatedAt: Nami.Api.Contracts.Common.Timestamp;

  @doc("Entity tag of this version; also served as the `ETag` response header.")
  @visibility(Lifecycle.Read)
  etag: ETag;
}

@doc("Retry key for a creating POST.")
model IdempotencyKeyHeader {
  @doc("""
    Client-chosen retry key, unique per intended create. A repeat with the same
    key and body answers the resource the first one created, in its current
    version, or creates it again once it was deleted; the same key with a
    different body is refused with 409 while that resource exists. Deleting the
    resource releases its key; keys expire after 24 hours.
    """)
  @header("Idempotency-Key")
  @minLength(1)
  @maxLength(255)
  idempotencyKey?: string;
}

@doc("Optimistic-concurrency precondition for PATCH and DELETE.")
model IfMatchHeader {
  @doc("""
    The `etag` of the version being changed. A request without it is refused
    with 428 Precondition Required; a request whose tag is no longer current is
    refused with 412 Precondition Failed.
    """)
  @header("If-Match")
  ifMatch: ETag;
}
