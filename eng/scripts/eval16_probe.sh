#!/usr/bin/env bash
# Throwaway evaluator probe (check/eval16-container only, never merged). Report-only: prints what it sees.
set -uo pipefail
image=nami:ci
ingest=fake-ingest-key-0001 read=fake-read-key-0001
keys="{\"demo\":[{\"key\":\"$ingest\",\"roles\":[\"ingest\"]},{\"key\":\"$read\",\"roles\":[\"read\"]}]}"
step() { printf '\n== PROBE %s\n' "$*"; }
code() { local method=$1 url=$2; shift 2; curl -sS -m 20 -o /dev/null -w '%{http_code}' -X "$method" "$url" "$@"; }
mcp_call='{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"sources_status","arguments":{}}}'
mcp_headers=(-H 'content-type: application/json' -H 'accept: application/json, text/event-stream')
wait_healthy() { local n=0; until [[ $(docker inspect -f '{{.State.Health.Status}}' "$1" 2>/dev/null) == healthy ]]; do n=$((n+1)); [[ $n -gt 120 ]] && { echo "NOT HEALTHY: $1"; docker logs "$1" 2>&1 | tail -20; return 1; }; sleep 0.5; done; }

step "image config and layers"
docker image inspect "$image" --format 'user={{.Config.User}} entrypoint={{json .Config.Entrypoint}} healthcheck={{json .Config.Healthcheck}} volumes={{json .Config.Volumes}} env={{json .Config.Env}}'
docker history --no-trunc --format '{{.Size}}\t{{.CreatedBy}}' "$image" | cut -c1-160
echo "shell present? (expect a failure to start)"; docker run --rm --entrypoint /bin/sh "$image" -c 'echo SHELL-PRESENT' 2>&1 | tail -1
echo "uid and /data on a fresh named volume:"; docker volume create e16-fresh >/dev/null
docker run --rm -v e16-fresh:/data --entrypoint /usr/local/bin/bun "$image" -e 'const fs=require("fs");const s=fs.statSync("/data");console.log("uid",process.getuid(),"gid",process.getgid(),"/data owner",s.uid,s.gid,"mode",(s.mode&0o7777).toString(8));fs.writeFileSync("/data/probe","x");console.log("write ok");try{fs.writeFileSync("/app/probe","x");console.log("/app WRITABLE")}catch(e){console.log("/app not writable:",e.code)}'
docker volume rm e16-fresh >/dev/null
echo "files in /app:"; docker run --rm --entrypoint /usr/local/bin/bun "$image" -e 'const fs=require("fs");for(const f of fs.readdirSync("/app",{recursive:true}))console.log(fs.statSync("/app/"+f).size,f)' | sort -rn | head -15

step "compose as documented: keys in .env, docker compose up"
printf '%s\n' "NAMI_OTLP_PROJECT_KEYS=$keys" > .env
docker tag "$image" nami
docker compose config | grep -E "NAMI_|127.0.0.1|published|target|host_ip|stop_grace|source|type:" 
docker compose up -d --no-build 2>&1 | tail -5
cid=$(docker compose ps -q nami); echo "container $cid"
wait_healthy "$cid" && echo "compose: healthy"
docker port "$cid"
echo "MCP read key: $(code POST http://127.0.0.1:5100/mcp "${mcp_headers[@]}" -H "x-otlp-api-key: $read" -d "$mcp_call")"
echo "MCP no key: $(code POST http://127.0.0.1:5100/mcp "${mcp_headers[@]}" -d "$mcp_call")"
echo "MCP via localhost name: $(code POST http://localhost:5100/mcp "${mcp_headers[@]}" -H "x-otlp-api-key: $read" -d "$mcp_call")"
echo "MCP Host 0.0.0.0: $(code POST http://127.0.0.1:5100/mcp "${mcp_headers[@]}" -H "x-otlp-api-key: $read" -H 'host: 0.0.0.0:5100' -d "$mcp_call")"
echo "MCP Origin http://0.0.0.0:5100: $(code POST http://127.0.0.1:5100/mcp "${mcp_headers[@]}" -H "x-otlp-api-key: $read" -H 'origin: http://0.0.0.0:5100' -d "$mcp_call")"
echo "MCP Origin https://evil.example: $(code POST http://127.0.0.1:5100/mcp "${mcp_headers[@]}" -H "x-otlp-api-key: $read" -H 'origin: https://evil.example' -d "$mcp_call")"
echo "health Host evil.example: $(code GET http://127.0.0.1:5100/health -H 'host: evil.example')"
ip=$(hostname -I | awk '{print $1}'); echo "MCP on the runner's own address $ip:5100 (expect no connection): $(code GET "http://$ip:5100/health" 2>&1 | tail -1)"
echo "OTLP 4318 on $ip (expect no connection): $(code GET "http://$ip:4318/" 2>&1 | tail -1)"
now=$(date +%s)
span="{\"resourceSpans\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"compose-service\"}}]},\"scopeSpans\":[{\"scope\":{\"name\":\"s\"},\"spans\":[{\"traceId\":\"5b8efff798038103d269b633813fc60c\",\"spanId\":\"eee19b7ec3c1b174\",\"name\":\"s\",\"kind\":2,\"startTimeUnixNano\":\"${now}000000000\",\"endTimeUnixNano\":\"${now}000000001\"}]}]}]}"
echo "OTLP/HTTP ingest key: $(code POST http://127.0.0.1:4318/v1/traces -H 'content-type: application/json' -H "x-otlp-api-key: $ingest" -d "$span")"
curl -sS -m 20 -X POST http://127.0.0.1:5100/mcp "${mcp_headers[@]}" -H "x-otlp-api-key: $read" -d "$mcp_call" | grep -o 'compose-service' | head -1
docker top "$cid" -o user,pid,args 2>/dev/null || docker top "$cid"
started=$(date +%s.%N); docker compose stop 2>&1 | tail -2; echo "compose stop took $(echo "$(date +%s.%N) - $started" | bc) s, exit $(docker inspect -f '{{.State.ExitCode}} oom={{.State.OOMKilled}}' "$cid"), StopTimeout=$(docker inspect -f '{{.Config.StopTimeout}}' "$cid")"
docker compose down -v 2>&1 | tail -2

step "second container on a locked store: its whole output"
docker volume create e16-lock >/dev/null
docker run -d --name e16-first -v e16-lock:/data -e NAMI_PUBLIC_URL=http://127.0.0.1:5100 -e "NAMI_OTLP_PROJECT_KEYS=$keys" "$image" >/dev/null
wait_healthy e16-first
docker run --name e16-second -v e16-lock:/data -e NAMI_PUBLIC_URL=http://127.0.0.1:5100 -e "NAMI_OTLP_PROJECT_KEYS=$keys" "$image" 2>&1 | cut -c1-400 | head -40
echo "second exit: $(docker inspect -f '{{.State.ExitCode}}' e16-second)"
docker rm -f e16-first e16-second >/dev/null; docker volume rm e16-lock >/dev/null

step "stop under load with the image's defaults (budget 8 s, docker stop default 10 s)"
docker volume create e16-load >/dev/null
docker run -d --name e16-load -v e16-load:/data -p 127.0.0.1:0:5100 -p 127.0.0.1:0:4318 -e NAMI_PUBLIC_URL=http://127.0.0.1:5100 -e "NAMI_OTLP_PROJECT_KEYS=$keys" "$image" >/dev/null
wait_healthy e16-load
otlp=http://$(docker port e16-load 4318/tcp | head -1)
work=$(mktemp -d)
python3 - "$work" <<'PY'
import json, sys, time
work = sys.argv[1]
now = int(time.time()) - 2
for i in range(120):
    spans = [{"traceId": f"{i * 400 + j + 1:032x}", "spanId": f"{i * 400 + j + 1:016x}", "name": "load", "kind": 2,
              "startTimeUnixNano": f"{now}000000000", "endTimeUnixNano": f"{now}000000001"} for j in range(400)]
    body = {"resourceSpans": [{"resource": {"attributes": [{"key": "service.name", "value": {"stringValue": f"load-{i:04d}x"}}]},
                               "scopeSpans": [{"scope": {"name": "load"}, "spans": spans}]}]}
    open(f"{work}/{i:04d}.json", "w").write(json.dumps(body))
PY
for f in "$work"/*.json; do
    ( c=$(curl -sS -m 60 -o /dev/null -w '%{http_code}' -X POST "$otlp/v1/traces" -H 'content-type: application/json' -H "x-otlp-api-key: $ingest" --data-binary "@$f" 2>/dev/null); echo "${c:-000}" > "${f%.json}.code" ) &
done
n=0; until grep -qsx 200 "$work"/*.code 2>/dev/null; do n=$((n+1)); [[ $n -gt 600 ]] && break; sleep 0.05; done
echo "answers when the stop begins: $(cat "$work"/*.code 2>/dev/null | sort | uniq -c | tr '\n' ' ') of 120"
started=$(date +%s.%N); docker stop e16-load >/dev/null; took=$(echo "$(date +%s.%N) - $started" | bc)
wait
echo "docker stop (default grace) took $took s; exit $(docker inspect -f '{{.State.ExitCode}} oom={{.State.OOMKilled}}' e16-load)"
echo "answers: $(cat "$work"/*.code | sort | uniq -c | tr '\n' ' ')"
docker logs e16-load 2>&1 | tail -8 | cut -c1-300
docker start e16-load >/dev/null; wait_healthy e16-load
mcp=http://$(docker port e16-load 5100/tcp | head -1)
answer=$(curl -sS -m 30 -X POST "$mcp/mcp" "${mcp_headers[@]}" -H "x-otlp-api-key: $read" -d "$mcp_call")
echo "sources_status bytes: ${#answer}; head: ${answer:0:600}"
acked=0 found=0 missing=""
for f in "$work"/*.code; do
    if [[ $(cat "$f") == 200 ]]; then acked=$((acked+1)); name="load-$(basename "${f%.code}")x"
        if [[ $answer == *"$name"* ]]; then found=$((found+1)); else missing+=" $name"; fi; fi
done
echo "acknowledged exports: $acked; their services named after the restart: $found; not named:${missing:- none}"
docker rm -f e16-load >/dev/null; docker volume rm e16-load >/dev/null
echo; echo "PROBE DONE"
