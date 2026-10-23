import "@typespec/http";

using TypeSpec.Http;

namespace Nami.Api.Contracts.Runner;

@doc("Lifecycle state of a resource supervised by the local nami runner.")
enum RunnerResourceLifecycle {
  pending: "pending",
  starting: "starting",
  ready: "ready",
  stopping: "stopping",
  stopped: "stopped",
  failed: "failed",
}

@doc("Kind of process supervised by the local nami runner.")
enum RunnerResourceKind {
  collector: "collector",
  project: "project",
  command: "command",
}

@doc("Current client-visible state of one runner resource.")
model RunnerResourceState {
  name: string;
  lifecycle: RunnerResourceLifecycle;
  timestamp: Nami.Api.Contracts.Common.Timestamp;
  kind?: RunnerResourceKind;

  @minValue(1)
  @maxValue(65535)
  @encodedName("application/json", "allocated_port")
  allocatedPort?: int32;

  endpoint?: Nami.Api.Contracts.Common.UrlString;
  @encodedName("application/json", "last_error")
  lastError?: string;
}

@doc("A runner lifecycle action was accepted and continues asynchronously.")
model RunnerActionAccepted {
  @statusCode statusCode: 202;
}

@doc("Origin stream for one captured child-process log line.")
enum RunnerLogStream {
  stdout: "out",
  stderr: "err",
}

@doc("One bounded child-process log line exposed by the runner.")
model RunnerLogLine {
  resource: string;
  stream: RunnerLogStream;
  line: string;
}
