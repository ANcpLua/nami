#!/usr/bin/env bash
set -euo pipefail
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$script_dir/.." && pwd)
source "$script_dir/scripts/limits.sh"
if [[ ${NAMI_LOCKED_ENTRY:-} != "$script_dir/inspect.sh" ]]; then
    exec python3 "$script_dir/scripts/workspace_lock.py" "$root" "$script_dir/inspect.sh" "$@"
fi
unset NAMI_LOCKED_ENTRY
exec python3 "$script_dir/scripts/inspect_code.py" "$@"
