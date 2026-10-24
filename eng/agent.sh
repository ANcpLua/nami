#!/usr/bin/env bash
set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$script_dir/.." && pwd)
source "$script_dir/scripts/limits.sh"
if [[ ${NAMI_LOCKED_ENTRY:-} != "$script_dir/agent.sh" ]]; then
    exec python3 "$script_dir/scripts/workspace_lock.py" "$root" "$script_dir/agent.sh" "$@"
fi
unset NAMI_LOCKED_ENTRY
bun=$(mise -C "$root" which bun)
if [[ ${1:-} == prepare ]]; then
    "$bun" install --cwd "$root/contracts" --frozen-lockfile
    exec "$bun" run --cwd "$root/contracts" build:emitters
fi
if [[ ! -f "$root/contracts/emitters/nami-lint/dist/index.js" || ! -d "$root/contracts/node_modules/@typespec/compiler" ]]; then
    echo 'ERROR @ancplua/nami-agent-control/setup-failed: run mise agent -- prepare first.' >&2
    exit 2
fi
exec "$bun" run "$root/contracts/agent-control/src/cli.ts" "$@"
