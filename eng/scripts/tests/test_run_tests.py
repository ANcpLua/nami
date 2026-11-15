import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from run_tests import HarnessError, lanes_of, missing_package_file, selection

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


if __name__ == "__main__":
    unittest.main()
