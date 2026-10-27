#!/usr/bin/env bash
set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$script_dir/.." && pwd)
if [[ ${NAMI_LOCKED_ENTRY:-} != "$script_dir/typespec.sh" ]]; then
    exec python3 "$script_dir/scripts/workspace_lock.py" "$root" "$script_dir/typespec.sh" "$@"
fi
unset NAMI_LOCKED_ENTRY
exec python3 "$script_dir/scripts/update_typespec.py" "$@"
