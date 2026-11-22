#!/usr/bin/env bash
# Vendored from the ancplua-lean-proof plugin (skills/lean-proof/scripts), MIT License, Copyright (c) 2026 ANcpLua.
# Final Lean audit. Run from the Lake project root:  lean_audit.sh <RootModule> [--spec <Namespace>] [--min-specs <N>]
# 1) lake build   2) axiom/spec audit (what the environment says)   3) leanchecker kernel replay per module (whether the kernel accepts it)
# A green `lake build` alone proves nothing: sorry is only a warning; native_decide and project axioms are silent.
# --min-specs N: fail when the spec namespace holds fewer than N statements (a renamed or emptied namespace audits nothing).
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="${1:?usage: lean_audit.sh <RootModule> [--spec <Namespace>] [--min-specs <N>]}"; shift
min_specs=""; args=()
while [ $# -gt 0 ]; do
  case "$1" in
    --min-specs) [[ ${2:-} =~ ^[0-9]+$ ]] || { echo "AUDIT: --min-specs needs a number"; exit 2; }; min_specs=$2; shift 2 ;;
    *) args+=("$1"); shift ;;
  esac
done
status=0
lake build || { echo "AUDIT: lake build failed"; exit 3; }
audit_out=$(lake env lean --run "$here/axiom_audit.lean" "$root" "${args[@]}") || status=1
echo "$audit_out"
if [ -n "$min_specs" ]; then
  specs=$(echo "$audit_out" | sed -nE 's/.* specs=([0-9]+) .*/\1/p' | head -1)
  if [ -z "$specs" ] || [ "$specs" -lt "$min_specs" ]; then
    echo "AUDIT: the spec namespace holds ${specs:-no} statements, expected at least $min_specs"; status=1
  fi
fi
# Never run leanchecker without a module argument: it replays every .olean on the search path.
mods=$(find .lake/build/lib/lean -name '*.olean' 2>/dev/null | sed -E 's#^\.lake/build/lib/lean/##; s#\.olean$##; s#/#.#g' | grep -E "^${root}(\.|$)" | sort)
[ -n "$mods" ] || { echo "AUDIT: no compiled modules under $root (not evidence)"; exit 3; }
n=0; bad=0
for m in $mods; do
  n=$((n+1))
  if ! out=$(lake env leanchecker "$m" 2>&1); then bad=$((bad+1)); echo "leanchecker FAILED $m: $(echo "$out" | grep -m1 -i 'declaration\|error\|exception')"; fi
done
echo "leanchecker modules=$n failures=$bad"
[ "$bad" -eq 0 ] || status=1
echo "AUDIT: $([ $status -eq 0 ] && echo PASS || echo FAIL)"
exit $status
