"""One current diagnostic record per lane, outside the source/instruction tree."""

import argparse
import hashlib
import json
import os
import re
import shutil
import sys
from pathlib import Path

LEGACY_ROOTS = ("artifacts/inspection", "artifacts/test-results", "artifacts/binlogs", "artifacts/logs",
                "artifacts/verification.lock", "inspectcode.sarif", ".fallout/build.schema.json",
                ".agents/skills/csharp-union-types/validation/last-run.json")


def owned_paths(root):
    candidates = [root / name for name in LEGACY_ROOTS]
    temporary = root / ".fallout/temp"
    candidates += [p for p in (temporary.rglob("*") if temporary.resolve().is_relative_to(root) else [])
                   if p.is_file() and (p.suffix in {".log", ".binlog"} or p.name == "build-graph.json")]
    for name in (".agents", ".config", ".fallout", "artifacts"):
        base = root / name
        if base.resolve().is_relative_to(root):
            candidates += list(base.rglob(".DS_Store"))
    if (root / ".agents").resolve().is_relative_to(root):
        candidates += list((root / ".agents").rglob("__pycache__"))
    binary_root = root / "artifacts/bin"
    for project in (binary_root.glob("*Tests") if binary_root.resolve().is_relative_to(root) else []):
        candidates += [p for p in project.rglob("*") if p.is_dir() and re.search(r"_[0-9a-f]{32}$", p.name)]
    result = []
    for path in sorted(set(candidates), key=lambda p: (len(p.parts), str(p))):
        if (path.exists() or path.is_symlink()) and path.parent.resolve() == path.parent and not any(path.is_relative_to(parent) for parent in result):
            result.append(path)
    return result


def owns_path(root, path):
    relative = path.relative_to(root)
    if any(relative == Path(name) or relative.is_relative_to(name) for name in LEGACY_ROOTS):
        return True
    parts = relative.parts
    if parts[:2] == (".fallout", "temp") and (path.suffix in {".log", ".binlog"} or path.name == "build-graph.json"):
        return True
    if parts and parts[0] in {".agents", ".config", ".fallout", "artifacts"} and path.name == ".DS_Store":
        return True
    if parts and parts[0] == ".agents" and "__pycache__" in parts:
        return True
    return (len(parts) > 3 and parts[:2] == ("artifacts", "bin") and parts[2].endswith("Tests")
            and any(re.search(r"_[0-9a-f]{32}$", part) for part in parts[3:]))


def artifact_inventory(root, overrides=None):
    from inspect_code import digest, source_files
    artifacts = []
    for path in owned_paths(root):
        rows = []
        size = 0
        members = [path]
        if path.is_dir() and not path.is_symlink():
            for directory, children, files in os.walk(path, followlinks=False):
                members += [Path(directory, name) for name in children + files]
        for member in sorted(set(members)):
            relative = str(member.relative_to(path.parent))
            if member.is_symlink():
                data = os.readlink(member).encode()
                kind = "symlink"
            elif member.is_file():
                data = member.read_bytes()
                kind = "file"
                size += len(data)
            else:
                data, kind = b"", "directory"
            rows.append((relative, kind, hashlib.sha256(data).hexdigest()))
        artifacts.append(dict(path=str(path.relative_to(root)), sha256=hashlib.sha256(json.dumps(rows).encode()).hexdigest(),
                              kind="symlink" if path.is_symlink() else "directory" if path.is_dir() else "file", bytes=size))
    protected = digest((p for p in source_files(root) if not owns_path(root, p)), root, overrides)
    return dict(protectedSource=protected, artifacts=artifacts)


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
    for path in owned_paths(root):
        remove(path)
    temporary = root / ".fallout/temp"
    if temporary.exists() and temporary.resolve() == temporary:
        for path in sorted(temporary.rglob("*"), reverse=True):
            if path.is_dir() and not path.is_symlink() and not any(path.iterdir()):
                path.rmdir()
        if not any(temporary.iterdir()):
            temporary.rmdir()
    for path in state_root(root).glob("test-scratch-*"):
        remove(path)


def show(root):
    base = state_root(root)
    print("CURRENT DIAGNOSTICS (one record per lane; no historical runs)")
    found = False
    for lane in ("build", "tests", "contracts", "inspection/dotnet", "inspection/typescript", "inspection/mcp", "agent", "union"):
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
                if lane == "agent":
                    actual = artifact_inventory(Path(root).resolve())["protectedSource"]
                elif lane in ("inspection/typescript", "inspection/mcp"):
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
            if lane == "contracts":
                print(f"  generated files changed={len(value.get('fileChanges', []))}; wire={value.get('wire', {}).get('status', 'UNAVAILABLE')}; compatibility/quality=NOT_ASSESSED")
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
    parser.add_argument("action", choices=("path", "clean", "show", "inventory", "predict"))
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    try:
        if args.action == "path":
            print(state_root(args.root))
        elif args.action == "clean":
            clean_workspace(args.root)
        elif args.action == "inventory":
            print(json.dumps(artifact_inventory(args.root.resolve())))
        elif args.action == "predict":
            root = args.root.resolve()
            overrides = {root / name: value.encode() for name, value in json.load(sys.stdin).items()}
            if any(not path.resolve().is_relative_to(root) for path in overrides):
                raise ValueError("Predicted file is outside the checkout")
            print(artifact_inventory(root, overrides)["protectedSource"])
        else:
            show(args.root)
    except (OSError, ValueError) as error:
        print(f"DIAGNOSTICS ERROR: {error}", file=sys.stderr)
        raise SystemExit(2)
