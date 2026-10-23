import "@typespec/events";
import "@typespec/http";
import "@typespec/sse";

import "../common/errors.tsp";
import "../models/runner.tsp";

using TypeSpec.Events;
using TypeSpec.Http;
using TypeSpec.SSE;

using Nami.Api.Contracts.Common.Errors;

namespace Nami.Api.Contracts.Runner {
  @events
  union RunnerResourceEvents {
    message: RunnerResourceState,
  }

  @events
  union RunnerLogEvents {
    message: RunnerLogLine,
  }

  @doc("Served only by the local nami runner on a loopback (127.0.0.1) listener. It rejects every request whose remote address or Host is not loopback, and checks the Origin header only on the mutating restart and stop operations, so these operations carry no API-key authentication.")
  @route("/runner/resources")
  @tag("Runner resources")
  interface RunnerResourcesApi {
    @get snapshot():
      | RunnerResourceState[]
      | ForbiddenError
      | ServiceUnavailableError
      | InternalServerError;

    @get
    @route("/stream")
    stream():
      | SSEStream<RunnerResourceEvents>
      | ForbiddenError
      | ServiceUnavailableError
      | InternalServerError;

    @get
    @route("/{resource}/logs")
    logs(@path resource: string):
      | RunnerLogLine[]
      | ForbiddenError
      | NotFoundError
      | ServiceUnavailableError
      | InternalServerError;

    @get
    @route("/{resource}/logs/stream")
    streamLogs(
      @path resource: string,
      @header("Last-Event-ID") lastEventId?: string,
    ):
      | SSEStream<RunnerLogEvents>
      | ForbiddenError
      | NotFoundError
      | ServiceUnavailableError
      | InternalServerError;

    @post
    @route("/{resource}/restart")
    restart(@path resource: string):
      | RunnerActionAccepted
      | ForbiddenError
      | NotFoundError
      | ConflictError
      | ServiceUnavailableError
      | InternalServerError;

    @post
    @route("/{resource}/stop")
    stop(@path resource: string):
      | RunnerActionAccepted
      | ForbiddenError
      | NotFoundError
      | ConflictError
      | ServiceUnavailableError
      | InternalServerError;
  }
}

