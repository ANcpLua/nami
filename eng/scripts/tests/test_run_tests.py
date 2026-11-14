import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from run_tests import missing_package_file


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


if __name__ == "__main__":
    unittest.main()
