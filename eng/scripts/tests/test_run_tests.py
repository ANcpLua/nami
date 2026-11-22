import os
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from run_tests import (LINUX_ONLY, HarnessError, lanes_of, missing_package_file, platform_minimum, read_results,
                       result_lines, selection, verdict)

SUITES = ("Unit", "Integration", "EndToEnd")
# Two test lanes in one suite and none in the others, as plan rule 14 describes them.
MANIFEST = {
    "Unit": {"minimumExpectedTests": 241, "timeout": "5m"},
    "Integration": {"minimumExpectedTests": 502, "timeout": "15m"},
    "EndToEnd": {"minimumExpectedTests": 47, "timeout": "20m"},
    "lanes": {
        "later": {"suite": "EndToEnd", "category": "Later", "minimumExpectedTests": 9, "timeout": "25m"},
        "live": {"suite": "EndToEnd", "category": "Live", "minimumExpectedTests": 1, "timeout": "5m"},
    },
}


class PackageCompletenessTests(unittest.TestCase):
    def test_finder_file_recorded_by_restore_is_not_a_missing_package_file(self):
        with tempfile.TemporaryDirectory() as temporary:
            package = Path(temporary)
            (package / "lib").mkdir()
            (package / "lib" / "library.dll").write_bytes(b"")
            self.assertIsNone(missing_package_file(package, [".DS_Store", "lib/.DS_Store", "lib/library.dll"]))

    def test_missing_package_file_is_still_reported(self):
        with tempfile.TemporaryDirectory() as temporary:
            package = Path(temporary)
            self.assertEqual("lib/library.dll", missing_package_file(package, [".DS_Store", "lib/library.dll"]))

    def test_file_that_only_resembles_a_finder_file_is_checked(self):
        with tempfile.TemporaryDirectory() as temporary:
            package = Path(temporary)
            self.assertEqual("lib/.DS_Store.txt", missing_package_file(package, ["lib/.DS_Store.txt"]))


class TestLaneTests(unittest.TestCase):
    def test_normal_run_leaves_out_every_lane_of_its_suite_and_keeps_the_suite_floor(self):
        self.assertEqual(("TestCategory!=Later&TestCategory!=Live", 47, "20m"), selection(MANIFEST, "EndToEnd"))
        self.assertEqual((None, 241, "5m"), selection(MANIFEST, "Unit"))

    def test_lane_run_selects_its_category_against_its_own_floor_and_timeout(self):
        self.assertEqual(("TestCategory=Later", 9, "25m"), selection(MANIFEST, "EndToEnd", lane="later"))
        self.assertEqual(("TestCategory=Live", 1, "5m"), selection(MANIFEST, "EndToEnd", lane="live"))

    def test_filtered_run_keeps_the_lane_exclusion_and_proves_no_floor(self):
        self.assertEqual(("(FullyQualifiedName~Ingest)&TestCategory!=Later&TestCategory!=Live", 1, "20m"),
                         selection(MANIFEST, "EndToEnd", user_filter="FullyQualifiedName~Ingest"))
        self.assertEqual(("FullyQualifiedName~Ingest", 1, "15m"),
                         selection(MANIFEST, "Integration", user_filter="FullyQualifiedName~Ingest"))
        self.assertEqual(("(FullyQualifiedName~Ingest)&TestCategory=Later", 1, "25m"),
                         selection(MANIFEST, "EndToEnd", lane="later", user_filter="FullyQualifiedName~Ingest"))

    def test_lane_without_a_known_suite_or_a_plain_category_is_refused(self):
        self.assertEqual(MANIFEST["lanes"], lanes_of(MANIFEST, SUITES))
        for broken in ({"suite": "Elsewhere", "category": "Later", "minimumExpectedTests": 1},
                       {"suite": "EndToEnd", "category": "Later|Live", "minimumExpectedTests": 1},
                       {"suite": "EndToEnd", "category": "Later", "minimumExpectedTests": 0}):
            with self.subTest(broken=broken), self.assertRaises(HarnessError):
                lanes_of({**MANIFEST, "lanes": {"broken": broken}}, SUITES)


ROOT = Path(__file__).resolve().parents[3]
# A report as the test platform writes it: the skipped results are those of a run with /proc taken away.
TRX = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult executionId="1" testName="Runs" outcome="Passed" />
    <UnitTestResult executionId="2" testName="A gen0 budget from the host's own environment" outcome="NotExecuted">
      <Output>
        <ErrorInfo>
          <Message>Assert.Inconclusive. reads /proc; Linux only</Message>
          <StackTrace>   at Nami.EndToEndTests.LinuxProcess.Require()</StackTrace>
        </ErrorInfo>
      </Output>
    </UnitTestResult>
    <UnitTestResult executionId="3" testName="Skipped for another reason" outcome="NotExecuted">
      <Output>
        <ErrorInfo>
          <Message>Assert.Inconclusive. Linux only applies to the test above, not to this one.</Message>
        </ErrorInfo>
      </Output>
    </UnitTestResult>
    <UnitTestResult executionId="4" testName="Skipped without a reason" outcome="NotExecuted" />
  </Results>
  <ResultSummary outcome="Completed">
    <Counters total="4" executed="1" passed="1" failed="0" />
  </ResultSummary>
</TestRun>
"""


class LinuxOnlySkipTests(unittest.TestCase):
    """Ledger N123: on a machine that is not Linux, the tests that need Linux skip; that alone is no failure."""

    def counts(self, passed=60, failed=0, skipped=9, linux_only=9):
        return {"total": passed + failed + skipped, "passed": passed, "failed": failed, "skipped": skipped,
                "linuxOnly": linux_only}

    def test_on_linux_a_skip_fails_the_run_whatever_its_reason_and_counts_for_no_floor(self):
        self.assertEqual((2, None), verdict(self.counts(), 60, on_linux=True))
        self.assertEqual((9, "executed fewer than 69 tests"), verdict(self.counts(), 69, on_linux=True))
        self.assertEqual((0, None), verdict(self.counts(passed=69, skipped=0, linux_only=0), 69, on_linux=True))
        self.assertEqual(["--minimum-expected-tests", "69"], platform_minimum(69, on_linux=True))

    def test_off_linux_the_linux_only_skips_count_as_run(self):
        # 60 of 69 EndToEnd tests run on a Mac; the nine others read /proc.
        self.assertEqual((0, None), verdict(self.counts(), 69, on_linux=False))
        # The platform counts executed tests only and would refuse these 60 against a floor of 69.
        self.assertEqual([], platform_minimum(69, on_linux=False))

    def test_off_linux_any_other_skip_a_failure_and_a_missing_test_still_fail_the_run(self):
        self.assertEqual((2, None), verdict(self.counts(skipped=10), 69, on_linux=False))
        self.assertEqual((2, None), verdict(self.counts(passed=59, failed=1), 69, on_linux=False))
        self.assertEqual((9, "executed fewer than 70 tests"), verdict(self.counts(), 70, on_linux=False))

    def test_report_counts_a_skip_as_linux_only_by_the_end_of_its_reason(self):
        with tempfile.TemporaryDirectory() as temporary:
            (Path(temporary) / "suite.trx").write_text(TRX, encoding="utf-8")
            counts, names, failed = read_results(Path(temporary))
        self.assertEqual({"total": 4, "passed": 1, "failed": 0, "skipped": 3, "linuxOnly": 1}, counts)
        self.assertEqual(4, len(names))
        self.assertEqual([], failed)

    def test_every_linux_only_test_gives_the_reason_the_runner_reads(self):
        reasons = {path.name: re.findall(r'Assert\.Inconclusive\("([^"]*)"\)', path.read_text(encoding="utf-8"))
                   for path in (ROOT / "tests/Nami.EndToEndTests/LinuxProcess.cs",
                                ROOT / "tests/Nami.IntegrationTests/StorageLifetimeTests.cs")}
        self.assertEqual(["reads /proc; " + LINUX_ONLY], reasons["LinuxProcess.cs"])
        self.assertIn("needs /dev/shm as a second volume; " + LINUX_ONLY, reasons["StorageLifetimeTests.cs"])


# A report with one failed and one passed test, cut down from one the test platform wrote.
FAILED_TRX = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult executionId="1" testId="a" testName="Catalog_EmbeddedConstants_MatchRegistrySnapshot" outcome="Passed" />
    <UnitTestResult executionId="2" testId="b" testName="Since_Boundaries_HalfOpenAgainstWatermark" outcome="Failed">
      <Output>
        <ErrorInfo>
          <Message>Assertion failed. Expected collection to contain exactly one element.</Message>
        </ErrorInfo>
      </Output>
    </UnitTestResult>
  </Results>
  <TestDefinitions>
    <UnitTest name="Catalog_EmbeddedConstants_MatchRegistrySnapshot" id="a">
      <Execution id="1" />
      <TestMethod className="Nami.IntegrationTests.IngestStoreTests" name="Catalog_EmbeddedConstants_MatchRegistrySnapshot" />
    </UnitTest>
    <UnitTest name="Since_Boundaries_HalfOpenAgainstWatermark" id="b">
      <Execution id="2" />
      <TestMethod className="Nami.IntegrationTests.ToolContractTests" name="Since_Boundaries_HalfOpenAgainstWatermark" />
    </UnitTest>
  </TestDefinitions>
  <ResultSummary outcome="Failed">
    <Counters total="2" executed="2" passed="1" failed="1" />
  </ResultSummary>
</TestRun>
"""
FAILED_TEST = "Nami.IntegrationTests.ToolContractTests.Since_Boundaries_HalfOpenAgainstWatermark"


class FailedTestNameTests(unittest.TestCase):
    """Ledger N139: a failed gate showed "Failed: 1" and no name; the log names each failed test."""

    def report(self, trx):
        with tempfile.TemporaryDirectory() as temporary:
            (Path(temporary) / "suite.trx").write_text(trx, encoding="utf-8")
            counts, _, failed = read_results(Path(temporary))
        return result_lines("Integration", counts, failed, 2)

    def test_report_names_the_failed_test_and_no_other(self):
        self.assertEqual(["TEST RESULT Integration: Passed: 1, Failed: 1, Skipped: 0, Total: 2, Exit: 2",
                          "TEST FAILED Integration: " + FAILED_TEST], self.report(FAILED_TRX))
        # A data row or a display name: the method leads, because the source and --filter know it.
        row = FAILED_TRX.replace('testId="b" testName="Since_Boundaries_HalfOpenAgainstWatermark"',
                                 'testId="b" testName="A window that ends at the watermark"')
        self.assertEqual("TEST FAILED Integration: " + FAILED_TEST + ": A window that ends at the watermark",
                         self.report(row)[1])
        # A timeout or an error is no pass either, and a report without definitions still has the result's name.
        bare = FAILED_TRX[:FAILED_TRX.index("  <TestDefinitions>")] + FAILED_TRX[FAILED_TRX.index("  <ResultSummary"):]
        self.assertEqual(["TEST FAILED Integration: Since_Boundaries_HalfOpenAgainstWatermark"],
                         self.report(bare.replace('outcome="Failed">', 'outcome="Timeout">', 1))[1:])

    def test_quiet_gate_log_shows_the_failed_test_as_the_run_prints_it(self):
        # The gate runs quietly: quiet.sh shows chosen lines at once and, after a failure, the last 60. More
        # output than that follows the name here, so only the first way can show it.
        lines = self.report(FAILED_TRX) + [f"attachment {number}" for number in range(100)]
        with tempfile.TemporaryDirectory() as temporary:
            result = subprocess.run(
                ["bash", str(ROOT / "eng/scripts/quiet.sh"), "failed-test-names", "--", "bash", "-c",
                 'printf "%s\n" "$@"; exit 2', "report", *lines],
                cwd=temporary, capture_output=True, text=True,
                env={**os.environ, "NAMI_VERBOSE": "0", "NAMI_DIAGNOSTICS_ROOT": temporary})
        shown = result.stdout.splitlines()
        self.assertEqual(2, result.returncode, result.stdout + result.stderr)
        self.assertEqual(lines[:2], [line for line in shown if line.startswith("TEST ")])
        self.assertNotIn("attachment 0", shown)
        self.assertFalse(any("IngestStoreTests" in line for line in shown))


if __name__ == "__main__":
    unittest.main()
