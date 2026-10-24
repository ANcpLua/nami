"""Serialize repository verification; nested build/test commands share the owner's lease."""

import json
import os
import signal
import subprocess
import sys
import uuid
from pathlib import Path
from diagnostics import begin, clean_workspace, state_root

if os.name == "nt":
    import msvcrt
else:
    import fcntl


def main():
    root, entry, *arguments = sys.argv[1:]
    root = str(Path(root).resolve())
    path = state_root(root) / "verification.lock"
    path.parent.mkdir(parents=True, exist_ok=True)
    environment = dict(os.environ, NAMI_LOCKED_ENTRY=entry, NAMI_DIAGNOSTICS_ROOT=str(state_root(root)))
    for key in ("MSBuildSDKsPath", "MSBUILD_EXE_PATH", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER"):
        environment.pop(key, None)
    with path.open("a+") as lease:
        lease.seek(0)
        try:
            owner = json.load(lease)
        except (ValueError, OSError):
            owner = {}
        inherited = owner.get("token") == os.environ.get("NAMI_VERIFICATION_TOKEN") and owner.get("root") == root
        if inherited and os.name != "nt":
            try:
                os.kill(owner["pid"], 0)
            except (OSError, KeyError):
                inherited = False
        if not inherited:
            try:
                if os.name == "nt":
                    lease.seek(0)
                    msvcrt.locking(lease.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    fcntl.flock(lease, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except OSError:
                print(f"VERIFICATION BUSY: {owner.get('entry', 'another command')} (pid {owner.get('pid', '?')}). Retry after it finishes.", file=sys.stderr)
                return 2
            token = uuid.uuid4().hex
            lease.seek(0)
            lease.truncate()
            json.dump(dict(root=root, pid=os.getpid(), token=token, entry=entry), lease)
            lease.flush()
            environment["NAMI_VERIFICATION_TOKEN"] = token
            clean_workspace(root)
            if entry.endswith("/build.sh"):
                begin(root, "build")
                from inspect_code import sources
                environment["NAMI_DIAGNOSTIC_SOURCE"] = sources(Path(root))
        command = ["bash", entry.replace("\\", "/"), *arguments] if entry.endswith(".sh") else [entry, *arguments]
        with subprocess.Popen(command, cwd=root, env=environment,
                              start_new_session=not inherited and os.name != "nt") as process:
            try:
                code = process.wait()
                return code if code >= 0 else 128 - code
            except KeyboardInterrupt:
                if not inherited and os.name != "nt":
                    os.killpg(process.pid, signal.SIGTERM)
                else:
                    process.terminate()
                process.wait()
                return 130
            finally:
                if not inherited:
                    clean_workspace(root)
                    summary_path = state_root(root) / "build/current/summary.json"
                    if entry.endswith("/build.sh") and summary_path.is_file():
                        summary = json.loads(summary_path.read_text())
                        if summary.get("status") == "RUNNING":
                            summary.update(status="completed" if process.returncode == 0 else "failed",
                                           exitCode=process.returncode)
                            summary_path.write_text(json.dumps(summary) + "\n")
                    lease.seek(0)
                    lease.truncate()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        print(f"VERIFICATION SETUP_FAILED: {error}", file=sys.stderr)
        raise SystemExit(2)
