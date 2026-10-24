"""Repository orchestration only: all test execution belongs to MSTest on native MTP."""

import argparse
import glob
import json
import os
import queue
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import threading
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path


class HarnessError(Exception):
    pass


def finish(summary, code):
    summary["exitCode"] = code
    summary["status"] = "passed" if code == 0 else "failed"
    return code


def test_status(directory):
    started, ended = Counter(), Counter()
    for path in directory.rglob("*.diag"):
        for line in path.read_text(errors="replace").splitlines():
            match = re.search(r"\b(Executing|Executed) test (.+)$", line)
            if match:
                (started if match[1] == "Executing" else ended)[match[2]] += 1
    pending = started - ended
    return f"completed={sum(ended.values())} pending={','.join(sorted(pending)) or 'none reported'}"


def execute(command, log, root, progress=None):
    print("$ " + shlex.join(map(str, command)), flush=True)
    with log.open("w", encoding="utf-8") as output:
        output.write("$ " + shlex.join(map(str, command)) + "\n")
        with subprocess.Popen(command, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                              text=True, encoding="utf-8", errors="replace") as process:
            lines = queue.Queue()
            def read_output():
                try:
                    for line in process.stdout:
                        lines.put(line)
                finally:
                    lines.put(None)
            reader = threading.Thread(target=read_output, daemon=True)
            reader.start()
            while True:
                try:
                    line = lines.get(timeout=30)
                except queue.Empty:
                    if progress is not None:
                        print(progress(), flush=True)
                    continue
                if line is None:
                    break
                output.write(line)
                output.flush()
                print(line, end="", flush=True)
            reader.join()
            code = process.wait()
    return code if code >= 0 else 128 - code


def project_state(dotnet, project, configuration, directory, root):
    command = [dotnet, "msbuild", str(project), "-nologo", f"-p:Configuration={configuration}",
               "-getProperty:IsTestingPlatformApplication,TargetPath,ProjectAssetsFile,MSBuildProjectExtensionsPath",
               f"-bl:{directory}/evaluation-{{}}.binlog"]
    result = subprocess.run(command, cwd=root, text=True, capture_output=True)
    (directory / "evaluation.log").write_text(result.stdout + result.stderr)
    if result.returncode:
        raise HarnessError(f"Project evaluation failed ({result.returncode}); see {directory / 'evaluation.log'}")
    try:
        state = json.loads(result.stdout[result.stdout.index("{"):])["Properties"]
    except (ValueError, KeyError) as error:
        raise HarnessError(f"Invalid project evaluation for {project}: {error}") from error
    (directory / "project-state.json").write_text(json.dumps(state, indent=2))
    if state.get("IsTestingPlatformApplication", "").lower() != "true":
        raise HarnessError(f"{project}: IsTestingPlatformApplication is not true. Restore the project; its NuGet MSBuild imports are unavailable.")
    def resolved(value):
        path = Path(value)
        return path if path.is_absolute() else project.parent / path

    assets = resolved(state["ProjectAssetsFile"])
    extensions = resolved(state["MSBuildProjectExtensionsPath"])
    required = [assets, extensions / (project.name + ".nuget.g.props"), extensions / (project.name + ".nuget.g.targets"), resolved(state["TargetPath"])]
    missing = [str(path) for path in required if not path.is_file()]
    if missing:
        raise HarnessError("Missing restore/build state: " + ", ".join(missing) + ". Run without --no-build/--no-restore, or use --test-modules for already-built modules.")
    packages = json.loads(assets.read_text())
    folders = [Path(path) for path in packages.get("packageFolders", {})]
    for name, library in packages.get("libraries", {}).items():
        if library.get("type") != "package":
            continue
        package = next((folder / library["path"] for folder in folders if (folder / library["path"]).is_dir()), None)
        if package is None:
            raise HarnessError(f"Global package {name} is missing. Preserve the original global packages folder or restore in this stage.")
        missing_file = next((file for file in library.get("files", []) if not (package / file).is_file()), None)
        if missing_file:
            raise HarnessError(f"Global package {name} is incomplete: {package / missing_file}. Restore in this stage.")
    return state


def read_results(directory):
    files = sorted(directory.glob("*.trx"))
    if not files:
        raise HarnessError(f"No fresh TRX report was produced in {directory}")
    counts = {"total": 0, "passed": 0, "failed": 0, "skipped": 0}
    names = []
    executions = set()
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    for file in files:
        document = ET.parse(file).getroot()
        summary = document.find("t:ResultSummary/t:Counters", namespace)
        if summary is None:
            raise HarnessError(f"TRX counters are missing: {file}")
        results = document.findall(".//t:UnitTestResult", namespace)
        if len(results) != int(summary.get("total", "0")):
            raise HarnessError(f"TRX results and counters disagree: {file}")
        for result in results:
            identity = result.get("executionId")
            if identity in executions:
                raise HarnessError(f"Duplicate test execution in reports: {identity}")
            executions.add(identity)
            outcome = result.get("outcome")
            counts["total"] += 1
            counts["passed" if outcome == "Passed" else "skipped" if outcome == "NotExecuted" else "failed"] += 1
            names.append(result.get("testName"))
    return counts, names


def main():
    parser = argparse.ArgumentParser(prog="eng/test.sh", description="Run Nami's MSTest suites on native Microsoft.Testing.Platform.")
    parser.add_argument("--root", type=Path, required=True, help=argparse.SUPPRESS)
    parser.add_argument("--dotnet", required=True, help=argparse.SUPPRESS)
    parser.add_argument("--suite", choices=("All", "Unit", "Integration", "EndToEnd"), default="All")
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--no-restore", action="store_true")
    parser.add_argument("--filter")
    parser.add_argument("--test-modules", help="Use built MSTest modules without evaluating projects; select a matching --suite.")
    parser.add_argument("--status", type=Path, help="Show running/completed cases from a previous or active run's MTP diagnostic logs.")
    args = parser.parse_args()
    root = args.root.resolve()
    workers = os.environ.get("NAMI_BUILD_WORKERS", "2")
    if args.status is not None:
        directory = args.status if args.status.is_absolute() else root / args.status
        if not directory.is_dir():
            print(f"TEST STATUS FAILED: run directory does not exist: {directory}", file=sys.stderr)
            return 10
        print(f"TEST STATUS: {test_status(directory)}\nRun: {directory}")
        return 0
    if os.environ.get("TESTINGPLATFORM_EXITCODE_IGNORE", "").strip():
        print("TEST SETUP FAILED: TESTINGPLATFORM_EXITCODE_IGNORE would hide MTP failures; remove it for repository verification.", file=sys.stderr)
        return 10
    manifest = json.loads((root / "eng/test-suites.json").read_text())
    order = ("Unit", "Integration", "EndToEnd")
    if set(manifest) != set(order):
        raise SystemExit("The suite manifest must define Unit, Integration and EndToEnd exactly once.")
    selected = list(order) if args.suite == "All" else [args.suite]
    base = root / "artifacts/test-results"
    base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run.", dir=base))
    summary = {"configuration": args.configuration, "requested": selected, "filtered": bool(args.filter), "suites": []}
    print(f"TEST RUN: suites={','.join(selected)} configuration={args.configuration}\nResults: {run}", flush=True)
    try:
        if not args.no_build and not args.test_modules:
            target = root / ("nami.slnx" if args.suite == "All" else manifest[args.suite]["project"])
            command = [args.dotnet, "build", str(target), "-c", args.configuration, f"-maxcpucount:{workers}", "-nodeReuse:false", "-p:UseSharedCompilation=false", f"-bl:{run}/build-{{}}.binlog"]
            if args.no_restore:
                command.append("--no-restore")
            code = execute(command, run / "build.log", root)
            if code:
                return finish(summary, code)
            if "EndToEnd" in selected:
                mcp = root / "src/nami.mcp"
                for command in (["bun", "install", "--frozen-lockfile"], ["bun", "run", "build"]):
                    code = execute(command, run / "mcp-build.log", mcp)
                    if code:
                        return finish(summary, code)
                command = [args.dotnet, "publish", str(root / "src/nami/nami.csproj"), "-c", args.configuration,
                           "-o", str(root / "artifacts/publish"), f"-maxcpucount:{workers}", "-nodeReuse:false", "-p:UseSharedCompilation=false", f"-bl:{run}/publish-{{}}.binlog"]
                if args.no_restore:
                    command.append("--no-restore")
                code = execute(command, run / "publish.log", root)
                if code:
                    return finish(summary, code)
                mcp_output = root / "artifacts/publish/mcp"
                mcp_output.mkdir(parents=True, exist_ok=True)
                shutil.copy2(mcp / "dist/main.js", mcp_output / "main.js")
        module_files = []
        if args.test_modules:
            expression = args.test_modules if Path(args.test_modules).is_absolute() else str(root / args.test_modules)
            module_files = [Path(path).resolve() for path in glob.glob(expression, recursive=True) if Path(path).is_file()]
            if not module_files:
                raise HarnessError(f"No built modules match {args.test_modules}")
            expected_names = {Path(manifest[name]["project"]).stem + ".dll" for name in selected}
            unexpected = [str(path) for path in module_files if path.name not in expected_names]
            if unexpected:
                raise HarnessError("The module expression includes unselected or non-test assemblies: " + ", ".join(unexpected))
        for name in selected:
            settings = manifest[name]
            directory = run / name
            directory.mkdir()
            project = root / settings["project"]
            minimum = 1 if args.filter else settings["minimumExpectedTests"]
            if args.test_modules:
                matches = [path for path in module_files if path.name == project.stem + ".dll"]
                if len(matches) != 1:
                    raise HarnessError(f"Expected exactly one built {project.stem}.dll; found {len(matches)}")
                command = [args.dotnet, "test", "--root-directory", str(matches[0].parent),
                           "--test-modules", matches[0].name]
                config = matches[0].parent / "testconfig.json"
            else:
                state = project_state(args.dotnet, project, args.configuration, directory, root)
                print(f"MTP PROJECT: {project} IsTestingPlatformApplication={state['IsTestingPlatformApplication']}", flush=True)
                command = [args.dotnet, "test", "--project", str(project), "-c", args.configuration, "--no-build", "--no-restore"]
                config = project.parent / "testconfig.json"
            if name == "EndToEnd" and not (root / "artifacts/publish/mcp/main.js").is_file():
                raise HarnessError("The bundled TypeScript MCP host is missing. Prepare PublishNative first.")
            if name == "EndToEnd" and not (root / "artifacts/publish" / ("nami.exe" if os.name == "nt" else "nami")).is_file():
                raise HarnessError("The NativeAOT Nami host is missing. Run without --no-build, or prepare PublishNative first.")
            if not config.is_file():
                raise HarnessError(f"MSTest configuration is missing: {config}")
            display = json.loads(config.read_text())["commandLineOptions"]
            command += ["--max-parallel-test-modules", "1", "--minimum-expected-tests", str(minimum),
                        "--output", settings["verbosity"], "--show-test-results", display["show-test-results"],
                        "--no-ansi", "--no-progress",
                        "--timeout", settings["timeout"], "--report-trx", "--results-directory", str(directory),
                        "--config-file", str(config), "--diagnostic", "--diagnostic-verbosity", "Information",
                        "--diagnostic-output-directory", str(directory / "diagnostics")]
            if args.filter:
                command += ["--filter", args.filter]
            print(f"TEST SUITE: {name} minimum={minimum}\nLog: {directory / 'test.log'}", flush=True)
            code = execute(command, directory / "test.log", root,
                           progress=lambda: f"TEST WAIT {name}: {test_status(directory)}; diagnostics: {directory / 'diagnostics'}")
            try:
                counts, names = read_results(directory)
            except (HarnessError, ET.ParseError, ValueError) as error:
                print(f"TEST REPORT FAILED: {error}", file=sys.stderr)
                return finish(summary, code or 10)
            summary["suites"].append({"suite": name, "minimum": minimum, "exitCode": code, **counts, "tests": names})
            print(f"TEST RESULT {name}: Passed: {counts['passed']}, Failed: {counts['failed']}, Skipped: {counts['skipped']}, Total: {counts['total']}, Exit: {code}", flush=True)
            if code:
                return finish(summary, code)
            if counts["passed"] + counts["failed"] < minimum:
                print(f"TEST MINIMUM FAILED: {name} executed fewer than {minimum} tests", file=sys.stderr)
                return finish(summary, 9)
            if counts["failed"] or counts["skipped"]:
                return finish(summary, 2)
        print("TEST RUN PASSED: every requested suite executed and its fresh reports were verified.", flush=True)
        return finish(summary, 0)
    except (HarnessError, OSError, ValueError, KeyError) as error:
        print(f"TEST SETUP FAILED: {error}", file=sys.stderr)
        return finish(summary, 10)
    finally:
        (run / "summary.json").write_text(json.dumps(summary, indent=2))
        print(f"Summary: {run / 'summary.json'}", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
