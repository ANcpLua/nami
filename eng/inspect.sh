#!/usr/bin/env bash
set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$script_dir/.." && pwd)
source "$script_dir/scripts/limits.sh"
if [[ ${NAMI_LOCKED_ENTRY:-} != "$script_dir/inspect.sh" ]]; then
    exec python3 "$script_dir/scripts/workspace_lock.py" "$root" "$script_dir/inspect.sh" "$@"
fi
unset NAMI_LOCKED_ENTRY
target=dotnet
arguments=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --target)
            [[ $# -ge 2 ]] || { echo "--target requires dotnet, typescript or mcp" >&2; exit 2; }
            target=$2; shift 2 ;;
        --target=*) target=${1#*=}; shift ;;
        *) arguments+=("$1"); shift ;;
    esac
done
case "$target" in
    dotnet) exec python3 "$script_dir/scripts/inspect_code.py" ${arguments[@]+"${arguments[@]}"} ;;
    typescript|mcp) exec python3 "$script_dir/scripts/inspect_agent.py" --target "$target" ${arguments[@]+"${arguments[@]}"} ;;
    *) echo "Unknown inspection target: $target" >&2; exit 2 ;;
esac
