#!/usr/bin/env python3
"""Explicit one-way export of the canonical Nami union skill. Never import target edits."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

MANIFEST = '.sync-manifest.json'
IGNORED = {'.git', 'bin', 'obj', 'artifacts', '__pycache__', '.idea', 'node_modules', '.DS_Store'}


def inventory(root):
    result = {}
    for path in sorted(root.rglob('*')):
        relative = path.relative_to(root)
        if path.name == MANIFEST or any(part in IGNORED for part in relative.parts):
            continue
        if path.is_symlink():
            raise ValueError(f'Symlinks are not exportable: {path}')
        if path.is_file():
            result[relative.as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def synchronize(source, targets, *, apply=False):
    source = Path(source).resolve()
    if (source / MANIFEST).is_symlink():
        raise ValueError('Source receipt must not be a symlink')
    files = inventory(source)
    receipt = json.dumps({'schema': 1, 'source': 'nami:csharp-union-types', 'files': files}, indent=2, sort_keys=True) + '\n'
    plans, changes = [], []
    for raw_target in targets:
        target = Path(os.path.abspath(raw_target))
        if target.resolve() != target or target == source or target.is_relative_to(source) or source.is_relative_to(target):
            raise ValueError(f'Target must be a separate, non-linked directory: {target}')
        if (target / MANIFEST).is_symlink():
            raise ValueError(f'Target receipt must not be a symlink: {target / MANIFEST}')
        previous = {}
        if (target / MANIFEST).is_file():
            value = json.loads((target / MANIFEST).read_text())
            if value.get('schema') != 1 or value.get('source') != 'nami:csharp-union-types':
                raise ValueError(f'Invalid sync ownership: {target / MANIFEST}')
            previous = value['files']
            for name in previous:
                path = Path(name)
                if (not name or path.is_absolute() or path.drive or '..' in path.parts
                        or any(part in IGNORED for part in path.parts) or name == MANIFEST):
                    raise ValueError(f'Unsafe managed path: {name}')
        current = inventory(target) if target.exists() else {}
        dirty = set()
        git_owned = False
        if not previous and target.exists():
            git = subprocess.run(['git', '-C', str(target), 'status', '--porcelain', '--untracked-files=all'], capture_output=True, text=True)
            if git.returncode == 0:
                git_owned = True
                dirty = {line[3:] for line in git.stdout.splitlines()}
        for name, value in current.items():
            expected = files.get(name)
            if previous and name in previous and value not in {previous[name], expected}:
                raise ValueError(f'Target has independent edits: {target / name}')
            if not previous and name in dirty and value != expected:
                raise ValueError(f'Target has uncommitted edits: {target / name}')
            if not previous and not git_owned and expected is not None and value != expected:
                raise ValueError(f'Unowned target file would be overwritten: {target / name}')
        removed = set(previous) - set(files)
        updated = [name for name, value in files.items() if current.get(name) != value]
        if updated or removed or not (target / MANIFEST).is_file() or (target / MANIFEST).read_text() != receipt:
            changes.append(str(target))
        plans.append((target, removed, updated))
    if not (source / MANIFEST).is_file() or (source / MANIFEST).read_text() != receipt:
        changes.append(str(source / MANIFEST))
    if apply:
        for target, removed, updated in plans:
            target.mkdir(parents=True, exist_ok=True)
            for name in removed:
                path = target / name
                if path.exists():
                    path.unlink()
            for name in updated:
                path = target / name
                path.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source / name, path)
            (target / MANIFEST).write_text(receipt, encoding='utf-8')
        (source / MANIFEST).write_text(receipt, encoding='utf-8')
    return changes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--target', action='append', type=Path, required=True)
    parser.add_argument('--apply', action='store_true', help='Write the validated export; default is check-only')
    args = parser.parse_args()
    try:
        changes = synchronize(args.source, args.target, apply=args.apply)
    except (OSError, ValueError) as error:
        print(f'Union sync failed: {error}')
        return 2
    print(('Updated' if args.apply else 'Drift') + ': ' + (', '.join(changes) or 'none'))
    return 0 if args.apply or not changes else 1


if __name__ == '__main__':
    raise SystemExit(main())
