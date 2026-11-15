"""Checks that keep the gate able to fail. GateTests.cs runs this file inside the gate."""
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SUITES = ("Unit", "Integration", "EndToEnd")
# Counted as P01 recorded them: a Node test is a line that starts with test(, the node:test function, so
# calls such as pattern.test( inside a test do not count; a Python test is a def test_ function.
NODE_TEST = re.compile(r"^test\(", re.M)
PYTHON_TEST = re.compile(r"^[ \t]*(?:async[ \t]+)?def test_", re.M)


def counted(folder, pattern, expression):
    return {path.relative_to(ROOT).as_posix(): len(expression.findall(path.read_text(encoding="utf-8")))
            for path in sorted((ROOT / folder).glob(pattern))}


class GateTests(unittest.TestCase):
    def test_suite_floors_match_recorded_baseline(self):
        floors = json.loads((ROOT / "eng/test-suites.json").read_text(encoding="utf-8"))
        baseline = json.loads((ROOT / "eng/test-baseline.json").read_text(encoding="utf-8"))
        self.assertEqual({name: baseline["suites"][name] for name in SUITES},
                         {name: floors[name]["minimumExpectedTests"] for name in SUITES},
                         "The floors in eng/test-suites.json must equal the suite counts in eng/test-baseline.json.")
        self.assertEqual(baseline["nodeTests"], counted("contracts/scripts/lib", "*.test.mjs", NODE_TEST),
                         "Node tests differ from eng/test-baseline.json; a commit that adds or removes tests updates it.")
        self.assertEqual(baseline["pythonTests"], counted("eng/scripts/tests", "test_*.py", PYTHON_TEST),
                         "Python tests differ from eng/test-baseline.json; a commit that adds or removes tests updates it.")

    def test_every_python_test_file_has_a_wrapper(self):
        # Rule 7: a Python test file is outside the gate until an MSTest method runs it. A wrapper names
        # the file in a C# string, as "eng/scripts/tests/test_x.py" or as the "test_x.py" discovery pattern.
        integration = ROOT / "tests/Nami.IntegrationTests"
        sources = "\n".join(path.read_text(encoding="utf-8") for path in sorted(integration.rglob("*.cs"))
                            if not {"bin", "obj"} & set(path.relative_to(integration).parts))
        unwrapped = [path.name for path in sorted((ROOT / "eng/scripts/tests").glob("test_*.py"))
                     if not re.search(r'["/]' + re.escape(path.name) + '"', sources)]
        self.assertEqual([], unwrapped, "Each Python test file needs an MSTest method in tests/Nami.IntegrationTests "
                                        "that runs it.")


if __name__ == "__main__":
    unittest.main()
