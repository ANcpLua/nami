#!/usr/bin/env bash

set -eo pipefail
export PYTHONDONTWRITEBYTECODE=1
SCRIPT_DIR=$(cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd)
cd "$SCRIPT_DIR/.."
# shellcheck source=eng/scripts/limits.sh
source "$SCRIPT_DIR/scripts/limits.sh"
if [[ ${NAMI_LOCKED_ENTRY:-} != "$SCRIPT_DIR/build.sh" ]]; then
    exec python3 "$SCRIPT_DIR/scripts/workspace_lock.py" "$PWD" "$SCRIPT_DIR/build.sh" "$@"
fi
unset NAMI_LOCKED_ENTRY

DOTNET_EXE=$(python3 "$SCRIPT_DIR/scripts/toolchain.py" dotnet)
export DOTNET_EXE
export DOTNET_ROOT="$(dirname "$DOTNET_EXE")"
PATH="$(dirname "$DOTNET_EXE"):$(python3 "$SCRIPT_DIR/scripts/toolchain.py" build-path):$PATH"
export PATH

# Quiet unless --verbose or NAMI_VERBOSE=1: eng/scripts/quiet.sh prints the target table, PASS or
# FAIL and the failing target's last lines, and keeps the whole output in
# external current diagnostic state. --help and the targets whose output is the point
# (Print) print as before.
verbose=${NAMI_VERBOSE:-0}
arguments=()
name=build
for argument in "$@"; do
    case $argument in
        --verbose) verbose=1; continue ;;
        --help | -h | Print | InspectCode) verbose=1 ;;
        -*) parameters=1 ;;
        *) [[ -n ${parameters:-} ]] || name+="-$argument" ;;
    esac
    arguments+=("$argument")
done
if [[ $verbose != 1 ]]; then
    exec "$SCRIPT_DIR/scripts/quiet.sh" "${name//[^A-Za-z0-9._-]/_}" -- "$SCRIPT_DIR/build.sh" ${arguments[@]+"${arguments[@]}"}
fi
set -- ${arguments[@]+"${arguments[@]}"}

bash --version 2>&1 | head -n 1

BUILD_PROJECT_FILE="$SCRIPT_DIR/build/build.csproj"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "Microsoft (R) .NET SDK version $("$DOTNET_EXE" --version)"

# Dash-form switches: Git Bash on the Windows release legs rewrites an argument that starts with / as a path.
mkdir -p "$NAMI_DIAGNOSTICS_ROOT/build/current"
"$DOTNET_EXE" build "$BUILD_PROJECT_FILE" "-maxcpucount:$NAMI_BUILD_WORKERS" -nodeReuse:false -p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet "-bl:$NAMI_DIAGNOSTICS_ROOT/build/current/bootstrap-{}.binlog"
"$DOTNET_EXE" run --project "$BUILD_PROJECT_FILE" --no-build -- "$@"
