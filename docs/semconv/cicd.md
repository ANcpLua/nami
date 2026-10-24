# `cicd` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `cicd.pipeline.action.name` | `enum` | release_candidate |  | The kind of action a pipeline run is performing. | `"BUILD"`, `"RUN"`, `"SYNC"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.pipeline.name` | `string` | release_candidate |  | The human readable name of the pipeline within a CI/CD system. | `"Build and Test"`, `"Lint"`, `"Deploy Go Project"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.pipeline.result` | `enum` | release_candidate |  | The result of a pipeline run. | `"success"`, `"failure"`, `"timeout"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `cicd.pipeline.run.id` | `string` | release_candidate |  | The unique identifier of a pipeline run within a CI/CD system. | `"120912"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.pipeline.run.state` | `enum` | release_candidate |  | The pipeline run goes through these states during its lifecycle. | `"pending"`, `"executing"`, `"finalizing"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.pipeline.run.url.full` | `string` | release_candidate |  | The [URL](https://wikipedia.org/wiki/URL) of the pipeline run, providing the complete address in order to locate and identify the pipeline run. | `"https://github.com/open-telemetry/semantic-conventions/actions/runs/9753949763?pr=1075"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.pipeline.task.name` | `string` | release_candidate |  | The human readable name of a task within a pipeline. Task here most closely aligns with a [computing process](https://wikipedia.org/wiki/Pipeline_(computing)) in a pipeline. Other terms for tasks include commands, steps, and procedures. | `"Run GoLang Linter"`, `"Go Build"`, `"go-test"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.pipeline.task.run.id` | `string` | release_candidate |  | The unique identifier of a task run within a pipeline. | `"12097"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.pipeline.task.run.result` | `enum` | release_candidate |  | The result of a task run. | `"success"`, `"failure"`, `"timeout"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `cicd.pipeline.task.run.url.full` | `string` | release_candidate |  | The [URL](https://wikipedia.org/wiki/URL) of the pipeline task run, providing the complete address in order to locate and identify the pipeline task run. | `"https://github.com/open-telemetry/semantic-conventions/actions/runs/9753949763/job/26920038674?pr=1075"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.pipeline.task.type` | `enum` | release_candidate |  | The type of the task within a pipeline. | `"build"`, `"test"`, `"deploy"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.system.component` | `string` | release_candidate |  | The name of a component of the CI/CD system. | `"controller"`, `"scheduler"`, `"agent"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.worker.id` | `string` | release_candidate |  | The unique identifier of a worker within a CI/CD system. | `"abc123"`, `"10.0.1.2"`, `"controller"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.worker.name` | `string` | release_candidate |  | The name of a worker within a CI/CD system. | `"agent-abc"`, `"controller"`, `"Ubuntu LTS"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.worker.state` | `enum` | release_candidate |  | The state of a CI/CD worker / agent. | `"available"`, `"busy"`, `"offline"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cicd.worker.url.full` | `string` | release_candidate |  | The [URL](https://wikipedia.org/wiki/URL) of the worker, providing the complete address in order to locate and identify the worker. | `"https://cicd.example.org/worker/abc123"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `cicd.pipeline.action.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `BUILD` | release_candidate |  | The pipeline run is executing a build. |
| `RUN` | release_candidate |  | The pipeline run is executing. |
| `SYNC` | release_candidate |  | The pipeline run is executing a sync. |

### `cicd.pipeline.result` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `cancellation` | release_candidate |  | The pipeline run was cancelled, eg. by a user manually cancelling the pipeline run. |
| `error` | release_candidate |  | The pipeline run failed due to an error in the CI/CD system, eg. due to the worker being killed. |
| `failure` | release_candidate |  | The pipeline run did not finish successfully, eg. due to a compile error or a failing test. Such failures are usually detected by non-zero exit codes of the tools executed in the pipeline run. |
| `skip` | release_candidate |  | The pipeline run was skipped, eg. due to a precondition not being met. |
| `success` | release_candidate |  | The pipeline run finished successfully. |
| `timeout` | release_candidate |  | A timeout caused the pipeline run to be interrupted. |

### `cicd.pipeline.run.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `executing` | release_candidate |  | The executing state spans the execution of any run tasks (eg. build, test). |
| `finalizing` | release_candidate |  | The finalizing state spans from when the run has finished executing (eg. cleanup of run resources). |
| `pending` | release_candidate |  | The run pending state spans from the event triggering the pipeline run until the execution of the run starts (eg. time spent in a queue, provisioning agents, creating run resources). |

### `cicd.pipeline.task.run.result` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `cancellation` | release_candidate |  | The task run was cancelled, eg. by a user manually cancelling the task run. |
| `error` | release_candidate |  | The task run failed due to an error in the CI/CD system, eg. due to the worker being killed. |
| `failure` | release_candidate |  | The task run did not finish successfully, eg. due to a compile error or a failing test. Such failures are usually detected by non-zero exit codes of the tools executed in the task run. |
| `skip` | release_candidate |  | The task run was skipped, eg. due to a precondition not being met. |
| `success` | release_candidate |  | The task run finished successfully. |
| `timeout` | release_candidate |  | A timeout caused the task run to be interrupted. |

### `cicd.pipeline.task.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `build` | release_candidate |  | build |
| `deploy` | release_candidate |  | deploy |
| `test` | release_candidate |  | test |

### `cicd.worker.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `available` | release_candidate |  | The worker is not performing work for the CI/CD system. It is available to the CI/CD system to perform work on (online / idle). |
| `busy` | release_candidate |  | The worker is performing work for the CI/CD system. |
| `offline` | release_candidate |  | The worker is not available to the CI/CD system (disconnected / down). |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `cicd.pipeline.run.active` | updowncounter | `{run}` | release_candidate |  | The number of pipeline runs currently active in the system by state. |
| `cicd.pipeline.run.duration` | histogram | `s` | release_candidate |  | Duration of a pipeline run grouped by pipeline, state and result. |
| `cicd.pipeline.run.errors` | counter | `{error}` | release_candidate |  | The number of errors encountered in pipeline runs (eg. compile, test failures). |
| `cicd.system.errors` | counter | `{error}` | release_candidate |  | The number of errors in a component of the CI/CD system (eg. controller, scheduler, agent). |
| `cicd.worker.count` | updowncounter | `{worker}` | release_candidate |  | The number of workers on the CI/CD system by state. |
