"""Select the remote branches that can go, and delete them when asked to.

A branch is selected when its tip is already in main, or when its name matches a line of the list file
(an exact name or a shell glob). The branches in KEEP are never selected. Without --confirm delete the
selection is only printed. The workflow branch-housekeeping runs this file.
"""

import argparse
import subprocess
import sys
from fnmatch import fnmatchcase
from pathlib import Path

KEEP = ("main", "contract/design-spikes", "lean/storage-write-queue", "m2/contracts-draft")
CONFIRM = "delete"
BATCH = 20
IN_MAIN = "in main"
LISTED = "listed"


def read_patterns(text):
    """The names and globs of a list file: one per line, without blank lines and # comments."""
    lines = (line.strip() for line in text.splitlines())
    return [line for line in lines if line and not line.startswith("#")]


def select(branches, in_main, patterns, keep=KEEP):
    """Split branch names into those to delete and those the keep list saved, each with its reasons.

    branches: every remote branch name. in_main: the names whose tip is contained in main.
    Returns (selected, kept): two sorted lists of (name, reasons); a branch with no reason is in neither.
    """
    selected, kept = [], []
    for name in sorted(set(branches)):
        reasons = []
        if name in in_main:
            reasons.append(IN_MAIN)
        if any(fnmatchcase(name, pattern) for pattern in patterns):
            reasons.append(LISTED)
        if reasons:
            (kept if name in keep else selected).append((name, reasons))
    return selected, kept


def batches(items, size=BATCH):
    return [items[start:start + size] for start in range(0, len(items), size)]


def parse_push(output):
    """Read `git push --porcelain` output of a delete: {branch: None when deleted, else the remote's reason}."""
    results = {}
    for line in output.splitlines():
        parts = line.split("\t")
        if len(parts) != 3 or not parts[1].startswith(":refs/heads/"):
            continue
        results[parts[1][len(":refs/heads/"):]] = None if parts[0] == "-" else parts[2]
    return results


def git(repository, *arguments, check=True):
    return subprocess.run(["git", "-C", str(repository), *arguments], check=check, capture_output=True, text=True)


def remote_branches(repository, remote, merged_into=None):
    """Branch name to tip, from the remote-tracking refs; with merged_into, only tips contained in that ref."""
    prefix = f"refs/remotes/{remote}"
    arguments = ["for-each-ref", "--format=%(refname) %(objectname)"]
    if merged_into:
        arguments.append(f"--merged={prefix}/{merged_into}")
    tips = {}
    for line in git(repository, *arguments, prefix).stdout.splitlines():
        reference, tip = line.split(" ")
        name = reference[len(prefix) + 1:]
        if name != "HEAD":
            tips[name] = tip
    return tips


def delete(repository, remote, batch, tips):
    """Delete one batch; a branch whose tip moved since it was selected stays (the lease fails)."""
    leases = [f"--force-with-lease=refs/heads/{name}:{tips[name]}" for name in batch]
    result = git(repository, "push", "--porcelain", *leases, remote, *[f":refs/heads/{name}" for name in batch],
                 check=False)
    outcome = parse_push(result.stdout)
    error = (result.stderr.strip().splitlines() or ["no answer for this branch"])[-1]
    return {name: outcome.get(name, error) for name in batch}


def main(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--list", required=True, type=Path, help="file of branch names and globs to delete")
    parser.add_argument("--repository", default=".", type=Path)
    parser.add_argument("--remote", default="origin")
    parser.add_argument("--main", default="main")
    parser.add_argument("--confirm", default="", help=f"'{CONFIRM}' deletes; anything else only prints")
    options = parser.parse_args(arguments)

    tips = remote_branches(options.repository, options.remote)
    if options.main not in tips:
        print(f"{options.remote}/{options.main} is missing: fetch all branches first.", file=sys.stderr)
        return 2
    in_main = set(remote_branches(options.repository, options.remote, options.main))
    selected, kept = select(tips, in_main, read_patterns(options.list.read_text(encoding="utf-8")))

    print(f"{len(tips)} branches on {options.remote}; {len(selected)} selected, "
          f"{len(tips) - len(selected)} stay ({len(kept)} of them only because of the keep list).")
    for name, reasons in kept:
        print(f"  keep    {name}  ({', '.join(reasons)}; on the keep list)")
    for name, reasons in selected:
        print(f"  select  {name}  ({', '.join(reasons)})")

    confirm = options.confirm.strip()
    if confirm != CONFIRM:
        note = f" The input confirm was '{confirm}', not '{CONFIRM}'." if confirm else ""
        print(f"Dry run: nothing deleted.{note} Run again with confirm: {CONFIRM} to delete these branches.")
        return 0

    failed = 0
    names = [name for name, _ in selected]
    for number, batch in enumerate(batches(names), start=1):
        print(f"Batch {number}: {len(batch)} branches")
        for name, reason in delete(options.repository, options.remote, batch, tips).items():
            failed += reason is not None
            print(f"  deleted      {name}" if reason is None else f"  not deleted  {name}  ({reason})")
    print(f"Deleted {len(names) - failed} of {len(names)} branches; {failed} failed.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
