"""Checks that keep the gate able to fail. GateTests.cs runs this file inside the gate."""
import hashlib
import json
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SUITES = ("Unit", "Integration", "EndToEnd")
# Counted as P01 recorded them: a Node test is a line that starts with test(, the node:test function, so
# calls such as pattern.test( inside a test do not count; a Python test is a def test_ function.
NODE_TEST = re.compile(r"^test\(", re.M)
PYTHON_TEST = re.compile(r"^[ \t]*(?:async[ \t]+)?def test_", re.M)
PRODUCT_PROTOS = "src/nami.collector.ingestion/Protos"
TEST_PROTOS = "tests/Nami.Tests/Protos"
TEST_NAMESPACE = b'option csharp_namespace = "Nami.Tests.Wire.'
# A test copy may differ from upstream beyond its csharp_namespace line only as named here, with the reason. The
# test puts the upstream byte back before hashing; it skips no file. Repairing the copy instead is the owner's call.
TEST_COPY_EXCEPTIONS = {
    "opentelemetry/proto/trace/v1/trace.proto":
        (362, "upstream ends line 362 with a carriage return; the product copy kept it, the test copy lost it"),
}


def counted(folder, pattern, expression):
    return {path.relative_to(ROOT).as_posix(): len(expression.findall(path.read_text(encoding="utf-8")))
            for path in sorted((ROOT / folder).glob(pattern))}


def git(*arguments):
    """Run git without the user's identity or signing settings and return its output."""
    command = ["git", "-c", "user.name=nami", "-c", "user.email=nami@example.invalid", "-c", "commit.gpgsign=false",
               *arguments]
    return subprocess.run(command, check=True, capture_output=True, text=True).stdout.strip()


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def proto_differences(root):
    """Compare both vendored OTLP proto copies under root with their lock files; one line per difference."""
    differences = []
    product = root / PRODUCT_PROTOS
    lock = json.loads((product / "upstream.lock.json").read_text(encoding="utf-8"))
    upstream, listed = {}, set()
    for source in lock["sources"]:
        for entry in source["files"]:
            path = entry["path"]
            listed.add(path)
            upstream[source["repository"], source["revision"], path] = entry["upstreamSha256"]
            data = (product / path).read_bytes()
            if sha256(data) != entry["vendoredSha256"]:
                differences.append(f"{PRODUCT_PROTOS}/{path}: SHA-256 is not its vendoredSha256")
            # A named canonicalization appends line feeds to the upstream bytes; without them the copy is upstream.
            canonicalization = entry.get("canonicalization", {})
            if set(canonicalization) - {"justification", "appendTrailingLineFeeds"}:
                differences.append(f"{PRODUCT_PROTOS}/{path}: unknown canonicalization {sorted(canonicalization)}")
            feeds = canonicalization.get("appendTrailingLineFeeds", 0)
            if not data.endswith(b"\n" * feeds) or sha256(data[:len(data) - feeds]) != entry["upstreamSha256"]:
                differences.append(f"{PRODUCT_PROTOS}/{path}: without its canonicalization it is not upstreamSha256")
    unlisted = {p.relative_to(product).as_posix() for p in product.rglob("*.proto")} - listed
    differences += [f"{PRODUCT_PROTOS}/{path}: not in upstream.lock.json" for path in sorted(unlisted)]

    test = root / TEST_PROTOS
    pin = json.loads((test / "upstream.json").read_text(encoding="utf-8"))
    for path, expected in pin["files"].items():
        if expected != upstream.get((pin["repository"], pin["commit"], path)):
            differences.append(f"{TEST_PROTOS}/upstream.json: {path} is not the upstreamSha256 that "
                               f"upstream.lock.json records for {pin['repository']} at {pin['commit']}")
        lines = (test / path).read_bytes().split(b"\n")
        rewritten = [index for index, line in enumerate(lines) if b"Nami.Tests.Wire." in line]
        if len(rewritten) != 1 or not lines[rewritten[0]].startswith(TEST_NAMESPACE):
            differences.append(f"{TEST_PROTOS}/{path}: expected exactly one rewritten csharp_namespace line")
            continue
        # Put the line back to the upstream value, then any named exception, then hash.
        lines[rewritten[0]] = lines[rewritten[0]].replace(b"Nami.Tests.Wire.", b"", 1)
        if path in TEST_COPY_EXCEPTIONS:
            number = TEST_COPY_EXCEPTIONS[path][0]
            if lines[number - 1].endswith(b"\r"):
                differences.append(f"{TEST_PROTOS}/{path}: line {number} has its carriage return; drop the exception")
                continue
            lines[number - 1] += b"\r"
        if sha256(b"\n".join(lines)) != expected:
            differences.append(f"{TEST_PROTOS}/{path}: differs from upstream beyond its csharp_namespace line")
    unlisted = {p.relative_to(test).as_posix() for p in test.rglob("*.proto")} - set(pin["files"])
    differences += [f"{TEST_PROTOS}/{path}: not in upstream.json" for path in sorted(unlisted)]
    return differences


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

    def test_vendored_protos_match_their_lock_files(self):
        # Offline: the lock files record the upstream hashes. A difference outside TEST_COPY_EXCEPTIONS is reported,
        # never fixed by editing a proto or a lock file.
        self.assertEqual([], proto_differences(ROOT))

    def test_registry_check_uses_cached_pin_without_network(self):
        sys.dont_write_bytecode = True
        sys.path.insert(0, str(ROOT / "registry"))
        import generate
        with tempfile.TemporaryDirectory() as temporary:
            temporary = Path(temporary)
            upstream = temporary / "upstream"
            (upstream / "model").mkdir(parents=True)
            (upstream / "model/registry.yaml").write_text("groups: []\n")
            git("init", "-q", str(upstream))
            git("-C", str(upstream), "add", "model")
            git("-C", str(upstream), "commit", "-q", "-m", "pinned")
            pinned = git("-C", str(upstream), "rev-parse", "HEAD")
            # pinned_model first looks for a checkout next to the repository; none has this name, so it uses the cache.
            sibling = "nami-gate-" + temporary.name
            self.assertFalse((generate.ROOT.parent / sibling).exists())
            cache = temporary / "cache"
            git("clone", "-q", "--no-checkout", str(upstream), str(cache / sibling))
            unreachable = (temporary / "unreachable.git").as_uri()
            git("-C", str(cache / sibling), "remote", "set-url", "origin", unreachable)

            self.assertEqual(pinned, generate.pinned_model(unreachable, pinned, sibling, cache, temporary / "pinned"))
            self.assertEqual("groups: []\n", (temporary / "pinned/model/registry.yaml").read_text())

            # A pin the cache lacks is still fetched, so against the unreachable remote it fails.
            (upstream / "model/registry.yaml").write_text("groups: [moved]\n")
            git("-C", str(upstream), "commit", "-q", "-a", "-m", "moved")
            moved = git("-C", str(upstream), "rev-parse", "HEAD")
            with self.assertRaisesRegex(RuntimeError, "fetch --depth 1 origin " + moved):
                generate.pinned_model(unreachable, moved, sibling, cache, temporary / "moved")


if __name__ == "__main__":
    unittest.main()
