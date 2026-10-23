#!/usr/bin/env bash
set -euo pipefail

# Bound compiler and NativeAOT worker concurrency inside Docker as well as on the host.
export DOTNET_PROCESSOR_COUNT="${DOTNET_PROCESSOR_COUNT:-4}"

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <output-directory>" >&2
  exit 2
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$REPO_ROOT/services/nami.collector/nami.collector.csproj"
OUTPUT_DIRECTORY="$1"
TEMP_DIRECTORY="$(mktemp -d)"
PUBLISH_OUTPUT="$TEMP_DIRECTORY/publish.log"

cleanup() {
  local status=$?
  set +e
  rm -rf -- "$TEMP_DIRECTORY"
  exit "$status"
}
trap cleanup EXIT

DUCKDB_VERSION="$(dotnet msbuild "$PROJECT" -maxcpucount:4 -getProperty:DuckDBVersion -nologo)"

if dotnet publish "$PROJECT" -maxcpucount:4 -nodeReuse:false -p:UseSharedCompilation=false -p:ConcurrentBuild=false -c Release -o "$OUTPUT_DIRECTORY" -p:NamiAot=true \
  >"$PUBLISH_OUTPUT" 2>&1; then
  :
else
  PUBLISH_STATUS=$?
  cat "$PUBLISH_OUTPUT" >&2
  exit "$PUBLISH_STATUS"
fi

WARNING_MARKER=": warning "
EXPECTED_IL2104="duckdb.net.data.full/$DUCKDB_VERSION/lib/net10.0/DuckDB.NET.Data.dll : warning IL2104: Assembly 'DuckDB.NET.Data' produced trim warnings."
EXPECTED_IL3053="duckdb.net.data.full/$DUCKDB_VERSION/lib/net10.0/DuckDB.NET.Data.dll : warning IL3053: Assembly 'DuckDB.NET.Data' produced AOT analysis warnings."
WARNING_COUNT="$(grep -Fc "$WARNING_MARKER" "$PUBLISH_OUTPUT" || true)"
IL2104_COUNT="$(grep -Fc "$EXPECTED_IL2104" "$PUBLISH_OUTPUT" || true)"
IL3053_COUNT="$(grep -Fc "$EXPECTED_IL3053" "$PUBLISH_OUTPUT" || true)"
UNEXPECTED_WARNINGS="$(grep -F "$WARNING_MARKER" "$PUBLISH_OUTPUT" \
  | grep -Fv "$EXPECTED_IL2104" \
  | grep -Fv "$EXPECTED_IL3053" || true)"

if [[ "$WARNING_COUNT" != "2" \
  || "$IL2104_COUNT" != "1" \
  || "$IL3053_COUNT" != "1" \
  || -n "$UNEXPECTED_WARNINGS" ]]; then
  cat "$PUBLISH_OUTPUT" >&2
  echo "[publish] ERROR: NativeAOT diagnostics do not match the reviewed DuckDB.NET.Data $DUCKDB_VERSION contract" >&2
  exit 1
fi

grep -Fv "$WARNING_MARKER" "$PUBLISH_OUTPUT" || true
echo "[publish] INFO: reviewed DuckDB.NET.Data $DUCKDB_VERSION IL2104 and IL3053 diagnostics"
