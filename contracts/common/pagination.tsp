// =============================================================================
// Nami API cursor pagination
// =============================================================================
// Every list operation pages the same way: it is a `@list` operation, spreads
// `CursorPageQuery` (one page-size rule, one cursor parameter) and answers a
// page whose items carry `@pageItems` and whose opaque `Cursor` carries
// `@continuationToken`. The cursor is a token the client passes back, never a
// link it follows, and the last page omits it.
// =============================================================================

import "@typespec/http";
import "./types.tsp";

using TypeSpec.Http;

namespace Nami.Api.Contracts.Common.Pagination;

@doc("Page size of a list operation: at least 1 and at most 1000 items.")
@minValue(1)
@maxValue(1000)
scalar PageLimit extends int32;

// The query parameters of every list operation. An alias, not a model, so it
// reaches the wire only as the parameters of the operations that spread it.
alias CursorPageQuery = {
  @doc("Page size; 100 when omitted.")
  @query
  @pageSize
  limit?: PageLimit = 100;

  @doc("The `next_cursor` of the previous page of the same query; omitted for the first page.")
  @query
  @continuationToken
  cursor?: Cursor;
};

@doc("One page of a list operation.")
@friendlyName("CursorPage{name}", T)
model CursorPage<T> {
  @doc("The items of this page.")
  @pageItems
  items: T[];

  @doc("Cursor of the next page, passed back as the `cursor` query parameter. Absent on the last page.")
  @encodedName("application/json", "next_cursor")
  @continuationToken
  nextCursor?: Cursor;

  @doc("Whether more items follow this page. `next_cursor` is present exactly when this is true.")
  @encodedName("application/json", "has_more")
  hasMore: boolean;
}
