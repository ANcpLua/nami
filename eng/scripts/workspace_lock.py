"""Serialize repository verification; nested build/test commands share the owner's lease."""

import json
import os
import signal
import subprocess
import sys
import uuid
from pathlib import Path

if os.name == "nt":
    import msvcrt
else:
    import fcntl


def main():
    root, entry, *arguments = sys.argv[1:]
    root = str(Path(root).resolve())
    path = Path(root, "artifacts", "verification.lock")
    path.parent.mkdir(parents=True, exist_ok=True)
    environment = dict(os.environ, NAMI_LOCKED_ENTRY=entry)
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
                    lease.seek(0)
                    lease.truncate()


if __name__ == "__main__":
    raise SystemExit(main())
