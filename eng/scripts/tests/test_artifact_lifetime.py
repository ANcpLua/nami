import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from artifact_lifetime import ArtifactStore, MAX_SECONDS
import run_tests
from diagnostics import state_root


class ArtifactLifetimeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.clock = 10000.0
        self.store = ArtifactStore(self.root / "owned", now=lambda: self.clock, boot="boot-one")

    def tearDown(self):
        self.temporary.cleanup()

    def failed(self):
        run = self.store.begin()
        (run.directory / "failure.log").write_text("original diagnostic")
        (run.workspaces / "mutable-work").mkdir()
        (run.workspaces / "mutable-work/database").write_text("mutable database")
        value = run.finish(False, retain_minutes=30)
        return run, value

    def test_success_removes_owned_payload_but_preserves_unowned_outputs(self):
        protected = self.root / "artifacts/publish"
        protected.mkdir(parents=True)
        (protected / "last-good").write_text("working application")
        unknown = self.root / "unknown"
        unknown.mkdir()
        (unknown / "source.cs").write_text("source")
        run = self.store.begin()
        (run.directory / "success.log").write_text("passed")
        run.finish(True)
        self.assertFalse(run.root.exists())
        self.assertEqual("working application", (protected / "last-good").read_text())
        self.assertEqual("source", (unknown / "source.cs").read_text())

    def test_failure_seals_evidence_and_discards_mutable_workspace(self):
        run, value = self.failed()
        self.assertFalse(run.directory.exists())
        self.assertFalse(run.workspaces.exists())
        with zipfile.ZipFile(run.root / "evidence.zip") as archive:
            self.assertEqual(["failure.log"], archive.namelist())
            self.assertEqual(b"original diagnostic", archive.read("failure.log"))
        self.assertEqual(self.clock + 1800, value["expires"])
        self.assertEqual(value, json.loads((run.root / "owner.json").read_text()))

    def test_exact_deadline_expires_without_another_test_run(self):
        run, value = self.failed()
        self.clock = value["expires"] - 0.001
        self.assertEqual("retained", self.store.sweep()[0]["action"])
        self.clock = value["expires"]
        self.assertEqual("deleted", self.store.sweep()[0]["action"])
        self.assertFalse(run.root.exists())

    def test_reboot_expires_evidence_before_its_time_deadline(self):
        run, _ = self.failed()
        next_boot = ArtifactStore(self.store.root, now=lambda: self.clock + 1, boot="boot-two")
        self.assertEqual("previous-boot", next_boot.sweep()[0]["reason"])
        self.assertFalse(run.root.exists())

    def test_reading_does_not_renew_deadline(self):
        run, value = self.failed()
        self.clock += 1000
        with zipfile.ZipFile(run.root / "evidence.zip") as archive:
            self.assertEqual(b"original diagnostic", archive.read("failure.log"))
        self.clock = value["expires"]
        self.store.sweep()
        self.assertFalse(run.root.exists())

    def test_optional_duration_has_a_finite_maximum(self):
        for minutes in (-1, 121):
            with self.subTest(minutes=minutes), self.assertRaises(ValueError):
                run = self.store.begin()
                try:
                    run.finish(False, retain_minutes=minutes)
                finally:
                    run.finish(True)
        run = self.store.begin()
        value = run.finish(False, retain_minutes=120)
        self.assertEqual(value["finished"] + MAX_SECONDS, value["expires"])
        self.clock = value["expires"]
        self.store.sweep()
        self.assertFalse(run.root.exists())

    def test_failure_is_deleted_immediately_without_explicit_retention(self):
        run = self.store.begin()
        (run.directory / "failure.log").write_text("failure")
        run.finish(False)
        self.assertFalse(run.root.exists())

    def test_active_lease_cannot_override_hard_deadline(self):
        run = self.store.begin()
        self.clock += MAX_SECONDS + 1
        try:
            self.assertEqual("deleted", self.store.sweep()[0]["action"])
            self.assertFalse(run.root.exists())
        finally:
            run._lease.__exit__(None, None, None)

    def test_dead_writer_is_collected_and_live_writer_is_protected(self):
        source = """
import sys, time
from artifact_lifetime import ArtifactStore
run = ArtifactStore(sys.argv[1], now=lambda:10000, boot='boot-one').begin()
print(run.identity, flush=True)
time.sleep(60)
"""
        env = dict(os.environ, PYTHONPATH=str(Path(__file__).resolve().parents[1]), PYTHONDONTWRITEBYTECODE="1")
        process = subprocess.Popen([sys.executable, "-c", source, str(self.store.root)],
                                   stdout=subprocess.PIPE, text=True, env=env)
        try:
            identity = process.stdout.readline().strip()
            self.assertEqual(32, len(identity))
            self.assertEqual("active", self.store.sweep()[0]["action"])
            process.kill()
            process.wait(timeout=10)
            self.assertEqual("abandoned", self.store.sweep()[0]["reason"])
            self.assertFalse((self.store.root / identity).exists())
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()
            process.stdout.close()

    def test_modified_snapshot_is_still_deleted_at_deadline(self):
        run, value = self.failed()
        archive = run.root / "evidence.zip"
        archive.chmod(0o600)
        archive.write_bytes(b"changed")
        self.clock = value["expires"]
        self.store.sweep()
        self.assertFalse(run.root.exists())

    def test_dry_run_reports_expiry_without_deleting(self):
        run, value = self.failed()
        self.clock = value["expires"]
        self.assertEqual("would-delete", self.store.sweep(dry_run=True)[0]["action"])
        self.assertTrue(run.root.exists())

    def test_invalid_deadline_cannot_silently_disable_cleanup(self):
        run, _ = self.failed()
        path = run.root / "owner.json"
        value = json.loads(path.read_text())
        value["expires"] += MAX_SECONDS
        path.write_text(json.dumps(value))
        self.assertEqual("invalid-metadata", self.store.sweep()[0]["reason"])
        self.assertFalse(run.root.exists())

    def test_unregistered_entry_in_owned_scratch_cannot_become_permanent(self):
        path = self.store.root / "unfinished-writer"
        path.mkdir()
        (path / "large.log").write_text("orphan")
        self.assertEqual("owned-scratch", self.store.sweep()[0]["reason"])
        self.assertFalse(path.exists())

    def test_old_implicit_retention_is_removed(self):
        run, _ = self.failed()
        path = run.root / "owner.json"
        value = json.loads(path.read_text())
        del value["retentionMinutes"]
        path.write_text(json.dumps(value))
        self.assertEqual("invalid-metadata", self.store.sweep()[0]["reason"])
        self.assertFalse(run.root.exists())

    def test_link_is_removed_without_touching_target(self):
        protected = self.root / "protected"
        protected.mkdir()
        (protected / "file").write_text("keep")
        (self.store.root / ("a" * 32)).symlink_to(protected, target_is_directory=True)
        self.assertEqual("deleted", self.store.sweep()[0]["action"])
        self.assertFalse((self.store.root / ("a" * 32)).exists())
        self.assertEqual("keep", (protected / "file").read_text())

    def test_runner_failure_preserves_exit_and_removes_report_by_default(self):
        project = self.root / "project"
        module = project / "built/Nami.UnitTests.dll"
        module.parent.mkdir(parents=True)
        module.touch()
        (module.parent / "testconfig.json").write_text(json.dumps({"commandLineOptions": {"show-test-results": "failed,skipped"}}))
        (project / "eng").mkdir()
        settings = {"project": "tests/Nami.UnitTests/Nami.UnitTests.csproj", "minimumExpectedTests": 1,
                    "verbosity": "Normal", "timeout": "1m"}
        (project / "eng/test-suites.json").write_text(json.dumps({name: settings for name in ("Unit", "Integration", "EndToEnd")}))

        def fake_execute(command, log, root, progress=None):
            directory = Path(command[command.index("--results-directory") + 1])
            directory.joinpath("results.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                '<ResultSummary><Counters total="1"/></ResultSummary><Results>'
                '<UnitTestResult executionId="one" testName="FailureCase" outcome="Failed"/>'
                '</Results></TestRun>')
            return 7

        argv = ["run_tests.py", "--root", str(project), "--dotnet", "unused", "--suite", "Unit",
                "--test-modules", str(module)]
        environment = dict(os.environ, NAMI_DIAGNOSTICS_HOME=str(self.root / "diagnostics"))
        environment.pop("TESTINGPLATFORM_EXITCODE_IGNORE", None)
        with patch.dict(os.environ, environment, clear=True), patch.object(sys, "argv", argv), patch.object(run_tests, "execute", fake_execute):
            self.assertEqual(7, run_tests.main())
            summary = json.loads((state_root(project) / "tests/current/summary.json").read_text())
            self.assertEqual("failed", summary["status"])
            self.assertEqual(7, summary["exitCode"])
            store = ArtifactStore(state_root(project) / "fixture-runs")
            self.assertFalse(Path(summary["directory"]).exists())
            self.assertFalse((store.root / summary["evidence"]["id"]).exists())
            self.assertIn("completed=1 status=failed", run_tests.test_status(state_root(project) / "tests/current"))


if __name__ == "__main__":
    unittest.main()
