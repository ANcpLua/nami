#!/usr/bin/env bash
# Vendored from the ancplua-lean-proof plugin (skills/lean-proof/scripts), MIT License, Copyright (c) 2026 ANcpLua.
# Final Lean audit. Run from the Lake project root:  lean_audit.sh <RootModule> [--spec <Namespace>]
# 1) lake build   2) axiom/spec audit (what the environment says)   3) leanchecker kernel replay per module (whether the kernel accepts it)
# A green `lake build` alone proves nothing: sorry is only a warning; native_decide and project axioms are silent.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="${1:?usage: lean_audit.sh <RootModule> [--spec <Namespace>]}"; shift
status=0
lake build || { echo "AUDIT: lake build failed"; exit 3; }
lake env lean --run "$here/axiom_audit.lean" "$root" "$@" || status=1
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
