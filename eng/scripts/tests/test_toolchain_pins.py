"""The toolchain pins agree with each other. ToolchainPinTests.cs runs this file inside the gate."""
import json
import re
import tomllib
import unittest
import xml.etree.ElementTree as ElementTree
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TYPESCRIPT = "7.1.0-beta"
TYPESCRIPT_PINS = ("src/nami.mcp/package.json", "contracts/package.json")


def read(relative):
    return (ROOT / relative).read_text(encoding="utf-8")


def version_property(name):
    declarations = ElementTree.fromstring(read("Version.props")).findall(f".//{name}")
    if len(declarations) != 1:
        raise AssertionError(f"Version.props declares {name} {len(declarations)} times, expected once")
    return declarations[0].text.strip()


def image_tag(image):
    tags = re.findall(rf"^FROM\s+mcr\.microsoft\.com/dotnet/{image}:([^\s@]+)", read("src/nami/Dockerfile"), re.M)
    if len(tags) != 1:
        raise AssertionError(f"src/nami/Dockerfile names dotnet/{image} {len(tags)} times, expected once")
    return tags[0]


def unpinned_base_images():
    # Every image a stage starts from carries the digest of its index; a stage that continues another one has none,
    # and neither has "scratch": that is no image but the empty stage (the Dockerfile's stage "symbols").
    dockerfile = read("src/nami/Dockerfile")
    stages = set(re.findall(r"^FROM\s+\S+\s+AS\s+(\S+)", dockerfile, re.M | re.I)) | {"scratch"}
    return [image for image in re.findall(r"^FROM\s+(\S+)", dockerfile, re.M)
            if image not in stages and not re.search(r"@sha256:[0-9a-f]{64}$", image)]


def runtime_of(sdk):
    # A 1xx SDK band ships the runtime whose patch is the SDK's last two digits: 11.0.100 has 11.0.0, 11.0.105 has
    # 11.0.5. Other bands have no such rule, so the test must learn them before a pin moves there.
    match = re.fullmatch(r"(\d+)\.(\d+)\.1(\d\d)", sdk)
    if not match:
        raise AssertionError(f"SDK {sdk} is not a release of the 1xx band; teach runtime_of its runtime")
    major, minor, patch = match.groups()
    return f"{major}.{minor}.{int(patch)}"


class ToolchainPinTests(unittest.TestCase):
    def test_toolchain_pins_agree(self):
        global_json = json.loads(read("global.json"))
        sdk = global_json["sdk"]["version"]
        # The six pins of the .NET release train, read by name.
        pins = {
            "global.json sdk.version": sdk,
            "mise.toml dotnet": tomllib.loads(read("mise.toml"))["tools"]["dotnet"],
            "Version.props DotNetRuntimeVersion": version_property("DotNetRuntimeVersion"),
            "Version.props MicrosoftDiaSymReaderVersion": version_property("MicrosoftDiaSymReaderVersion"),
            "src/nami/Dockerfile dotnet/sdk": image_tag("sdk"),
            "src/nami/Dockerfile dotnet/runtime-deps": image_tag("runtime-deps"),
        }
        problems = [f"{name} is {value}, a pre-release" for name, value in pins.items()
                    if "-rc" in value or "-preview" in value]
        if not problems:
            runtime = runtime_of(sdk)
            expected = {
                "mise.toml dotnet": sdk,
                "src/nami/Dockerfile dotnet/sdk": sdk,
                "Version.props DotNetRuntimeVersion": runtime,
                # The runtime image is the chiseled Ubuntu 26.04 one of that runtime: no shell, user app.
                "src/nami/Dockerfile dotnet/runtime-deps": f"{runtime}-resolute-chiseled",
            }
            problems += [f"{name} is {pins[name]}, expected {value} for SDK {sdk}"
                         for name, value in expected.items() if pins[name] != value]
        problems += [f"src/nami/Dockerfile: {image} is not pinned by digest" for image in unpinned_base_images()]
        if global_json["sdk"].get("rollForward") != "disable":
            problems.append(f"global.json sdk.rollForward is {global_json['sdk'].get('rollForward')}, expected disable")
        runner = global_json.get("test", {}).get("runner")
        if runner != "Microsoft.Testing.Platform":
            problems.append(f"global.json test.runner is {runner}, expected Microsoft.Testing.Platform")
        for path in TYPESCRIPT_PINS:
            actual = json.loads(read(path)).get("devDependencies", {}).get("typescript")
            if actual != TYPESCRIPT:
                problems.append(f"{path}: typescript is {actual}, expected {TYPESCRIPT}")
        self.assertEqual([], problems)


if __name__ == "__main__":
    unittest.main()
