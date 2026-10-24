"""Resolve installed executable pins from mise.toml without installing anything."""

import os
import re
import shutil
import subprocess
import sys
import tomllib
from pathlib import Path


def pin(root, name):
    tools = tomllib.loads((Path(root) / "mise.toml").read_text())["tools"]
    value = tools[name]
    return value if isinstance(value, str) else value["version"]


def resolve(root, name, command=None, override=None):
    if override:
        return str(Path(override).expanduser().absolute())
    mise = shutil.which("mise")
    if not mise:
        raise ValueError("mise is required; install the configured toolchain explicitly")
    result = subprocess.run([mise, "-C", str(root), "which", command or name], text=True, capture_output=True,
                            env=dict(os.environ, MISE_EXEC_AUTO_INSTALL="false", MISE_AUTO_INSTALL="false"), timeout=30)
    if result.returncode:
        raise ValueError(f"Pinned {name}@{pin(root, name)} is not installed. Run mise install explicitly.")
    # Keep executable aliases (cargo -> rustup): argv[0] is part of their behavior.
    return str(Path(result.stdout.strip()).absolute())


def installation(root, name):
    mise = shutil.which("mise")
    if not mise:
        raise ValueError("mise is required")
    result = subprocess.run([mise, "-C", str(root), "where", name], text=True, capture_output=True,
                            env=dict(os.environ, MISE_EXEC_AUTO_INSTALL="false", MISE_AUTO_INSTALL="false"), timeout=30)
    if result.returncode:
        raise ValueError(f"Pinned {name}@{pin(root, name)} is not installed. Run mise install explicitly.")
    return Path(result.stdout.strip()).resolve()


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[2]
    try:
        if sys.argv[1] == "dotnet":
            executable = resolve(root, "dotnet", override=os.environ.get("DOTNET_EXE"))
            expected = pin(root, "dotnet")
            import json
            if json.loads((root / "global.json").read_text())["sdk"]["version"] != expected:
                raise ValueError("mise.toml and global.json must pin the same .NET SDK")
            actual = subprocess.check_output([executable, "--version"], cwd=root, text=True).strip()
            if actual != expected:
                raise ValueError(f"Expected .NET SDK {expected}; found {actual}")
            print(executable)
        elif sys.argv[1] == "build-path":
            commands = [("bun", "bun"), ("node", "node"), ("rust", "cargo"), ("github:open-telemetry/weaver", "weaver")]
            paths = []
            for name, command in commands:
                executable = resolve(root, name, command)
                actual = subprocess.check_output([executable, "--version"], cwd=root, text=True).strip()
                if not re.search(r"(?<![\d.])v?" + re.escape(pin(root, name)) + r"(?![\d.])", actual):
                    raise ValueError(f"Expected {name}@{pin(root, name)}; found {actual}")
                paths.append(str(Path(executable).parent))
            print(os.pathsep.join(paths))
        else:
            raise ValueError("Unknown toolchain query")
    except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        print(f"TOOLCHAIN SETUP_FAILED: {error}", file=sys.stderr)
        raise SystemExit(2)
