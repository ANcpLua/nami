#!/usr/bin/env bash
set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$script_dir/.." && pwd)
source "$script_dir/scripts/limits.sh"
if [[ ${NAMI_LOCKED_ENTRY:-} != "$script_dir/contracts.sh" ]]; then
    exec python3 "$script_dir/scripts/workspace_lock.py" "$root" "$script_dir/contracts.sh" "$@"
fi
unset NAMI_LOCKED_ENTRY
node=$(mise -C "$root" which node)
bun=$(mise -C "$root" which bun)
fingerprint=$(python3 - "$root" "$script_dir/scripts" <<'PY'
import sys
from pathlib import Path
sys.path.insert(0, sys.argv[2])
from inspect_code import sources
print(sources(Path(sys.argv[1])))
PY
)
exec "$node" "$root/contracts/scripts/verify-contracts.mjs" "$@" --root "$root" --state "$NAMI_DIAGNOSTICS_ROOT/contracts/current" --bun "$bun" --source "$fingerprint"
