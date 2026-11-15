import "../common/types.tsp";

namespace Nami.Api.Contracts.Engine;

@doc("One complete private response. A tool refusal is an explicit error value, never a lost reply.")
model EngineResponse {
  id: string;
  revision: string;
  result: unknown;
  @encodedName("application/json", "is_error") isError: boolean;
}

@doc("Fixed bounds for the private protocol, shared by both implementations.")
model EngineLimits {
  @encodedName("application/json", "max_frame_bytes")
  maxFrameBytes: int32 = 1048576;
  @encodedName("application/json", "max_pending") maxPending: int32 = 32;
  workers: int32 = 2;
}

// A property list spread into each command, not a type of its own: as an alias of a model
// expression it has no name, so no emitter generates a class, an interface or a schema for it.
alias EngineEnvelope = { id: string; revision: string; };
model EngineCall {
  ...EngineEnvelope;
  method: "call";
  name: string;
  input: unknown;
  credential: string | null;
  traceparent: string | null;
  tracestate: string | null;
}
model EngineHealth { ...EngineEnvelope; method: "health"; }
model EngineCancel { ...EngineEnvelope; method: "cancel"; }
@doc("One private engine request. Credentials select the project; client tool arguments never do.")
@discriminated(#{envelope: "none", discriminatorPropertyName: "method"})
union EngineCommand { call: EngineCall, health: EngineHealth, cancel: EngineCancel }
