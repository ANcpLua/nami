import contextlib
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import branch_housekeeping
from branch_housekeeping import IN_MAIN, KEEP, LISTED, batches, parse_push, read_patterns, select

ROOT = Path(__file__).resolve().parents[3]
LIST = """
# a comment
check/eval10-*
storage/write-queue-gaps

lean/storage-write-queue-red
"""


class SelectionTests(unittest.TestCase):
    def test_list_file_ignores_comments_and_blank_lines(self):
        self.assertEqual(["check/eval10-*", "storage/write-queue-gaps", "lean/storage-write-queue-red"],
                         read_patterns(LIST))

    def test_branch_in_main_is_selected_without_a_list_entry(self):
        selected, kept = select(["feature/merged", "feature/open"], {"feature/merged"}, [])
        self.assertEqual([("feature/merged", [IN_MAIN])], selected)
        self.assertEqual([], kept)

    def test_exact_name_and_glob_select_unmerged_branches(self):
        branches = ["check/eval10-a", "check/eval10-b/deep", "check/eval100", "storage/write-queue-gaps",
                    "storage/write-queue-gaps-2", "feature/open"]
        selected, _ = select(branches, set(), read_patterns(LIST))
        self.assertEqual([("check/eval10-a", [LISTED]), ("check/eval10-b/deep", [LISTED]),
                          ("storage/write-queue-gaps", [LISTED])], selected)

    def test_branch_that_is_merged_and_listed_names_both_reasons(self):
        selected, _ = select(["storage/write-queue-gaps"], {"storage/write-queue-gaps"}, read_patterns(LIST))
        self.assertEqual([("storage/write-queue-gaps", [IN_MAIN, LISTED])], selected)

    def test_keep_list_wins_over_main_and_over_the_list(self):
        merged = {"main", "contract/design-spikes", "m2/contracts-draft"}
        selected, kept = select([*KEEP, "check/eval10-a"], merged, ["*"])
        self.assertEqual([("check/eval10-a", [LISTED])], selected)
        self.assertEqual(sorted(KEEP), [name for name, _ in kept])
        self.assertEqual(("main", "contract/design-spikes", "lean/storage-write-queue",
                          "lean/storage-write-queue-red", "m2/contracts-draft"), KEEP)

    def test_shipped_list_never_reaches_a_kept_branch(self):
        patterns = read_patterns((ROOT / "eng/branches-to-delete.txt").read_text(encoding="utf-8"))
        self.assertIn("check/eval10-*", patterns)
        selected, kept = select([*KEEP, "check/eval10-x", "privacy/redaction-stage", "feature/open"], {"main"},
                                patterns)
        self.assertEqual(["check/eval10-x", "privacy/redaction-stage"], [name for name, _ in selected])
        # The shipped list names lean/storage-write-queue-red; main is kept although its tip is in main.
        self.assertEqual([("lean/storage-write-queue-red", [LISTED]), ("main", [IN_MAIN])], kept)

    def test_batches_are_small_and_complete(self):
        names = [f"b{number}" for number in range(45)]
        split = batches(names)
        self.assertEqual([20, 20, 5], [len(batch) for batch in split])
        self.assertEqual(names, [name for batch in split for name in batch])
        self.assertEqual([], batches([]))

    def test_push_output_tells_deleted_from_rejected(self):
        output = ("To https://example.invalid/repository\n"
                  "-\t:refs/heads/check/a\t[deleted]\n"
                  "!\t:refs/heads/check/b\t[rejected] (stale info)\n"
                  "Done\n")
        self.assertEqual({"check/a": None, "check/b": "[rejected] (stale info)"}, parse_push(output))


class RepositoryTests(unittest.TestCase):
    """The command line against a real remote: a bare repository in a temporary folder."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        root = Path(self.temporary.name)
        self.remote = root / "remote.git"
        self.clone = root / "clone"
        self.list = root / "list.txt"
        self.list.write_text("# throwaway branches\ncheck/*\nlean/storage-write-queue-red\n", encoding="utf-8")
        subprocess.run(["git", "init", "--quiet", "--bare", "--initial-branch=main", str(self.remote)], check=True)
        subprocess.run(["git", "init", "--quiet", "--initial-branch=main", str(self.clone)], check=True)
        self.git("remote", "add", "origin", str(self.remote))
        self.commit("first")
        self.git("branch", "feature/merged")
        self.commit("second")
        self.git("branch", "feature/merged-at-tip")
        for branch in ["feature/open", "check/red", "lean/storage-write-queue-red", "contract/design-spikes"]:
            self.git("checkout", "--quiet", "-b", branch, "main")
            if branch != "contract/design-spikes":
                self.commit(branch)
        self.git("checkout", "--quiet", "main")
        self.git("push", "--quiet", "--all", "origin")
        self.git("fetch", "--quiet", "origin")

    def tearDown(self):
        self.temporary.cleanup()

    def git(self, *arguments, repository=None):
        command = ["git", "-C", str(repository or self.clone), "-c", "user.name=nami",
                   "-c", "user.email=nami@example.invalid", "-c", "commit.gpgsign=false", *arguments]
        return subprocess.run(command, check=True, capture_output=True, text=True).stdout.strip()

    def commit(self, message):
        self.git("commit", "--quiet", "--allow-empty", "-m", message)

    def remote_heads(self):
        return sorted(self.git("for-each-ref", "--format=%(refname:lstrip=2)", "refs/heads",
                               repository=self.remote).splitlines())

    def run_script(self, confirm):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            code = branch_housekeeping.main(["--list", str(self.list), "--repository", str(self.clone),
                                             "--confirm", confirm])
        return code, output.getvalue()

    def test_dry_run_prints_the_selection_and_deletes_nothing(self):
        before = self.remote_heads()
        for confirm in ["", "Delete", "yes"]:
            code, output = self.run_script(confirm)
            self.assertEqual(0, code, output)
            self.assertIn("7 branches on origin; 3 selected, 4 stay", output)
            self.assertIn("select  check/red  (listed)", output)
            self.assertIn("select  feature/merged  (in main)", output)
            self.assertIn("select  feature/merged-at-tip  (in main)", output)
            self.assertIn("keep    main  (in main; on the keep list)", output)
            self.assertIn("keep    contract/design-spikes  (in main; on the keep list)", output)
            self.assertIn("keep    lean/storage-write-queue-red  (listed; on the keep list)", output)
            self.assertNotIn("feature/open", output)
            self.assertIn("Dry run: nothing deleted.", output)
            self.assertEqual(before, self.remote_heads())

    def test_confirm_delete_removes_exactly_the_selection(self):
        code, output = self.run_script("delete")
        self.assertEqual(0, code, output)
        self.assertEqual(["contract/design-spikes", "feature/open", "lean/storage-write-queue-red", "main"],
                         self.remote_heads())
        for name in ["check/red", "feature/merged", "feature/merged-at-tip"]:
            self.assertIn(f"deleted      {name}", output)
        self.assertIn("Deleted 3 of 3 branches; 0 failed.", output)

    def test_branch_that_moved_after_the_fetch_stays(self):
        self.git("checkout", "--quiet", "check/red")
        self.commit("new work")
        self.git("push", "--quiet", "origin", "check/red")
        self.git("update-ref", "refs/remotes/origin/check/red", "check/red~1")
        code, output = self.run_script("delete")
        self.assertEqual(1, code, output)
        self.assertIn("not deleted  check/red", output)
        self.assertIn("Deleted 2 of 3 branches; 1 failed.", output)
        self.assertEqual(["check/red", "contract/design-spikes", "feature/open", "lean/storage-write-queue-red",
                          "main"], self.remote_heads())


if __name__ == "__main__":
    unittest.main()
