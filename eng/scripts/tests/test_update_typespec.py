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
from update_typespec import Registry, UpdateError, REFERENCE, apply_update, collect_packages, constraints, package_update, installed_reference, workspace_updates


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

    def test_workspace_compiler_peer_tracks_exact_selection_and_preserves_other_settings(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "contracts/libraries/extension/package.json"
            path.parent.mkdir(parents=True)
            original = {"name": "local-extension", "peerDependencies": {
                "@typespec/compiler": "1.17.0", "@typespec/http": "^1.17.0"},
                "scripts": {"custom": "keep"}}
            path.write_text(json.dumps(original))
            selected = {"@typespec/compiler": package(version="1.18.0-dev.13")}
            updated = workspace_updates(root, {"workspaces": ["libraries/*"]}, selected)
            self.assertEqual({**original, "peerDependencies": {
                "@typespec/compiler": "1.18.0-dev.13", "@typespec/http": "^1.17.0"}}, updated[path])
            self.assertEqual(original, json.loads(path.read_text()))
            self.assertEqual([{"source": "local-extension", "dependency": "@typespec/compiler",
                               "version": "1.18.0-dev.13", "range": "1.18.0-dev.13"}],
                             constraints(selected, updated))

    def test_incompatible_local_workspace_peer_is_reported_before_apply(self):
        from update_typespec import verify_constraints
        selected = {"@typespec/http": package("@typespec/http", "1.18.0-dev.6")}
        workspaces = {Path("extension/package.json"): {"name": "local-extension", "peerDependencies": {
            "@typespec/http": "1.17.0"}}}
        failure = [{"source": "local-extension", "dependency": "@typespec/http",
                    "version": "1.18.0-dev.6", "range": "1.17.0"}]
        with patch("update_typespec.subprocess.run", return_value=subprocess.CompletedProcess([], 0, json.dumps(failure))) as run:
            with self.assertRaisesRegex(UpdateError, r"local-extension requires @typespec/http 1.17.0, selected 1.18.0-dev.6"):
                verify_constraints(selected, "bun", workspaces)
        self.assertEqual(failure, json.loads(run.call_args.kwargs["input"]))

    def test_check_reports_stale_workspace_peer_when_root_and_reference_are_current(self):
        import update_typespec
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            workspace = root / "contracts/extension/package.json"
            workspace.parent.mkdir(parents=True)
            compiler = package(version="1.18.0-dev.13")
            manifest = {"workspaces": ["extension"], "devDependencies": {"@typespec/compiler": compiler["version"]}}
            (root / "contracts/package.json").write_text(json.dumps(manifest))
            (root / update_typespec.POLICY).write_text(json.dumps({"registry": "https://registry.example", "tag": "next"}))
            original = '{"name":"extension","peerDependencies":{"@typespec/compiler":"1.17.0"}}\n'
            workspace.write_text(original)
            reference = root / REFERENCE
            reference.parent.mkdir(parents=True)
            reference.write_text('{"packages":[]}')
            with patch.object(update_typespec, "ROOT", root), patch.object(sys, "argv", ["update_typespec.py", "--check"]), \
                 patch("update_typespec.collect_packages", return_value=({"@typespec/compiler": compiler}, {"@typespec/compiler": "compiler"})), \
                 patch("update_typespec.resolve", side_effect=["bun", "node"]), patch("update_typespec.verify_constraints") as verify, \
                 patch("update_typespec.installed_reference", return_value={"packages": []}), patch("sys.stdout", new_callable=io.StringIO) as output:
                self.assertEqual(1, update_typespec.main())
            self.assertIn("compiler peer 1.17.0 -> 1.18.0-dev.13", output.getvalue())
            self.assertEqual("1.18.0-dev.13", verify.call_args.args[2][workspace]["peerDependencies"]["@typespec/compiler"])
            self.assertEqual(original, workspace.read_text())


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

    def test_failed_contract_validation_restores_exact_workspace_compiler_peer(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "contracts/package.json"
            workspace = root / "contracts/extension/package.json"
            lock = root / "contracts/bun.lock"
            reference = root / REFERENCE
            workspace.parent.mkdir(parents=True)
            reference.parent.mkdir(parents=True)
            originals = {manifest: b'{"workspaces":["extension"]}\n',
                         workspace: b'{ "name": "extension", "peerDependencies": { "@typespec/compiler": "1.17.0" } }\n',
                         lock: b"original lock\n", reference: b"original reference\n"}
            for path, content in originals.items():
                path.write_bytes(content)
            calls = []

            def run(command, **kwargs):
                calls.append(command)
                if command == ["bun", "install"]:
                    self.assertEqual("1.18.0-dev.13", json.loads(workspace.read_text())["peerDependencies"]["@typespec/compiler"])
                    lock.write_text("changed lock")
                if command[0] == "bash":
                    raise subprocess.CalledProcessError(1, command)
                if command == ["bun", "install", "--frozen-lockfile"]:
                    self.assertEqual(originals, {path: path.read_bytes() for path in originals})

            selected = {"@typespec/compiler": package(version="1.18.0-dev.13")}
            with patch("update_typespec.installed_reference", return_value={"packages": []}), patch("update_typespec.validate_imports"):
                with self.assertRaises(subprocess.CalledProcessError):
                    apply_update(root, {"workspaces": ["extension"]}, selected, {}, "next", "bun", "node", run)
            self.assertEqual(originals, {path: path.read_bytes() for path in originals})
            self.assertEqual(["bun", "install", "--frozen-lockfile"], calls[-1])

    def test_success_updates_workspace_compiler_peer_before_install(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "contracts/package.json"
            workspace = root / "contracts/extension/package.json"
            workspace.parent.mkdir(parents=True)
            manifest.write_text('{"workspaces":["extension"]}\n')
            workspace.write_text('{"peerDependencies":{"@typespec/compiler":"1.17.0"}}\n')
            calls = []

            def run(command, **kwargs):
                calls.append(command)
                if command == ["bun", "install"]:
                    self.assertEqual("1.18.0-dev.13", json.loads(workspace.read_text())["peerDependencies"]["@typespec/compiler"])

            selected = {"@typespec/compiler": package(version="1.18.0-dev.13")}
            with patch("update_typespec.installed_reference", return_value={"packages": []}), patch("update_typespec.validate_imports"):
                apply_update(root, {"workspaces": ["extension"]}, selected, {}, "next", "bun", "node", run)
            self.assertEqual(["bun", "install"], calls[0])
            self.assertEqual(["bash", str(root / "eng/contracts.sh"), "verify"], calls[1])
            self.assertEqual({"packages": []}, json.loads((root / REFERENCE).read_text()))

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
