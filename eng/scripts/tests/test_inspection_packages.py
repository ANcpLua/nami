import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inspect_agent import Failure, validate_package


class PackageIdentityTests(unittest.TestCase):
    def test_declared_workspace_resolves_its_manifest(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            local = root / "tools" / "local"
            local.mkdir(parents=True)
            (local / "package.json").write_text(json.dumps({"name": "@test/local", "version": "0.1.0"}))
            installed = root / "node_modules" / "@test" / "local"
            installed.parent.mkdir(parents=True)
            installed.symlink_to(local, target_is_directory=True)
            self.assertEqual("0.1.0", validate_package(root, {"workspaces": ["tools/*"]}, "@test/local", "workspace:*"))

    def test_registry_copy_cannot_impersonate_a_workspace(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for location in [root / "local", root / "node_modules" / "local"]:
                location.mkdir(parents=True)
                (location / "package.json").write_text(json.dumps({"name": "local", "version": "0.1.0"}))
            with self.assertRaises(Failure):
                validate_package(root, {"workspaces": ["local"]}, "local", "workspace:*")

    def test_external_package_keeps_its_exact_pin(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            installed = root / "node_modules" / "external"
            installed.mkdir(parents=True)
            (installed / "package.json").write_text(json.dumps({"name": "external", "version": "1.2.3"}))
            self.assertEqual("1.2.3", validate_package(root, {}, "external", "1.2.3"))
            with self.assertRaises(Failure):
                validate_package(root, {}, "external", "1.2.4")


if __name__ == "__main__":
    unittest.main()
