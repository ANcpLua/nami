import io
import json
import subprocess
import sys
import tempfile
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from update_typespec import Registry, UpdateError, REFERENCE, apply_update, collect_packages, constraints, package_update, installed_reference


def package(name="@typespec/compiler", version="1.17.0", **extra):
    return {"name": name, "version": version, **extra}


class Response(io.BytesIO):
    def __init__(self, data, headers=None):
        super().__init__(json.dumps(data).encode())
        self.headers = headers or {}


class RegistryMetadataTests(unittest.TestCase):
    def test_not_modified_reuses_valid_metadata_and_sends_etag(self):
        calls = []

        def open_request(request, timeout):
            calls.append((request, timeout))
            if len(calls) == 1:
                return Response(package(), {"ETag": '"revision-one"'})
            raise urllib.error.HTTPError(request.full_url, 304, "Not modified", {}, None)

        with tempfile.TemporaryDirectory() as directory:
            registry = Registry("https://registry.example", directory, opener=open_request)
            self.assertEqual(package(), registry.fetch("@typespec/compiler", "latest"))
            self.assertEqual(package(), registry.fetch("@typespec/compiler", "latest"))
        self.assertEqual(2, len(calls))
        self.assertEqual('"revision-one"', calls[1][0].get_header("If-none-match"))
        self.assertEqual("https://registry.example/@typespec%2Fcompiler/latest", calls[0][0].full_url)
        self.assertEqual(20, calls[0][1])

    def test_offline_uses_cache_without_opening_network(self):
        with tempfile.TemporaryDirectory() as directory:
            Registry("https://registry.example", directory, opener=lambda *a, **k: Response(package())).fetch("@typespec/compiler", "latest")
            registry = Registry("https://registry.example", directory, offline=True,
                                opener=lambda *a, **k: self.fail("Offline mode opened the network"))
            self.assertEqual(package(), registry.fetch("@typespec/compiler", "latest"))

    def test_offline_without_cache_explains_missing_package(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(UpdateError, "No cached metadata for @typespec/compiler@latest"):
                Registry("https://registry.example", directory, offline=True).fetch("@typespec/compiler", "latest")

    def test_invalid_identity_or_deprecation_is_rejected_without_poisoning_cache(self):
        cases = [package(name="@other/compiler"), package(version="latest"), package(version=117),
                 package(deprecated="withdrawn"), []]
        for value in cases:
            with self.subTest(value=value), tempfile.TemporaryDirectory() as directory:
                registry = Registry("https://registry.example", directory, opener=lambda *a, **k: Response(value))
                with self.assertRaises(UpdateError):
                    registry.fetch("@typespec/compiler", "latest")
                self.assertEqual([], list(Path(directory).glob("*.json")))

    def test_http_error_does_not_silently_fall_back_to_stale_cache(self):
        with tempfile.TemporaryDirectory() as directory:
            Registry("https://registry.example", directory, opener=lambda *a, **k: Response(package())).fetch("@typespec/compiler", "latest")

            def unavailable(request, timeout):
                raise urllib.error.HTTPError(request.full_url, 503, "Unavailable", {}, None)

            with self.assertRaisesRegex(UpdateError, "HTTP 503"):
                Registry("https://registry.example", directory, opener=unavailable).fetch("@typespec/compiler", "latest")


class PackagePlanTests(unittest.TestCase):
    def test_collects_required_typespec_peers_but_not_optional_unselected_peers(self):
        available = {
            "@typespec/graphql": package("@typespec/graphql", "0.4.0", peerDependencies={
                "@typespec/compiler": "^1.17.0", "@typespec/optional": "*"},
                peerDependenciesMeta={"@typespec/optional": {"optional": True}}),
            "@typespec/compiler": package(),
        }

        class FakeRegistry:
            def fetch(self, name, tag):
                return available[name]

        packages, roles = collect_packages(FakeRegistry(), {"packages": {"@typespec/graphql": "preview emitter"}}, "latest")
        self.assertEqual(["@typespec/compiler", "@typespec/graphql"], list(packages))
        self.assertEqual("required TypeSpec peer", roles["@typespec/compiler"])
        self.assertEqual([{"source": "@typespec/graphql", "dependency": "@typespec/compiler",
                           "version": "1.17.0", "range": "^1.17.0"}], constraints(packages))

    def test_update_uses_exact_versions_and_preserves_unmanaged_user_settings(self):
        current = {"name": "nami", "scripts": {"custom": "keep"}, "devDependencies": {
            "@typespec/compiler": "1.16.0", "typescript": "7.1.0-beta", "local": "workspace:*"}}
        updated = package_update(current, {"@typespec/compiler": package(), "@typespec/sse": package("@typespec/sse", "0.87.0")})
        self.assertEqual({"@typespec/compiler": "1.17.0", "@typespec/sse": "0.87.0",
                          "typescript": "7.1.0-beta", "local": "workspace:*"}, updated["devDependencies"])
        self.assertEqual({"custom": "keep"}, updated["scripts"])
        self.assertEqual("1.16.0", current["devDependencies"]["@typespec/compiler"])

    def test_incompatible_peer_is_reported_before_apply(self):
        from update_typespec import verify_constraints
        packages = {"@typespec/compiler": package(), "@typespec/graphql": package("@typespec/graphql", "0.4.0", peerDependencies={"@typespec/compiler": "~1.18.0"})}
        failure = [{"source": "@typespec/graphql", "dependency": "@typespec/compiler", "version": "1.17.0", "range": "~1.18.0"}]
        with patch("update_typespec.subprocess.run", return_value=subprocess.CompletedProcess([], 0, json.dumps(failure))):
            with self.assertRaisesRegex(UpdateError, r"graphql requires @typespec/compiler ~1.18.0, selected 1.17.0"):
                verify_constraints(packages, "bun")


class UpdateTransactionTests(unittest.TestCase):
    def test_keyboard_interrupt_restores_the_original_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "contracts/package.json"
            manifest.parent.mkdir(parents=True)
            manifest.write_text('{"name":"original"}\n')
            calls = []

            def run(command, **kwargs):
                calls.append(command)
                if len(calls) == 1:
                    raise KeyboardInterrupt()

            with self.assertRaises(KeyboardInterrupt):
                apply_update(root, {"name": "changed"}, {}, {}, "latest", "bun", "node", run)
            self.assertEqual('{"name":"original"}\n', manifest.read_text())
            self.assertEqual(["bun", "install", "--frozen-lockfile"], calls[-1])

    def test_failed_contract_validation_restores_exact_package_lock_and_reference(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "contracts/package.json"
            lock = root / "contracts/bun.lock"
            reference = root / REFERENCE
            manifest.parent.mkdir(parents=True)
            reference.parent.mkdir(parents=True)
            originals = {manifest: b'{"custom":"unchanged"}\n', lock: b"original lock\n", reference: b"original reference\n"}
            for path, content in originals.items():
                path.write_bytes(content)
            calls = []

            def run(command, **kwargs):
                calls.append(command)
                if command == ["bun", "install"]:
                    lock.write_text("changed lock")
                if command[0] == "bash":
                    raise subprocess.CalledProcessError(1, command)

            with patch("update_typespec.installed_reference", return_value={"packages": []}), patch("update_typespec.validate_imports"):
                with self.assertRaises(subprocess.CalledProcessError):
                    apply_update(root, {"devDependencies": {}}, {}, {}, "latest", "bun", "node", run)
            self.assertEqual(originals, {path: path.read_bytes() for path in originals})
            self.assertEqual(["bun", "install", "--frozen-lockfile"], calls[-1])

    def test_failed_initial_install_removes_previously_absent_generated_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "contracts/package.json"
            manifest.parent.mkdir(parents=True)
            manifest.write_text('{"name":"existing"}\n')
            calls = []

            def run(command, **kwargs):
                calls.append(command)
                if len(calls) == 1:
                    (root / "contracts/bun.lock").write_text("partial lock")
                    raise subprocess.CalledProcessError(1, command)

            with self.assertRaises(subprocess.CalledProcessError):
                apply_update(root, {}, {}, {}, "latest", "bun", "node", run)
            self.assertEqual('{"name":"existing"}\n', manifest.read_text())
            self.assertFalse((root / "contracts/bun.lock").exists())
            self.assertFalse((root / REFERENCE).exists())

    def test_success_writes_reference_only_after_contract_validation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "contracts").mkdir()
            (root / "contracts/package.json").write_text("{}")
            expected = {"packages": [{"package": "@typespec/compiler", "version": "1.17.0", "entrypoints": {}}]}

            def run(command, **kwargs):
                self.assertFalse((root / REFERENCE).exists())

            with patch("update_typespec.installed_reference", return_value=expected), patch("update_typespec.validate_imports"):
                apply_update(root, {"name": "updated"}, {}, {}, "latest", "bun", "node", run)
            self.assertEqual(expected, json.loads((root / REFERENCE).read_text()))
            self.assertEqual({"name": "updated"}, json.loads((root / "contracts/package.json").read_text()))


class InstalledReferenceTests(unittest.TestCase):
    def test_missing_secondary_export_is_visible_while_required_main_is_valid(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            installed = root / "contracts/node_modules/@typespec/compiler"
            installed.mkdir(parents=True)
            (installed / "main.tsp").write_text("model Example {}")
            (installed / "package.json").write_text(json.dumps(package(tspMain="main.tsp", exports={
                "./secondary": {"types": "missing.d.ts"}})))
            value = installed_reference(root, {"@typespec/compiler": package()}, {"@typespec/compiler": "compiler"}, "latest")
            entries = value["packages"][0]["entrypoints"]
            self.assertTrue(entries["tspMain"]["exists"])
            self.assertEqual({"path": "contracts/node_modules/@typespec/compiler/missing.d.ts", "exists": False, "sha256": None}, entries["./secondary:types"])

    def test_missing_required_typespec_entry_fails_validation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            installed = root / "contracts/node_modules/@typespec/compiler"
            installed.mkdir(parents=True)
            (installed / "package.json").write_text(json.dumps(package(tspMain="missing.tsp")))
            with self.assertRaisesRegex(UpdateError, "Missing declared entry point"):
                installed_reference(root, {"@typespec/compiler": package()}, {"@typespec/compiler": "compiler"}, "latest")


if __name__ == "__main__":
    unittest.main()
