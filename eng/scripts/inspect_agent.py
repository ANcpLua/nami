"""Pinned TypeScript/contract checks and isolated MCP client evidence."""

import argparse
import hashlib
import json
import os
import re
import shlex
import shutil
import signal
import socket
import subprocess
import tempfile
import time
import urllib.request
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

from inspect_code import ROOT, digest, sources, write_json

BASE = ROOT / "artifacts/inspection/agent"
RANK = {"error": 0, "warning": 1, "note": 2}
GENERATED = ["internal/nami.contracts/Nami", "contracts/json-schema", "contracts/openapi",
             "src/nami.mcp/src/contracts/generated"]


class Failure(Exception):
    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


def fingerprint():
    # Include ignored compiler inputs as well as Git's visible files. Dependency trees and
    # generated build directories have separate tool/output fingerprints.
    paths = []
    for root in [ROOT / "contracts", ROOT / "src/nami.mcp", ROOT / "eng/scripts"]:
        for directory, children, files in os.walk(root):
            children[:] = [name for name in children if name not in {"node_modules", "dist", "bin", "obj", "__pycache__"}]
            paths.extend(Path(directory, name) for name in files
                         if Path(name).suffix in {".ts", ".mts", ".cts", ".js", ".mjs", ".cjs", ".json", ".yaml"})
    return hashlib.sha256((sources() + digest(paths)).encode()).hexdigest()


def tree_hash(roots):
    return digest(path for root in roots for path in (ROOT / root).rglob("*") if path.is_file() and path.name != ".DS_Store")


def generated_manifest():
    return {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
            for root in GENERATED for path in sorted((ROOT / root).rglob("*"))
            if path.is_file() and path.name != ".DS_Store"}


def output_hash(target):
    roots = [*GENERATED, "contracts/emitters/csharp/dist", "contracts/emitters/ts-types/dist",
             "contracts/emitters/nami-lint/dist", "src/nami.mcp/dist"]
    if target == "mcp":
        roots.append("artifacts/publish")
    return tree_hash(roots)


def tool(command, arguments):
    return subprocess.check_output([str(command), *arguments], cwd=ROOT, text=True, timeout=15).strip()


def runtimes(pin):
    result = {}
    for name in ["bun", "node"]:
        executable = shutil.which(name)
        if not executable:
            raise Failure("SETUP_FAILED", f"Install pinned {name} {pin[name]}.")
        actual = tool(executable, ["--version"]).removeprefix("v")
        if actual != pin[name]:
            raise Failure("SETUP_FAILED", f"{name}: expected {pin[name]}, found {actual}.")
        executable = Path(tool(executable, ["-p", "process.execPath"])).resolve()
        result[name] = {"version": actual, "path": str(executable), "sha256": hashlib.sha256(executable.read_bytes()).hexdigest()}
    return result


def mcp_tool(pin, node):
    path = Path(os.environ.get("MCPDO_PATH") or shutil.which("mcpdo") or "")
    if path.suffix == ".cmd":
        path = path.parent / "node_modules" / pin["package"] / pin["entry"]
    path = path.resolve()
    if not path.is_file() or path.suffix not in {".js", ".mjs", ".cjs"}:
        raise Failure("SETUP_FAILED", f"Install {pin['package']}@{pin['version']} (mcpdo), or set MCPDO_PATH to its JavaScript entry point.")
    actual = tool(node, [str(path), "--version"])
    if actual != pin["version"]:
        raise Failure("SETUP_FAILED", f"mcpdo: expected {pin['version']}, found {actual}.")
    package = next((parent / "package.json" for parent in path.parents
                    if (parent / "package.json").is_file()
                    and json.loads((parent / "package.json").read_text()).get("name") == pin["package"]), None)
    if package is None or json.loads(package.read_text())["version"] != pin["version"]:
        raise Failure("SETUP_FAILED", "mcpdo entry does not belong to the pinned inspector package.")
    return {"version": actual, "path": str(path), "sha256": tree_hash_absolute(path.parent)}


def tree_hash_absolute(root):
    digest_value = hashlib.sha256()
    for path in sorted(root.glob("*.js")):
        digest_value.update(path.name.encode() + path.read_bytes())
    return digest_value.hexdigest()


def render(summary, findings, args, directory):
    context = summary["context"]
    print(f"INSPECT {summary['status']}: target={context['target']} configuration={context['configuration']} exit={summary['exitCode']} source={context['source'][:12]}")
    for name, info in context.get("tools", {}).items():
        print(f"Tool: {name} {info['version']} ({info['path']})")
    print(f"Build: {summary.get('build', 'not run')}; checks={len(summary.get('checks', []))}")
    for warning in summary.get("buildDiagnostics", [])[:args.limit]:
        print(f"BUILD: {warning}")
    counts = summary.get("counts", {})
    print(f"Findings: {sum(counts.values())}; errors={counts.get('error', 0)} warnings={counts.get('warning', 0)} notes={counts.get('note', 0)}")
    selected = [item for item in findings if (not args.rule or item["rule"] == args.rule)
                and (not args.level or item["level"] == args.level)]
    for item in selected[args.offset:args.offset + args.limit]:
        print(f"{item['level'].upper()} [{item['lane']}] {item['rule']} {item['path']}:{item['line']}:{item['column']}: {item['message'][:500]}")
    for problem in summary.get("problems", [])[:args.limit]:
        print(f"PROBLEM: {' '.join(problem.split())[:700]}")
    relative = directory.relative_to(ROOT)
    print(f"Evidence: {relative}/summary.json")
    print(f"Query: ./eng/inspect.sh --target {context['target']} --results {relative} --offset {args.offset + args.limit} --limit {args.limit}")


class Run:
    def __init__(self, args, directory, context):
        self.args, self.directory, self.context = args, directory, context
        self.environment = dict(os.environ, NO_COLOR="1", FORCE_COLOR="0", GOMAXPROCS=os.environ.get("NAMI_BUILD_WORKERS", "2"))
        for key in ("MSBuildSDKsPath", "MSBUILD_EXE_PATH", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER"):
            self.environment.pop(key, None)
        self.summary = dict(context=context, status="SETUP_FAILED", exitCode=2, build="not run", checks=[], problems=[])
        self.findings = []
        self.lane = "compiler"

    def command(self, label, command, cwd=ROOT, environment=None, timeout=300, json_output=False):
        stdout = self.directory / f"{label}.{'json' if json_output else 'log'}"
        stderr = self.directory / f"{label}.stderr.log"
        print(f"INSPECT RUN: {label}", flush=True)
        write_json(self.directory / f"{label}.command.json", [str(item) for item in command])
        with stdout.open("w") as output, stderr.open("w") as errors:
            with subprocess.Popen(command, cwd=cwd, env=environment or self.environment, stdout=output, stderr=errors,
                                  start_new_session=os.name != "nt") as process:
                deadline = time.monotonic() + timeout
                try:
                    while True:
                        try:
                            code = process.wait(timeout=max(0.01, min(30, deadline - time.monotonic())))
                            break
                        except subprocess.TimeoutExpired:
                            if time.monotonic() >= deadline:
                                self.summary["checks"].append(dict(name=label, timedOut=True))
                                raise Failure("PROTOCOL_FAILED" if self.lane == "protocol" else "ANALYSIS_FAILED", f"{label} exceeded {timeout}s; see {stdout.name}.")
                            print(f"INSPECT WAIT: {label}", flush=True)
                finally:
                    if process.poll() is None:
                        if os.name == "nt": process.kill()
                        else: os.killpg(process.pid, signal.SIGKILL)
                        process.wait()
        self.summary["checks"].append(dict(name=label, exitCode=code, stdout=stdout.name, stderr=stderr.name))
        if self.lane == "compiler" and not json_output:
            warnings = self.summary.setdefault("buildDiagnostics", [])
            for path in [stdout, stderr]:
                for line in path.read_text(errors="replace").splitlines():
                    if re.search(r"\bwarning\s+[A-Z]+\d+\s*:", line) and line.strip() not in warnings:
                        warnings.append(line.strip())
        if code:
            messages = []
            for path in [stdout, stderr]:
                for line in path.read_text(errors="replace").splitlines():
                    line = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", line)
                    match = re.search(r"^(.+)\((\d+),(\d+)\): (error|warning) (TS\d+): (.*)$", line)
                    typespec = re.search(r"^(.+):(\d+):(\d+) - (error|warning) ([\w/-]+): (.*)$", line)
                    match = match or typespec
                    if match:
                        file, row, column, level, rule, message = match.groups()
                        if typespec and match is typespec: rule = "TypeSpec/" + rule
                        self.findings.append(dict(lane="compiler", level=level, rule=rule, path=file,
                                                  line=int(row), column=int(column), message=message))
                    elif re.search(r"\b(error|failed|exception)\b", line, re.I): messages.append(line)
            self.summary["problems"].extend(messages[-5:] or [f"{label} exited {code}; see its evidence files."])
            if self.lane == "protocol":
                self.findings.append(dict(lane="protocol", level="error", rule="MCP_COMMAND_FAILED", path="<mcp>", line=1, column=1, message=f"{label} exited {code}; see {stderr.name}."))
            raise Failure("PROTOCOL_FAILED" if self.lane == "protocol" else "FINDINGS" if self.findings else "ANALYSIS_FAILED", f"{label} failed with exit {code}.")
        if json_output:
            if stdout.stat().st_size > 8 * 1024 * 1024:
                raise Failure("ANALYSIS_FAILED", f"{label} JSON exceeds the evidence bound.")
            return json.loads(stdout.read_text())

    def compiler_versions(self):
        self.context["packages"] = {}
        for root in ["contracts", "src/nami.mcp"]:
            package = json.loads((ROOT / root / "package.json").read_text())
            for name, expected in (package.get("dependencies", {}) | package.get("devDependencies", {})).items():
                path = ROOT / root / "node_modules" / name / "package.json"
                if not path.is_file() or json.loads(path.read_text())["version"] != expected:
                    raise Failure("SETUP_FAILED", f"{root}: installed {name} does not match exact package pin {expected}.")
                self.context["packages"][f"{root}:{name}"] = expected
            manifest = ROOT / root / "node_modules/typescript/package.json"
            expected = package["devDependencies"]["typescript"]
            if not manifest.is_file() or json.loads(manifest.read_text())["version"] != expected:
                raise Failure("SETUP_FAILED", f"{root}: restore the locked TypeScript {expected} dependency.")
            script = "import get from './node_modules/typescript/lib/getExePath.js';console.log(get())"
            executable = Path(subprocess.check_output([self.context['tools']['node']['path'], "--input-type=module", "-e", script], cwd=ROOT / root, text=True, timeout=15).strip())
            actual = tool(executable, ["--version"]).removeprefix("Version ")
            if actual != expected:
                raise Failure("SETUP_FAILED", f"{root}: native compiler is {actual}, package requires {expected}.")
            self.context["tools"][root] = dict(version=actual, path=str(executable), sha256=hashlib.sha256(executable.read_bytes()).hexdigest())

    def prepare(self):
        self.summary["build"] = "running"
        bun = self.context["tools"]["bun"]["path"]
        for name, root in [("contracts-install", "contracts"), ("mcp-install", "src/nami.mcp")]:
            self.command(name, [bun, "install", "--frozen-lockfile"], ROOT / root)
        self.compiler_versions()
        generated = generated_manifest()
        write_json(self.directory / "generated-before.json", generated)
        backup = self.directory / "generation-before"
        for relative in GENERATED:
            if (ROOT / relative).exists():
                shutil.copytree(ROOT / relative, backup / relative)
        try:
            self.command("contracts", [bun, "run", "compile"], ROOT / "contracts")
        finally:
            # Validation must not erase checked-in output when a TypeSpec build fails.
            after = generated_manifest()
            write_json(self.directory / "generated-after.json", after)
            for relative in GENERATED:
                destination = ROOT / relative
                if destination.exists():
                    if generated != after:
                        shutil.copytree(destination, self.directory / "generation-after" / relative)
                    shutil.rmtree(destination)
                if (backup / relative).exists():
                    shutil.copytree(backup / relative, destination)
        if generated != after:
            for path in sorted(generated.keys() | after.keys()):
                if generated.get(path) != after.get(path):
                    self.findings.append(dict(lane="schema", level="error", rule="GENERATED_MISMATCH", path=path,
                                              line=1, column=1, message="Regeneration changed, added or removed this artifact."))
            raise Failure("FINDINGS", "Generated contracts changed. Review and commit generation before validating.")
        self.command("typescript-mcp", [bun, "run", "build"], ROOT / "src/nami.mcp")
        if self.args.target == "mcp":
            self.command("publish", ["bash", str(ROOT / "eng/build.sh"), "PublishNative", "--Configuration", self.args.configuration])
        if fingerprint() != self.context["source"]:
            raise Failure("STALE", "Compiler inputs changed during preparation; rerun against a stable checkout.")
        self.summary["build"] = "passed"

    def evidence(self, mode, path, era=""):
        result = self.command(f"{path.stem}-validation", [self.context["tools"]["bun"]["path"],
                              str(ROOT / "eng/scripts/mcp_evidence.mjs"), mode, str(path), era], json_output=True)
        self.findings.extend(result["findings"])
        return result

    def protocol(self):
        self.lane = "protocol"
        self.summary["status"] = "PROTOCOL_FAILED"
        bun, node = (self.context["tools"][name]["path"] for name in ["bun", "node"])
        cli = [node, self.context["tools"]["mcpdo"]["path"]]
        metadata = self.command("metadata", [bun, str(ROOT / "eng/scripts/mcp_evidence.mjs"), "metadata"], json_output=True)
        if not metadata.get("probe") or len(metadata["tools"]) != 6:
            raise Failure("SETUP_FAILED", "The generated six-tool probe manifest is incomplete.")
        environment = {key: value for key, value in self.environment.items() if not key.startswith(("NAMI_", "OTEL_")) and key != "MCP_CATALOG_PATH"}
        # The private command returns a secret daemon token. Keep it only in memory, never in logs.
        private = subprocess.check_output([*cli, "private"], env=environment, text=True, timeout=15)
        for line in private.splitlines():
            words = shlex.split(line)
            if not words or words[0] != "export": raise Failure("SETUP_FAILED", "Unexpected private-daemon response.")
            for word in words[1:]:
                key, value = word.split("=", 1)
                if key not in {"MCP_INSPECTOR_DAEMON_DIR", "MCP_INSPECTOR_DAEMON_TOKEN"}: raise Failure("SETUP_FAILED", "Unexpected private-daemon variable.")
                environment[key] = value
        keys = metadata["config"]
        server_env = {keys["AuthMode"]: "Unsecured", keys["ProjectKeys"]: "{}", keys["OtlpPort"]: "0",
                      keys["GrpcPort"]: "0", keys["BindAddress"]: "127.0.0.1", "ASPNETCORE_URLS": "http://127.0.0.1:0"}
        eras = ["modern", "legacy"] if self.args.era == "both" else [self.args.era]
        transports = ["stdio", "http"] if self.args.transport == "both" else [self.args.transport]
        binary = ROOT / "artifacts/publish" / ("nami.exe" if os.name == "nt" else "nami")
        command = [bun, str(ROOT / "artifacts/publish/mcp/main.js"), "--engine", str(binary)]
        server = None
        self.summary["cleanup"] = dict(privateDaemonStopped=False, httpExitCode=None)
        config = {"mcpServers": {}}
        with tempfile.TemporaryDirectory(prefix="state.", dir=self.directory) as state:
            try:
                if "http" in transports:
                    with socket.socket() as listener:
                        listener.bind(("127.0.0.1", 0)); port = listener.getsockname()[1]
                    http_env = dict(environment, **server_env, **{keys["Port"]: str(port), keys["DataPath"]: str(Path(state) / "http.duckdb")})
                    with (self.directory / "http-server.log").open("w") as log:
                        server = subprocess.Popen(command, cwd=ROOT, env=http_env, stdin=subprocess.PIPE, stdout=log, stderr=log)
                    deadline = time.monotonic() + 30
                    while True:
                        if server.poll() is not None: raise Failure("ANALYSIS_FAILED", "HTTP MCP host exited; see http-server.log.")
                        try:
                            with urllib.request.urlopen(f"http://127.0.0.1:{port}/health", timeout=1) as response:
                                if response.status == 200: break
                        except OSError:
                            if time.monotonic() >= deadline: raise Failure("ANALYSIS_FAILED", "HTTP MCP readiness timed out.")
                            time.sleep(0.1)
                for transport in transports:
                    for era in eras:
                        name = f"nami-{transport}-{era}"
                        entry = {"url": f"http://127.0.0.1:{port}/mcp", "type": "streamable-http"} if transport == "http" else dict(type="stdio", command=bun, args=command[1:] + ["--stdio"], cwd=str(ROOT), env=dict(server_env, **{keys["DataPath"]: str(Path(state) / f"{name}.duckdb")}))
                        config["mcpServers"][name] = entry
                config_file = self.directory / "connections.json"
                write_json(config_file, config)
                client = [*cli, "--plain", "--format", "json", "--stored-auth-only", "--config", str(config_file)]
                for transport in transports:
                    for era in eras:
                        name = f"nami-{transport}-{era}"
                        connected = self.command(f"{name}-connect", [*client, "connect", name, "--era", era, "--elicit", "off"], environment=environment, json_output=True, timeout=40)
                        if connected.get("pendingAuth"): raise Failure("ANALYSIS_FAILED", "Unexpected interactive auth in local probe.")
                        for operation, mode in [("connections/show", "connection"), ("tools/list", "catalog"), ("tools/call", "call")]:
                            label = f"{name}-{mode}"
                            arguments = [name] if mode == "connection" else [metadata["probe"], "--tool-args-json", "{}"] if mode == "call" else []
                            self.command(label, [*client, "--connection", name, operation, *arguments], environment=environment, json_output=True, timeout=40)
                            self.evidence(mode, self.directory / f"{label}.json", era)
                        self.command(f"{name}-disconnect", [*client, "disconnect", name], environment=environment, json_output=True, timeout=20)
            finally:
                try:
                    self.command("daemon-stop", [*cli, "--format", "json", "daemon", "stop"], environment=environment, json_output=True, timeout=20)
                    deadline = time.monotonic() + 5
                    while True:
                        stopped = self.command("daemon-status", [*cli, "--format", "json", "daemon", "status"], environment=environment, json_output=True, timeout=10)
                        if stopped.get("running") is False:
                            self.summary["cleanup"]["privateDaemonStopped"] = True
                            break
                        if time.monotonic() >= deadline: raise Failure("PROTOCOL_FAILED", "Private daemon did not stop.")
                        time.sleep(0.1)
                finally:
                    if server:
                        server.stdin.close()
                        try:
                            code = server.wait(timeout=10)
                            self.summary["cleanup"]["httpExitCode"] = code
                            if code:
                                self.summary["problems"].append("HTTP host exited unsuccessfully; see http-server.log.")
                        except subprocess.TimeoutExpired:
                            server.terminate()
                            try: server.wait(timeout=3)
                            except subprocess.TimeoutExpired: server.kill(); server.wait()
                            self.summary["cleanup"]["httpExitCode"] = server.returncode
                            self.summary["problems"].append("HTTP host required forced shutdown.")
        self.summary["coverage"] = dict(transports=transports, eras=eras, catalog=metadata["tools"], toolCalls=[metadata["probe"]],
                                        excludes=["Hosted OAuth/connectors", "business-logic correctness (covered by existing E2E suites)"])
        if self.summary["problems"]:
            raise Failure("PROTOCOL_FAILED", "Protocol cleanup did not complete normally.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", choices=["typescript", "mcp"], required=True)
    parser.add_argument("--configuration", choices=["Release", "Debug"], default="Release")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--transport", choices=["stdio", "http", "both"])
    parser.add_argument("--era", choices=["modern", "legacy", "both"])
    parser.add_argument("--results", type=Path)
    parser.add_argument("--rule")
    parser.add_argument("--level", choices=list(RANK))
    parser.add_argument("--offset", type=int, default=0)
    parser.add_argument("--limit", type=int, default=20)
    args = parser.parse_args()
    if args.offset < 0 or not 1 <= args.limit <= 200: parser.error("Invalid evidence page bounds")
    if args.target == "typescript" and (args.transport or args.era): parser.error("Transport/era apply only to the MCP target")
    if args.results and (args.no_build or args.transport or args.era): parser.error("Stored run scope is immutable")
    args.transport, args.era = args.transport or "both", args.era or "both"
    if args.results:
        directory = (ROOT / args.results).resolve(); directory.relative_to(BASE)
        summary = json.loads((directory / "summary.json").read_text())
        findings = [json.loads(line) for line in (directory / "findings.jsonl").read_text().splitlines()]
        if summary["context"]["target"] != args.target: parser.error("Stored target differs")
        if fingerprint() != summary["context"]["source"]:
            summary.update(status="STALE", exitCode=2, problems=["Workspace differs from the recorded run."])
        elif summary["context"].get("outputs") and output_hash(args.target) != summary["context"]["outputs"]:
            summary.update(status="STALE", exitCode=2, problems=["Build outputs differ from the recorded run."])
        render(summary, findings, args, directory)
        return summary["exitCode"]
    BASE.mkdir(parents=True, exist_ok=True)
    directory = Path(tempfile.mkdtemp(prefix="run.", dir=BASE))
    context = dict(target=args.target, configuration=args.configuration, source=fingerprint(),
                   head=tool("git", ["rev-parse", "HEAD"]), tools={}, startedAt=datetime.now(timezone.utc).isoformat())
    print(f"INSPECT START: {args.target}; evidence={directory.relative_to(ROOT)}", flush=True)
    run = Run(args, directory, context)
    prepared = BASE / f"prepared-{args.target}-{args.configuration}.json"
    try:
        pin = json.loads((ROOT / ".config/agent-tools.json").read_text())
        context["tools"] = runtimes(pin)
        if args.target == "mcp":
            context["tools"]["mcpdo"] = mcp_tool(pin["mcpdo"], context["tools"]["node"]["path"])
            dotnet = os.environ.get("DOTNET_EXE") or ("/usr/local/share/dotnet/dotnet" if Path("/usr/local/share/dotnet/dotnet").is_file() else shutil.which("dotnet"))
            sdk = json.loads((ROOT / "global.json").read_text())["sdk"]["version"]
            if not dotnet or tool(dotnet, ["--version"]) != sdk:
                raise Failure("SETUP_FAILED", f"MCP publish requires the exact SDK {sdk} from global.json.")
            context["tools"]["dotnet"] = dict(version=sdk, path=str(Path(dotnet).resolve()))
            run.environment.update(DOTNET_EXE=context["tools"]["dotnet"]["path"],
                                   PATH=str(Path(dotnet).resolve().parent) + os.pathsep + run.environment.get("PATH", ""))
        run.summary["status"] = "BUILD_FAILED"
        if args.no_build:
            run.compiler_versions()
            previous = json.loads(prepared.read_text()) if prepared.exists() else {}
            expected = dict(source=context["source"], tools=context["tools"], packages=context["packages"], outputs=output_hash(args.target))
            if any(previous.get(key) != value for key, value in expected.items()):
                raise Failure("STALE", "No matching prepared build; rerun without --no-build.")
            run.summary["build"] = "reused"
            run.summary["buildDiagnostics"] = previous.get("diagnostics", [])
        else:
            prepared.unlink(missing_ok=True)
            run.prepare()
            write_json(prepared, dict(source=context["source"], tools=context["tools"], packages=context["packages"], outputs=output_hash(args.target), diagnostics=run.summary.get("buildDiagnostics", [])))
        before = output_hash(args.target)
        context["outputs"] = before
        if args.target == "mcp": run.protocol()
        if fingerprint() != context["source"] or output_hash(args.target) != before:
            prepared.unlink(missing_ok=True)
            raise Failure("STALE", "Workspace or outputs changed during validation.")
        run.summary.update(status="FINDINGS" if run.findings else "CLEAN", exitCode=1 if run.findings else 0)
    except Failure as error:
        if run.summary["build"] == "running": run.summary["build"] = "failed"
        run.summary.update(status=error.status, exitCode=1 if error.status == "FINDINGS" else 2)
        run.summary["problems"].append(str(error))
    except KeyboardInterrupt:
        run.summary.update(status="CANCELLED", exitCode=130)
        run.summary["problems"].append("Validation was cancelled; no clean analysis is claimed.")
    except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError) as error:
        if run.summary["build"] == "running": run.summary["build"] = "failed"
        run.summary["problems"].append(str(error))
    run.findings.sort(key=lambda item: (RANK[item["level"]], item["lane"], item["path"], item["line"], item["rule"]))
    run.summary["counts"] = dict(Counter(item["level"] for item in run.findings))
    run.summary["finishedAt"] = datetime.now(timezone.utc).isoformat()
    write_json(directory / "summary.json", run.summary)
    (directory / "findings.jsonl").write_text("".join(json.dumps(item) + "\n" for item in run.findings))
    render(run.summary, run.findings, args, directory)
    return run.summary["exitCode"]


if __name__ == "__main__":
    def cancel(_signal, _frame):
        raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, cancel)
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError) as error:
        print(f"INSPECT SETUP_FAILED: {error}")
        raise SystemExit(2)
