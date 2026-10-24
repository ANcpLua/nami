#!/usr/bin/env bash
set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$script_dir/.." && pwd)
# shellcheck source=eng/scripts/limits.sh
source "$script_dir/scripts/limits.sh"
if [[ ${1:-} != --status && ${NAMI_LOCKED_ENTRY:-} != "$script_dir/test.sh" ]]; then
    exec python3 "$script_dir/scripts/workspace_lock.py" "$root" "$script_dir/test.sh" "$@"
fi
unset NAMI_LOCKED_ENTRY
if [[ ${1:-} == --status ]]; then
    exec python3 "$script_dir/scripts/run_tests.py" --root "$root" --dotnet unused "$@"
fi
dotnet_exe=$(python3 "$script_dir/scripts/toolchain.py" dotnet)
export DOTNET_ROOT="$(dirname "$dotnet_exe")"
PATH="$(dirname "$dotnet_exe"):$PATH"
export PATH
exec python3 "$script_dir/scripts/run_tests.py" --root "$root" --dotnet "$dotnet_exe" "$@"
