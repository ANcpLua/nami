#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
API_HOST_PORT="${NAMI_SMOKE_API_PORT:-5199}"
API_CONTAINER_PORT=5119
OTLP_HTTP_HOST_PORT="${NAMI_SMOKE_OTLP_HTTP_PORT:-4318}"
GRPC_HOST_PORT="${NAMI_SMOKE_GRPC_PORT:-4317}"
API_BASE="http://127.0.0.1:$API_HOST_PORT/"
OTLP_HTTP_BASE="http://127.0.0.1:$OTLP_HTTP_HOST_PORT/"
GRPC_BASE="http://127.0.0.1:$GRPC_HOST_PORT/"
SDK_CONFORMANCE_APP_PORT=5299
SDK_CONFORMANCE_APP_BASE="http://127.0.0.1:$SDK_CONFORMANCE_APP_PORT/"
SMOKE_PLATFORM="${NAMI_SMOKE_PLATFORM:-linux/amd64}"
SMOKE_ID="$$-${RANDOM}"
IMAGE_NAME="nami-collector-aot-smoke:$SMOKE_ID"
CONTAINER_NAME="nami-collector-aot-smoke-$SMOKE_ID"
VOLUME_NAME="nami-collector-aot-smoke-$SMOKE_ID"
DRIVER_PROJECT="$REPO_ROOT/eng/tools/CollectorAotSmoke/CollectorAotSmoke.csproj"
SDK_CONFORMANCE_PROJECT="$REPO_ROOT/tests/Nami.Sdk.Conformance/Nami.Sdk.Conformance.csproj"
SDK_CONFORMANCE_DRIVER_PROJECT="$REPO_ROOT/eng/tools/NamiSdkConformance/NamiSdkConformance.csproj"
SDK_CONFORMANCE_PUBLISH_DIRECTORY="$REPO_ROOT/artifacts/publish/Nami.Sdk.Conformance/release"
AUTH_KEY="aot-smoke-api-key-$SMOKE_ID"

cleanup() {
  local status=$?
  set +e

  if ((status != 0)) && docker container inspect "$CONTAINER_NAME" >/dev/null 2>&1; then
    echo "[smoke] Docker container state:"
    docker container inspect --format '{{json .State}}' "$CONTAINER_NAME" 2>&1
    echo "[smoke] Collector container logs:"
    docker logs "$CONTAINER_NAME" 2>&1
  fi

  docker container rm --force "$CONTAINER_NAME" >/dev/null 2>&1
  docker image rm --force "$IMAGE_NAME" >/dev/null 2>&1
  docker volume rm --force "$VOLUME_NAME" >/dev/null 2>&1
  exit "$status"
}
trap cleanup EXIT

fail() {
  echo "[smoke] FAIL: $*" >&2
  exit 1
}

phase() {
  echo
  echo "[smoke] === $* ==="
}

run_driver() {
  dotnet run \
    --project "$DRIVER_PROJECT" \
    --configuration Release \
    --no-build \
    -- "$@"
}

run_sdk_conformance() {
  local executable="$SDK_CONFORMANCE_PUBLISH_DIRECTORY/Nami.Sdk.Conformance"
  local service_name="nami-sdk-conformance-$SMOKE_ID"

  [[ -x "$executable" ]] \
    || fail "Native AOT Nami.Telemetry.Hosting conformance executable is missing: $executable"

  dotnet run \
    --project "$SDK_CONFORMANCE_DRIVER_PROJECT" \
    --configuration Release \
    --no-build \
    -- \
    "$executable" \
    "$SDK_CONFORMANCE_APP_BASE" \
    "$API_BASE" \
    "$OTLP_HTTP_BASE" \
    "$service_name"
}

wait_for_ready() {
  local health_status="starting"
  local running

  for ((attempt = 0; attempt < 120; attempt++)); do
    running="$(docker container inspect --format '{{.State.Running}}' "$CONTAINER_NAME" 2>/dev/null)" \
      || fail "collector container disappeared during startup"
    [[ "$running" == "true" ]] || fail "collector container exited during startup"

    health_status="$(docker container inspect --format '{{.State.Health.Status}}' "$CONTAINER_NAME" 2>/dev/null)" \
      || fail "collector Docker health state is unavailable"
    if [[ "$health_status" == "healthy" ]] \
      && curl -fsS --max-time 2 "${API_BASE}health" >/dev/null 2>&1 \
      && curl -fsS --max-time 2 "${OTLP_HTTP_BASE}health" >/dev/null 2>&1; then
      echo "[smoke] Docker HEALTHCHECK, product HTTP, and OTLP HTTP listeners are ready"
      return
    fi

    sleep 0.5
  done

  fail "/health never became ready (Docker health: $health_status)"
}

assert_runtime_identity() {
  local data_owner_uid
  local pid_gid
  local pid_uid
  local nami_gid
  local nami_uid

  pid_uid="$(docker exec "$CONTAINER_NAME" awk '/^Uid:/ { print $2; exit }' /proc/1/status)"
  pid_gid="$(docker exec "$CONTAINER_NAME" awk '/^Gid:/ { print $2; exit }' /proc/1/status)"
  nami_uid="$(docker exec "$CONTAINER_NAME" id -u nami)"
  nami_gid="$(docker exec "$CONTAINER_NAME" id -g nami)"
  [[ "$pid_uid" != "0" && "$pid_uid" == "$nami_uid" ]] \
    || fail "collector PID 1 UID is $pid_uid, expected nami UID $nami_uid"
  [[ "$pid_gid" == "$nami_gid" ]] \
    || fail "collector PID 1 GID is $pid_gid, expected nami GID $nami_gid"

  data_owner_uid="$(docker exec "$CONTAINER_NAME" stat -c '%u' /data)"
  [[ "$data_owner_uid" == "$nami_uid" ]] \
    || fail "/data owner UID is $data_owner_uid, expected nami UID $nami_uid"
  echo "[smoke] PID 1 runs as nami and owns /data"
}

start_container() {
  local auth_mode="$1"
  local -a docker_args=(
    run --detach
    --platform "$SMOKE_PLATFORM"
    --name "$CONTAINER_NAME"
    --publish "127.0.0.1:$API_HOST_PORT:$API_CONTAINER_PORT"
    --publish "127.0.0.1:$OTLP_HTTP_HOST_PORT:4318"
    --publish "127.0.0.1:$GRPC_HOST_PORT:4317"
    --mount "type=volume,source=$VOLUME_NAME,target=/data"
    --env "PORT=$API_CONTAINER_PORT"
    --env "NAMI_OTLP_AUTH_MODE=$auth_mode"
  )

  if [[ "$auth_mode" == "ApiKey" ]]; then
    docker_args+=(--env "NAMI_OTLP_PROJECT_KEYS={\"default\":[{\"key\":\"$AUTH_KEY\",\"roles\":[\"ingest\",\"read\"]}]}")
  fi

  docker_args+=("$IMAGE_NAME")
  docker "${docker_args[@]}" >/dev/null
}

stop_cleanly() {
  local phase_name="$1"
  local exit_code
  local running

  docker stop --time 60 "$CONTAINER_NAME" >/dev/null \
    || fail "$phase_name docker stop failed"
  running="$(docker container inspect --format '{{.State.Running}}' "$CONTAINER_NAME")"
  exit_code="$(docker container inspect --format '{{.State.ExitCode}}' "$CONTAINER_NAME")"
  [[ "$running" == "false" ]] || fail "$phase_name left the collector running"
  [[ "$exit_code" == "0" ]] || fail "$phase_name exited with status $exit_code"
  echo "[smoke] $phase_name completed with exit status 0"
}

for endpoint in "${API_BASE}health" "${OTLP_HTTP_BASE}health" "${SDK_CONFORMANCE_APP_BASE}conformance"; do
  if curl -sS --max-time 2 "$endpoint" >/dev/null 2>&1; then
    fail "something already listens at $endpoint; stop it or choose another smoke host port"
  fi
done

phase "Build the checked-in OTLP wire driver"
dotnet build "$DRIVER_PROJECT" --configuration Release --nologo
dotnet build "$SDK_CONFORMANCE_DRIVER_PROJECT" --configuration Release --nologo

phase "Publish the released-Nami.Telemetry.Hosting conformance app with Native AOT"
mkdir -p "$SDK_CONFORMANCE_PUBLISH_DIRECTORY"
dotnet publish \
  "$SDK_CONFORMANCE_PROJECT" \
  --configuration Release \
  --output "$SDK_CONFORMANCE_PUBLISH_DIRECTORY" \
  --self-contained true \
  --nologo \
  --verbosity minimal \
  -warnaserror

phase "Build the collector deployment image"
docker build \
  --progress=plain \
  --platform "$SMOKE_PLATFORM" \
  --file "$REPO_ROOT/services/nami.collector/Dockerfile" \
  --tag "$IMAGE_NAME" \
  "$REPO_ROOT"

docker volume create "$VOLUME_NAME" >/dev/null
docker run --rm \
  --platform "$SMOKE_PLATFORM" \
  --entrypoint /bin/sh \
  --mount "type=volume,source=$VOLUME_NAME,target=/data" \
  "$IMAGE_NAME" \
  -c 'chown 0:0 /data && chmod 0755 /data && test "$(stat -c "%u:%g:%a" /data)" = "0:0:755"'
echo "[smoke] Root-owned 0755 deployment volume initialized"

phase "Start the unsecured image with OTLP HTTP :4318 and gRPC :4317 enabled"
start_container Unsecured
wait_for_ready
assert_runtime_identity


phase "Lanes 1-5: HTTP JSON/protobuf, gRPC traces/logs, and metrics ingest"
run_driver wire "$API_BASE" "$OTLP_HTTP_BASE" "$GRPC_BASE"
docker exec "$CONTAINER_NAME" test -s /data/nami.duckdb \
  || fail "collector did not persist its DuckDB file on /data"
echo "[smoke] Native storage file verified"

phase "Stock OTel SDK default export with only OTEL_EXPORTER_OTLP_ENDPOINT"
(
  while IFS='=' read -r variable _; do
    case "$variable" in
      OTEL_EXPORTER_OTLP_*) unset "$variable" ;;
    esac
  done < <(env)
  export OTEL_EXPORTER_OTLP_ENDPOINT="$GRPC_BASE"
  run_driver stock-sdk "$API_BASE"
)

phase "Released Nami.Telemetry.Hosting builder.AddNami Native AOT conformance"
run_sdk_conformance

phase "Lane 7: persistent trace survives a container restart"
stop_cleanly "Initial graceful stop"
docker start "$CONTAINER_NAME" >/dev/null
wait_for_ready
assert_runtime_identity
run_driver persistence "$API_BASE"
echo "[smoke] Trace persisted across container restart"
stop_cleanly "Persistent-container graceful stop"

phase "Lane 6: ApiKey-mode gRPC rejects missing metadata"
docker container rm "$CONTAINER_NAME" >/dev/null
start_container ApiKey
wait_for_ready
assert_runtime_identity
run_driver grpc-auth "$GRPC_BASE"
stop_cleanly "ApiKey-container graceful stop"

echo
echo "[smoke] PASS: the Native AOT deployment image satisfies all seven OTLP wire lanes and released-Nami.Telemetry.Hosting conformance"
