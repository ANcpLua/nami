#!/usr/bin/env python3
"""Check or update the configured TypeSpec packages using small npm tag responses."""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import re
import subprocess
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

from toolchain import resolve

ROOT = Path(__file__).resolve().parents[2]
POLICY = Path("contracts/typespec-packages.json")
REFERENCE = Path(".agents/skills/nami-contract-models/references/typespec-packages.json")
VERSION = re.compile(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?\Z")


class UpdateError(RuntimeError):
    pass


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n")
    temporary.replace(path)


class Registry:
    def __init__(self, url, cache, offline=False, opener=urllib.request.urlopen):
        self.url = url.rstrip("/")
        self.cache = Path(cache)
        self.offline = offline
        self.opener = opener

    def fetch(self, name, tag):
        url = self.url + "/" + urllib.parse.quote(name, safe="@") + "/" + urllib.parse.quote(tag, safe="")
        path = self.cache / (hashlib.sha256(url.encode()).hexdigest() + ".json")
        cached = json.loads(path.read_text()) if path.exists() else None
        if self.offline:
            if cached is None:
                raise UpdateError(f"No cached metadata for {name}@{tag}; run an online check first.")
            value = cached["package"]
        else:
            headers = {"Accept": "application/json", "User-Agent": "nami-typespec-maintenance/1"}
            if cached and cached.get("etag"):
                headers["If-None-Match"] = cached["etag"]
            if cached and cached.get("last_modified"):
                headers["If-Modified-Since"] = cached["last_modified"]
            try:
                with self.opener(urllib.request.Request(url, headers=headers), timeout=20) as response:
                    value = json.load(response)
                    cache_value = {"package": value, "etag": response.headers.get("ETag"),
                                   "last_modified": response.headers.get("Last-Modified")}
            except urllib.error.HTTPError as error:
                error.close()
                if error.code != 304 or cached is None:
                    raise UpdateError(f"npm metadata for {name}@{tag}: HTTP {error.code}") from error
                value = cached["package"]
                cache_value = None
            self.validate(name, value)
            if cache_value is not None:
                write_json(path, cache_value)
        self.validate(name, value)
        return value

    @staticmethod
    def validate(name, value):
        if not isinstance(value, dict) or value.get("name") != name or not isinstance(value.get("version"), str) or not VERSION.fullmatch(value["version"]):
            raise UpdateError(f"Invalid npm package identity for {name}")
        if value.get("deprecated"):
            raise UpdateError(f"Refusing deprecated {name}@{value['version']}: {value['deprecated']}")


def collect_packages(registry, policy, tag):
    roles = dict(policy["packages"])
    packages = {}
    pending = set(roles)
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        while pending:
            names = sorted(pending)
            values = list(pool.map(lambda name: registry.fetch(name, tag), names))
            packages.update(zip(names, values))
            pending = set()
            for package in values:
                optional = package.get("peerDependenciesMeta", {})
                for name in package.get("peerDependencies", {}):
                    if name.startswith("@typespec/") and not optional.get(name, {}).get("optional") and name not in packages:
                        roles.setdefault(name, "required TypeSpec peer")
                        pending.add(name)
    return dict(sorted(packages.items())), roles


def constraints(packages):
    checks = []
    for name, package in packages.items():
        for relationship in ("dependencies", "peerDependencies"):
            for dependency, version_range in package.get(relationship, {}).items():
                if dependency in packages:
                    checks.append({"source": name, "dependency": dependency,
                                   "version": packages[dependency]["version"], "range": version_range})
    return checks


def verify_constraints(packages, bun):
    checks = constraints(packages)
    script = "const xs = await Bun.stdin.json(); console.log(JSON.stringify(xs.filter(x => !Bun.semver.satisfies(x.version, x.range))));"
    result = subprocess.run([bun, "-e", script], input=json.dumps(checks), text=True, capture_output=True, check=True)
    failures = json.loads(result.stdout)
    if failures:
        raise UpdateError("Incompatible latest TypeSpec packages: " + "; ".join(
            f"{x['source']} requires {x['dependency']} {x['range']}, selected {x['version']}" for x in failures))


def package_update(current, packages):
    result = json.loads(json.dumps(current))
    dependencies = result.setdefault("devDependencies", {})
    for name, metadata in packages.items():
        dependencies[name] = metadata["version"]
    return result


def installed_reference(root, packages, roles, tag):
    records = []
    for name, metadata in packages.items():
        directory = root / "contracts/node_modules" / name
        installed = json.loads((directory / "package.json").read_text())
        if installed.get("name") != name or installed.get("version") != metadata["version"]:
            raise UpdateError(f"Installed identity differs from selected {name}@{metadata['version']}")
        entrypoints = {}
        for label in ("tspMain", "types", "typings"):
            if isinstance(installed.get(label), str):
                entrypoints[label] = installed[label]
        for key, value in installed.get("exports", {}).items():
            if isinstance(value, dict):
                for condition in ("types", "typespec"):
                    if isinstance(value.get(condition), str):
                        entrypoints[f"{key}:{condition}"] = value[condition]
        sources = {}
        for label, relative in sorted(entrypoints.items()):
            if "*" in relative:
                continue
            path = directory / relative
            if not path.is_file() and label in ("tspMain", "types", "typings", ".:types", ".:typespec"):
                raise UpdateError(f"Missing declared entry point: {name} {relative}")
            sources[label] = {"path": str(path.relative_to(root)),
                              "exists": path.is_file(),
                              "sha256": hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None}
        records.append({"package": name, "version": metadata["version"], "role": roles[name], "tag": tag,
                        "integrity": metadata.get("dist", {}).get("integrity"),
                        "peerDependencies": metadata.get("peerDependencies", {}), "entrypoints": sources})
    return {"generatedBy": "eng/typespec.sh --apply", "scope": "Installed public entry points; not a complete API or behavior inventory.",
            "packages": records}


def validate_imports(root, packages, node, run=subprocess.run):
    compiler = root / "contracts/node_modules/@typespec/compiler"
    manifest = json.loads((compiler / "package.json").read_text())
    with tempfile.TemporaryDirectory(prefix=".nami-typespec-", dir=root / "contracts/node_modules") as temporary:
        source = Path(temporary) / "main.tsp"
        names = [name for name in packages if json.loads((root / "contracts/node_modules" / name / "package.json").read_text()).get("tspMain")]
        source.write_text("\n".join(f'import "{name}";' for name in names) + "\n")
        # An explicit empty emitter config avoids activating the application's configured outputs.
        config = Path(temporary) / "tspconfig.yaml"
        config.write_text("emit: []\n")
        run([node, str(compiler / manifest["bin"]["tsp"]), "compile", str(source), "--config", str(config),
             "--no-emit", "--warn-as-error"], cwd=root / "contracts", check=True)


def apply_update(root, updated, packages, roles, tag, bun, node, run=subprocess.run):
    tracked = [root / "contracts/package.json", root / "contracts/bun.lock", root / REFERENCE]
    original = {p: p.read_bytes() if p.exists() else None for p in tracked}
    try:
        write_json(tracked[0], updated)
        run([bun, "install"], cwd=root / "contracts", check=True)
        reference = installed_reference(root, packages, roles, tag)
        for package in reference["packages"]:
            for label, entry in package["entrypoints"].items():
                if not entry["exists"]:
                    print(f"PACKAGE EXPORT MISSING: {package['package']} {label}: {entry['path']}", flush=True)
        validate_imports(root, packages, node, run)
        # Verify restores generator-owned files itself on failure or drift.
        run(["bash", str(root / "eng/contracts.sh"), "verify"], cwd=root, check=True)
        write_json(root / REFERENCE, reference)
    except (Exception, KeyboardInterrupt):
        for path, content in original.items():
            if content is None:
                path.unlink(missing_ok=True)
            else:
                path.write_bytes(content)
        try:
            run([bun, "install", "--frozen-lockfile"], cwd=root / "contracts", check=True)
        except Exception as rollback:
            raise UpdateError("Package files restored, but restoring node_modules failed; run bun install --frozen-lockfile.") from rollback
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--apply", action="store_true", help="Install exact pins, validate imports and contract parity, refresh the reference")
    mode.add_argument("--check", action="store_true", help="Check available versions and the installed reference without changing source files (default)")
    parser.add_argument("--offline", action="store_true", help="Use cached npm metadata; this does not establish latest online versions")
    parser.add_argument("--tag", help="Override the configured npm tag, e.g. next; never guess the highest version")
    args = parser.parse_args()
    if args.apply and args.offline:
        raise UpdateError("--apply requires an online metadata check; --offline is read-only.")
    policy = json.loads((ROOT / POLICY).read_text())
    tag = args.tag or policy["tag"]
    registry = Registry(policy["registry"], ROOT / "artifacts/typespec/metadata", args.offline)
    packages, roles = collect_packages(registry, policy, tag)
    bun, node = resolve(ROOT, "bun"), resolve(ROOT, "node")
    verify_constraints(packages, bun)
    current = json.loads((ROOT / "contracts/package.json").read_text())
    updated = package_update(current, packages)
    changed = current != updated
    for name, metadata in packages.items():
        old = current.get("devDependencies", {}).get(name, "not installed")
        print(f"{name}: {old} -> {metadata['version']} [{tag}; {roles[name]}]", flush=True)
    try:
        expected = installed_reference(ROOT, packages, roles, tag)
        actual = json.loads((ROOT / REFERENCE).read_text()) if (ROOT / REFERENCE).exists() else None
        changed |= expected != actual
        for package in expected["packages"]:
            for label, entry in package["entrypoints"].items():
                if not entry["exists"]:
                    print(f"PACKAGE EXPORT MISSING: {package['package']} {label}: {entry['path']}")
    except (OSError, ValueError, UpdateError):
        changed = True
    if args.apply and changed:
        apply_update(ROOT, updated, packages, roles, tag, bun, node)
        print("TypeSpec packages and installed entry-point reference updated; contract parity verified.")
        return 0
    print(("CACHED " if args.offline else "") + ("UPDATE AVAILABLE: eng/typespec.sh --apply" if changed else "CURRENT"))
    return 1 if changed else 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, UpdateError, subprocess.SubprocessError, urllib.error.URLError) as error:
        print(f"TypeSpec update failed: {error}", file=sys.stderr)
        raise SystemExit(2)
