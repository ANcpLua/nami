#!/usr/bin/env bash
set -euo pipefail
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$script_dir/.." && pwd)
# shellcheck source=eng/scripts/limits.sh
source "$script_dir/scripts/limits.sh"
if [[ ${1:-} != --status && ${NAMI_LOCKED_ENTRY:-} != "$script_dir/test.sh" ]]; then
    exec python3 "$script_dir/scripts/workspace_lock.py" "$root" "$script_dir/test.sh" "$@"
fi
unset NAMI_LOCKED_ENTRY
if [[ -n ${DOTNET_EXE:-} ]]; then
    dotnet_exe=$DOTNET_EXE
elif [[ $(uname -s) == Darwin && -x /usr/local/share/dotnet/dotnet ]]; then
    dotnet_exe=/usr/local/share/dotnet/dotnet
else
    dotnet_exe=$(command -v dotnet || true)
fi
[[ -n $dotnet_exe && -x $dotnet_exe ]] || { echo "The SDK from global.json is required." >&2; exit 10; }
PATH="$(dirname "$dotnet_exe"):$PATH"
export PATH
exec python3 "$script_dir/scripts/run_tests.py" --root "$root" --dotnet "$dotnet_exe" "$@"
