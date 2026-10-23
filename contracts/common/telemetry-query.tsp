import "./types.tsp";
using TypeSpec.OpenAPI;
namespace Nami.Api.Contracts.Common;

@doc("Opaque project-scoped failure fingerprint issued and verified by the collector.")
@minLength(1)
@maxLength(512)
scalar FailureId extends string;

@doc("Signed self-contained project-scoped verification token issued by the collector.")
@minLength(1)
@maxLength(8192)
scalar VerifyId extends string;

@doc("A comparison operand keeps its JSON type: a number or boolean is never coerced to a string.")
union MatcherValue { text: string, number: float64, boolean: boolean }

@doc("Supported predicate operators. eq, neq and in compare typed values: a number equals a number of the same value, a string the same string and a boolean the same boolean, so 1 does not equal \"1\". prefix and contains match the value's text; gt, gte, lt and lte compare numbers.")
enum MatcherOp { eq, neq, `in`, prefix, contains, exists, gt, gte, lt, lte }

@doc("Attribute namespace in which the predicate is evaluated.")
enum MatcherScope { span, resource, log }

@doc("One attribute predicate. It tests the record's own span or log attributes unless `scope` is `resource`; every matcher of a list must hold.")
model TelemetryMatcher {
  @doc("Attribute key, e.g. `http.route`, or `service.name` for the record's service.")
  @minLength(1)
  @maxLength(256)
  key: string;
  @doc("Comparison operator: `in` takes `values`, `exists` takes no operand and every other operator takes `value`.")
  `op`: MatcherOp;
  @doc("Operand of every operator except `in` and `exists`. It keeps its JSON type, so the number 1 does not equal the string `1`.")
  value?: MatcherValue;
  @doc("Operands of `in`, 1 to 256; the attribute must equal one of them.")
  @minItems(1)
  @maxItems(256)
  values?: MatcherValue[];
  @doc("Where `key` is looked up: `span` or `log` for the record's own attributes (the default), `resource` for its resource attributes, such as `service.version` or `deployment.environment.name`.")
  scope?: MatcherScope;
}

@doc("Absolute half-open window applied by the collector.")
model ToolWindow {
  @doc("Start of the window (inclusive), RFC 3339.")
  @encodedName("application/json", "start_time")
  startTime: Nami.Api.Contracts.Common.Timestamp;
  @doc("End of the window (exclusive), RFC 3339.")
  @encodedName("application/json", "end_time")
  endTime: Nami.Api.Contracts.Common.Timestamp;
}

@doc("What the answer rests on; unknown counts are null, never an invented zero.")
model ToolCoverage {
  @doc("Records the read examined.")
  examined: Count | null;
  @doc("Records or items that matched the request; null when the read was cut, because the total is then unknown.")
  matched: Count | null;
  @doc("Items this answer returns; null when the response budget shortened it.")
  returned: Count | null;
  @doc("Whether matching items were left out of this answer, by its page size or the response budget; `next_cursor` continues them where there is one.")
  truncated: boolean;
  @doc("Whether the read held every record the answer could come from. False is a partial answer: the read stopped at its row limit.")
  @encodedName("application/json", "scan_complete")
  scanComplete: boolean;
  @doc("End of the window the answer covers.")
  @encodedName("application/json", "as_of")
  asOf: Nami.Api.Contracts.Common.Timestamp;
}

@doc("ToolReadContext contract.")
model ToolReadContext {
  window: ToolWindow;
  coverage: ToolCoverage;
  @doc("Item totals by paged collection path. Omitted totals are unknown, not zero.")
  @encodedName("application/json", "page_totals")
  pageTotals?: Record<Nami.Api.Contracts.Common.JsonInteger>;
}

