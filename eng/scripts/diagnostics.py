"""One current diagnostic record per lane, outside the source/instruction tree."""

import argparse
import hashlib
import json
import os
import shutil
import sys
from pathlib import Path


def state_root(root):
    root = Path(root).resolve()
    home = Path(os.environ.get("NAMI_DIAGNOSTICS_HOME") or
                str(Path(os.environ.get("XDG_STATE_HOME", Path.home() / ".local/state")) / "nami/diagnostics")).expanduser().resolve()
    if home == root or home.is_relative_to(root):
        raise ValueError("NAMI_DIAGNOSTICS_HOME must be outside the repository")
    return home / hashlib.sha256(str(root).encode()).hexdigest()[:16]


def remove(path):
    if path.is_symlink() or path.is_file():
        path.unlink()
    elif path.is_dir():
        shutil.rmtree(path)


def begin(root, lane):
    path = state_root(root) / lane / "current"
    remove(path)
    path.mkdir(parents=True)
    (path / "summary.json").write_text(json.dumps({"status": "RUNNING", "lane": lane}) + "\n")
    return path


def current(root, lane, requested="current"):
    path = state_root(root) / lane / "current"
    if str(requested) != "current" and Path(requested).resolve() != path:
        raise ValueError("Historical run paths are not supported. Use --results current.")
    if not (path / "summary.json").is_file():
        raise ValueError(f"No current {lane} result. Run the matching entry point first.")
    return path


def clean_workspace(root):
    root = Path(root).resolve()
    # These are generated diagnostics only. Preserve bin/obj/publish, source and config.
    for name in ("artifacts/inspection", "artifacts/test-results", "artifacts/binlogs", "artifacts/logs",
                 "artifacts/verification.lock", "inspectcode.sarif", ".fallout/build.schema.json"):
        remove(root / name)
    temporary = root / ".fallout/temp"
    if temporary.exists():
        for path in list(temporary.rglob("*")):
            if path.is_file() and (path.suffix in {".log", ".binlog"} or path.name == "build-graph.json"):
                path.unlink()
        for path in sorted(temporary.rglob("*"), reverse=True):
            if path.is_dir() and not any(path.iterdir()):
                path.rmdir()
        if not any(temporary.iterdir()):
            temporary.rmdir()
    for name in (".agents", ".config", ".fallout", "artifacts"):
        for path in (root / name).rglob(".DS_Store"):
            path.unlink()
    for path in (root / ".agents").rglob("__pycache__"):
        remove(path)


def show(root):
    base = state_root(root)
    print("CURRENT DIAGNOSTICS (one record per lane; no historical runs)")
    found = False
    for lane in ("build", "tests", "inspection/dotnet", "inspection/typescript", "inspection/mcp", "union"):
        path = base / lane / "current/summary.json"
        if not path.is_file():
            continue
        found = True
        try:
            value = json.loads(path.read_text())
            status = value.get("status", "passed" if value.get("passed") else "failed" if "passed" in value else "unknown")
            context = value.get("context", {})
            exit_code = value.get("exitCode", "?")
            if context.get("source"):
                from inspect_code import outputs, sources
                changed_outputs = False
                if lane in ("inspection/typescript", "inspection/mcp"):
                    from inspect_agent import fingerprint, output_hash
                    actual = fingerprint(Path(root).resolve())
                    if context.get("outputs"):
                        changed_outputs = context["outputs"] != output_hash(context["target"], Path(root).resolve())
                else:
                    actual = sources(Path(root).resolve())
                    if context.get("outputs"):
                        changed_outputs = context["outputs"] != outputs(context["configuration"], Path(root).resolve())
                if actual != context["source"] or changed_outputs:
                    status = "STALE"
                    exit_code = 2
            else:
                status = "recorded " + status
            print(f"{lane}: {status}; exit={exit_code}")
            for problem in value.get("problems", [])[:3]:
                print("  " + " ".join(str(problem).split())[:300])
            for suite in value.get("suites", []):
                print(f"  {suite['suite']}: passed={suite.get('passed', 0)} failed={suite.get('failed', 0)} skipped={suite.get('skipped', 0)}")
            counts = value.get("counts")
            if counts is not None:
                print("  " + " ".join(f"{key}={counts.get(key, 0)}" for key in ("error", "warning", "note")))
        except (OSError, ValueError) as error:
            print(f"{lane}: INCOMPLETE ({error})")
    if not found:
        print("No current result. Run ./eng/build.sh Check, ./eng/test.sh or ./eng/inspect.sh.")
    print("Details: ./eng/inspect.sh [--target typescript|mcp] --results current --limit 10")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("path", "clean", "show"))
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    try:
        if args.action == "path":
            print(state_root(args.root))
        elif args.action == "clean":
            clean_workspace(args.root)
        else:
            show(args.root)
    except (OSError, ValueError) as error:
        print(f"DIAGNOSTICS ERROR: {error}", file=sys.stderr)
        raise SystemExit(2)
