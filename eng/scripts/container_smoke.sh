#!/usr/bin/env bash
# Smoke test of the nami image with a real Docker daemon. The container workflow runs it for linux/amd64 and
# linux/arm64; eng/scripts/docker_desktop_check.sh runs it on a Mac or Windows machine with Docker Desktop.
#
#   eng/scripts/container_smoke.sh [--image NAME] [--bind-ready]
#
# Without --image it builds src/nami/Dockerfile first. --bind-ready also bind-mounts a host directory for the
# ready manifest and reads it from the host (on Docker Desktop that crosses the file-sharing layer).
# Needs docker, curl and a POSIX shell environment; the keys below are fake and exist only in this test.
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
image="" bind_ready=false
while [[ $# -gt 0 ]]; do
    case $1 in
        --image) image=$2; shift 2 ;;
        --bind-ready) bind_ready=true; shift ;;
        *) echo "usage: $0 [--image NAME] [--bind-ready]" >&2; exit 2 ;;
    esac
done
if [[ -z $image ]]; then
    image=nami:smoke
    docker build -f "$root/src/nami/Dockerfile" -t "$image" "$root"
fi

id=nami-smoke-$$
ingest=fake-ingest-key-0001 read=fake-read-key-0001
keys="{\"demo\":[{\"key\":\"$ingest\",\"roles\":[\"ingest\"]},{\"key\":\"$read\",\"roles\":[\"read\"]}]}"
work=$(mktemp -d)
step() { printf '\n== %s\n' "$*"; }
fail() { printf 'SMOKE FAILED: %s\n' "$*" >&2; docker logs "$id" 2>&1 | tail -40 >&2 || true; exit 1; }
cleanup() {
    docker rm -f "$id" "$id-second" "$id-files" >/dev/null 2>&1 || true
    docker volume rm "$id" >/dev/null 2>&1 || true
}
trap cleanup EXIT

wait_healthy() {
    local deadline=$(( $(date +%s) + 60 )) status
    while :; do
        status=$(docker inspect -f '{{.State.Status}} {{.State.Health.Status}}' "$id")
        [[ $status == "running healthy" ]] && return 0
        [[ $status == running* ]] || fail "the container is '$status' instead of running"
        [[ $(date +%s) -lt $deadline ]] || fail "not healthy after 60 s ($status)"
        sleep 0.5
    done
}
# code METHOD URL [curl options]: the HTTP status only
code() { local method=$1 url=$2; shift 2; curl -sS -o /dev/null -w '%{http_code}' -X "$method" "$url" "$@"; }
expect() { [[ $2 == "$3" ]] || fail "$1: expected $3, got $2"; echo "ok  $1: $2"; }
mcp_call='{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"sources_status","arguments":{}}}'
mcp_headers=(-H 'content-type: application/json' -H 'accept: application/json, text/event-stream')

step "image"
docker image inspect "$image" --format 'id {{.Id}}  {{.Os}}/{{.Architecture}}  user {{.Config.User}}  {{.Size}} bytes'
expect "image user" "$(docker image inspect "$image" --format '{{.Config.User}}')" app

step "image files: /app belongs to root, and the image's user can change nothing in it"
# The image has no shell and no ls: its files are listed from an export of a container that never ran.
docker create --name "$id-files" "$image" >/dev/null
docker export "$id-files" | tar -tv --numeric-owner > "$work/files.txt"
docker rm "$id-files" >/dev/null
# tar -tv: mode, uid/gid, size, date, time, name
awk '$6 == "app/" || $6 ~ /^app\//' "$work/files.txt" > "$work/app.txt"
grep -q ' app/nami$' "$work/app.txt" && grep -q ' app/mcp/main.js$' "$work/app.txt" || fail "the listing of /app lacks the engine or the bundle"
problems=$(awk '$2 != "0/0" || substr($1, 6, 1) == "w" || substr($1, 9, 1) == "w"' "$work/app.txt")
[[ -z $problems ]] || { printf '%s\n' "$problems" >&2; fail "not owned by root, or writable by group or others (above)"; }
echo "ok  $(wc -l < "$work/app.txt") entries under /app, each owned by 0/0 and writable by its owner only"
# And the attempt itself, as the user the image runs as, with the Bun the image already holds.
attempt='const fs = require("node:fs"); const done = [];
for (const [what, act] of [
  ["write a new file into /app", () => fs.writeFileSync("/app/planted", "")],
  ["rename the engine", () => fs.renameSync("/app/nami", "/app/nami.old")],
  ["overwrite the engine", () => fs.writeFileSync("/app/nami", "")],
  ["overwrite the bundle", () => fs.writeFileSync("/app/mcp/main.js", "")],
  ["remove the bundle", () => fs.rmSync("/app/mcp/main.js")],
  ["remove the health probe", () => fs.rmSync("/app/mcp/healthcheck.js")]]) { try { act(); done.push(what); } catch {} }
console.log(`uid ${process.getuid()} could: ${done.join(", ") || "nothing"}`);'
expect "changing /app as the image's user" "$(docker run --rm --entrypoint /usr/local/bin/bun "$image" -e "$attempt")" "uid 1654 could: nothing"

step "start: three ports on host ports of Docker's choice, a fresh named volume, a stop budget of 25 s in a grace period of 30 s"
docker volume create "$id" >/dev/null
run=(docker run -d --name "$id" --stop-timeout 30 -v "$id:/data"
    -p 127.0.0.1:0:5100 -p 127.0.0.1:0:4318 -p 127.0.0.1:0:4317
    -e NAMI_PUBLIC_URL=http://127.0.0.1:5100 -e "NAMI_OTLP_PROJECT_KEYS=$keys" -e NAMI_STOP_BUDGET_SECONDS=25)
if $bind_ready; then
    chmod 0777 "$work"
    run+=(-v "$work:/ready" -e NAMI_READY_FILE=/ready/ready.json)
fi
"${run[@]}" "$image" >/dev/null
wait_healthy
echo "ok  healthy (the image's own shell-less healthcheck)"
docker top "$id" -o user,pid,args 2>/dev/null || docker top "$id"
mcp=http://$(docker port "$id" 5100/tcp | head -1)
otlp=http://$(docker port "$id" 4318/tcp | head -1)
grpc=http://$(docker port "$id" 4317/tcp | head -1)
echo "mcp $mcp  otlp/http $otlp  otlp/grpc $grpc"
[[ $mcp == http://127.0.0.1:* && $otlp == http://127.0.0.1:* && $grpc == http://127.0.0.1:* ]] || fail "a port is not published on 127.0.0.1"
if $bind_ready; then
    [[ -s $work/ready.json ]] || fail "the ready manifest is not visible in the bind-mounted host directory"
    echo "ok  ready manifest through the bind mount: $(cat "$work/ready.json")"
fi

step "health, keys, Origin"
expect "GET /health" "$(code GET "$mcp/health")" 200
expect "MCP without a key" "$(code POST "$mcp/mcp" "${mcp_headers[@]}" -d "$mcp_call")" 401
expect "MCP with the ingest key" "$(code POST "$mcp/mcp" "${mcp_headers[@]}" -H "x-otlp-api-key: $ingest" -d "$mcp_call")" 403
expect "MCP from another site's page" "$(code POST "$mcp/mcp" "${mcp_headers[@]}" -H "x-otlp-api-key: $read" -H 'origin: https://evil.example' -d "$mcp_call")" 403
expect "MCP under a rebound host name" "$(code POST "$mcp/mcp" "${mcp_headers[@]}" -H "x-otlp-api-key: $read" -H 'host: evil.example' -d "$mcp_call")" 403

step "one OTLP export, one MCP query"
now=$(date +%s)
span="{\"resourceSpans\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"smoke-service\"}}]},\"scopeSpans\":[{\"scope\":{\"name\":\"smoke\"},\"spans\":[{\"traceId\":\"5b8efff798038103d269b633813fc60c\",\"spanId\":\"eee19b7ec3c1b174\",\"name\":\"smoke\",\"kind\":2,\"startTimeUnixNano\":\"${now}000000000\",\"endTimeUnixNano\":\"${now}000000001\"}]}]}]}"
expect "OTLP/HTTP without a key" "$(code POST "$otlp/v1/traces" -H 'content-type: application/json' -d "$span")" 401
expect "OTLP/HTTP with the ingest key" "$(code POST "$otlp/v1/traces" -H 'content-type: application/json' -H "x-otlp-api-key: $ingest" -d "$span")" 200
# An empty ExportTraceServiceRequest as one gRPC message: the listener must speak HTTP/2 and take the key.
printf '\0\0\0\0\0' > "$work/empty.grpc"
expect "OTLP/gRPC with the ingest key (HTTP status, HTTP version)" "$(curl -sS -o /dev/null -w '%{http_code} %{http_version}' --http2-prior-knowledge \
    -H 'content-type: application/grpc' -H 'te: trailers' -H "x-otlp-api-key: $ingest" --data-binary "@$work/empty.grpc" \
    "$grpc/opentelemetry.proto.collector.trace.v1.TraceService/Export")" "200 2"
query() { curl -sS -X POST "$mcp/mcp" "${mcp_headers[@]}" -H "x-otlp-api-key: $read" -d "$mcp_call"; }
answer=$(query)
[[ $answer == *smoke-service* ]] || fail "sources_status does not name the service that was sent: $answer"
echo "ok  MCP sources_status with the read key names smoke-service"

step "a second container on the same volume must not open the store"
set +e
docker run --name "$id-second" -v "$id:/data" -e NAMI_PUBLIC_URL=http://127.0.0.1:5100 -e "NAMI_OTLP_PROJECT_KEYS=$keys" "$image" > "$work/second.log" 2>&1
second=$?
set -e
[[ $second -ne 0 ]] || fail "the second container exited 0"
grep -q "Could not set lock on file" "$work/second.log" || { cat "$work/second.log" >&2; fail "the second container did not fail on the store's lock"; }
echo "ok  second container: exit $second, $(grep -o 'Could not set lock on file[^:]*' "$work/second.log" | head -1)"
expect "the first container after that" "$(docker inspect -f '{{.State.Status}}' "$id") $(code GET "$mcp/health")" "running 200"

step "docker stop: clean and well inside the grace period"
started=$(date +%s)
docker stop "$id" >/dev/null
took=$(( $(date +%s) - started ))
expect "exit code, OOM-killed" "$(docker inspect -f '{{.State.ExitCode}} {{.State.OOMKilled}}' "$id")" "0 false"
[[ $took -le 10 ]] || fail "docker stop took $took s; an idle nami ends at once, and 30 s would mean Docker killed it"
echo "ok  docker stop took $took s"
if $bind_ready; then
    [[ ! -e $work/ready.json ]] || fail "the ready manifest is still there after the stop"
    echo "ok  ready manifest removed"
fi

step "start again on the same volume: the acknowledged export is still there"
docker start "$id" >/dev/null
wait_healthy
mcp=http://$(docker port "$id" 5100/tcp | head -1)
answer=$(query)
[[ $answer == *smoke-service* ]] || fail "the export is gone after a restart: $answer"
echo "ok  sources_status still names smoke-service"
docker stop "$id" >/dev/null
expect "exit code of the second stop" "$(docker inspect -f '{{.State.ExitCode}}' "$id")" 0
printf '\nSMOKE PASSED: %s\n' "$(docker image inspect "$image" --format '{{.Id}} {{.Os}}/{{.Architecture}}')"
