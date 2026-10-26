"""Own test-run scratch and bounded, boot-scoped failure evidence. Never prune build outputs."""

import argparse
import contextlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
import uuid
import zipfile
from pathlib import Path

sys.dont_write_bytecode = True
from diagnostics import state_root

DEFAULT_SECONDS = 0
MAX_SECONDS = 2 * 60 * 60
OWNER = "nami.test-run.v1"
IDENTITY = re.compile(r"[0-9a-f]{32}\Z")


def boot_identity():
    if sys.platform == "darwin":
        return subprocess.check_output(["/usr/sbin/sysctl", "-n", "kern.bootsessionuuid"], text=True).strip()
    if sys.platform == "win32":
        return subprocess.check_output([
            "powershell", "-NoProfile", "-NonInteractive", "-Command",
            "(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o')"
        ], text=True).strip()
    return Path("/proc/sys/kernel/random/boot_id").read_text().strip()


@contextlib.contextmanager
def locked(path, *, blocking=True, create=False):
    if path.is_symlink():
        raise ValueError(f"Refusing a linked lease: {path}")
    stream = path.open("a+b" if create else "r+b")
    acquired = False
    try:
        if os.name == "nt":
            import msvcrt
            if path.stat().st_size == 0:
                stream.write(b"0")
                stream.flush()
            stream.seek(0)
            try:
                msvcrt.locking(stream.fileno(), msvcrt.LK_LOCK if blocking else msvcrt.LK_NBLCK, 1)
            except OSError as error:
                if not blocking and error.errno in (11, 13):
                    raise BlockingIOError(error.errno, str(error)) from error
                raise
        else:
            import fcntl
            fcntl.flock(stream, fcntl.LOCK_EX | (0 if blocking else fcntl.LOCK_NB))
        acquired = True
        yield stream
    finally:
        if acquired:
            if os.name == "nt":
                import msvcrt
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream, fcntl.LOCK_UN)
        stream.close()


def write_manifest(path, value):
    temporary = path.with_suffix(".tmp")
    with temporary.open("w") as stream:
        json.dump(value, stream, sort_keys=True)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)


def read_manifest(directory):
    path = directory / "owner.json"
    if path.is_symlink():
        raise ValueError(f"Refusing linked manifest: {path}")
    value = json.loads(path.read_text())
    if (value.get("owner") != OWNER or value.get("id") != directory.name
            or value.get("state") not in {"active", "failed", "passed"}
            or not isinstance(value.get("boot"), str) or not value["boot"]):
        raise ValueError(f"Invalid artifact owner: {path}")
    for key in ("started", "expires"):
        if type(value.get(key)) not in (int, float) or not 0 < value[key] < float("inf"):
            raise ValueError(f"Invalid artifact timestamp: {path}")
    if value["state"] != "active":
        finished = value.get("finished")
        minutes = value.get("retentionMinutes")
        if (type(finished) not in (int, float) or type(minutes) is not int or not 0 <= minutes <= 120
                or not value["started"] <= finished <= value["expires"] <= finished + MAX_SECONDS
                or value["expires"] != finished + minutes * 60):
            raise ValueError(f"Invalid artifact deadline: {path}")
    elif value["expires"] != value["started"] + MAX_SECONDS:
        raise ValueError(f"Invalid active artifact deadline: {path}")
    return value


class ArtifactStore:
    def __init__(self, root, *, now=time.time, boot=None):
        self.root = Path(root)
        if self.root.resolve() != self.root.absolute():
            raise ValueError("Artifact store must not traverse symlinks")
        self.now = now
        self.boot = boot if boot is not None else boot_identity()
        self.root.mkdir(parents=True, exist_ok=True)

    def begin(self):
        return ArtifactRun(self)

    def sweep(self, *, dry_run=False):
        results = []
        with locked(self.root / ".registry.lock", create=True):
            for path in sorted(self.root.iterdir()):
                if path.name == ".registry.lock":
                    continue
                if path.is_symlink() or not path.is_dir() or not IDENTITY.fullmatch(path.name):
                    if not dry_run:
                        if path.is_dir() and not path.is_symlink():
                            shutil.rmtree(path)
                        else:
                            path.unlink()
                    results.append({"id": path.name, "action": "would-delete" if dry_run else "deleted", "reason": "owned-scratch"})
                    continue
                guard = locked(path / ".lease", blocking=False, create=True)
                busy = False
                acquired = False
                try:
                    try:
                        guard.__enter__()
                        acquired = True
                    except BlockingIOError:
                        busy = True
                    value = read_manifest(path)
                    if value.get("finished", value["started"]) > self.now() + 5:
                        raise ValueError("Invalid future timestamp")
                    expired = value["boot"] != self.boot or self.now() >= value["expires"]
                    abandoned = value["state"] == "active" and not busy
                    remove = expired or abandoned
                    reason = "previous-boot" if value["boot"] != self.boot else "expired" if expired else "abandoned" if abandoned else "active" if busy else "retained"
                except (ValueError, KeyError, FileNotFoundError):
                    remove, reason = True, "invalid-metadata"
                finally:
                    if acquired:
                        guard.__exit__(None, None, None)
                # This entire namespace is owned scratch. A lock never extends its deadline.
                if remove and not dry_run:
                    shutil.rmtree(path)
                action = "would-delete" if remove and dry_run else "deleted" if remove else "active" if busy else "retained"
                results.append({"id": path.name, "action": action, "reason": reason})
        return results

class ArtifactRun:
    def __init__(self, store):
        self.store = store
        self.identity = uuid.uuid4().hex
        self.root = store.root / self.identity
        with locked(store.root / ".registry.lock", create=True):
            self.root.mkdir()
            self._lease = locked(self.root / ".lease", create=True)
            self.lease = self._lease.__enter__()
            self.value = dict(owner=OWNER, id=self.identity, state="active", boot=store.boot,
                              started=store.now())
            self.value["expires"] = self.value["started"] + MAX_SECONDS
            write_manifest(self.root / "owner.json", self.value)
        self.directory = self.root / "payload"
        self.workspaces = self.root / "workspaces"
        self.directory.mkdir()
        self.workspaces.mkdir()
        self.closed = False

    def finish(self, passed, *, retain_minutes=DEFAULT_SECONDS // 60):
        if self.closed:
            raise ValueError("The run is already closed")
        if not 0 <= retain_minutes <= MAX_SECONDS // 60:
            raise ValueError("Retention must be between 0 and 120 minutes")
        try:
            finished = self.store.now()
            if not passed and retain_minutes:
                temporary = self.root / "evidence.tmp"
                with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED) as archive:
                    for path in sorted(self.directory.rglob("*")):
                        if path.is_symlink():
                            raise ValueError(f"Cannot seal linked evidence: {path}")
                        if path.is_file():
                            archive.write(path, path.relative_to(self.directory))
                temporary.replace(self.root / "evidence.zip")
                if os.name != "nt":
                    os.chmod(self.root / "evidence.zip", 0o400)
            self.value.update(state="passed" if passed else "failed", finished=finished,
                              retentionMinutes=0 if passed else retain_minutes,
                              expires=finished + (0 if passed else retain_minutes * 60))
            write_manifest(self.root / "owner.json", self.value)
            shutil.rmtree(self.directory)
            shutil.rmtree(self.workspaces)
            return dict(self.value)
        finally:
            self._lease.__exit__(None, None, None)
            self.closed = True
            self.store.sweep()


def store_for(root):
    return ArtifactStore(state_root(root) / "fixture-runs")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    sweep = commands.add_parser("sweep", help="Remove only expired or abandoned Nami-owned test runs")
    sweep.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    store = store_for(Path(__file__).resolve().parents[2])
    result = store.sweep(dry_run=args.dry_run)
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError) as error:
        print(f"ARTIFACT CLEANUP FAILED: {error}", file=sys.stderr)
        raise SystemExit(2)
