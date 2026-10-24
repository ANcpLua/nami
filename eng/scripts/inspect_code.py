"""One checkout, pinned tools, one build context, bounded agent-readable findings."""

import argparse
import hashlib
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path
from urllib.parse import urlparse, unquote

from inspect_report import collect

ROOT = Path(__file__).resolve().parents[2]
RANK = {"error": 0, "warning": 1, "note": 2, "none": 3}


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def digest(paths):
    result = hashlib.sha256()
    for path in sorted(set(paths)):
        result.update(str(path.relative_to(ROOT)).encode() + b"\0")
        result.update(hashlib.sha256(path.read_bytes()).digest() if path.is_file() else b"<missing>")
    return result.hexdigest()


def sources():
    listing = subprocess.check_output(["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"], cwd=ROOT)
    paths = [ROOT / os.fsdecode(name) for name in listing.split(b"\0") if name]
    # Git ignore rules do not control Compile's implicit globs. An ignored .cs file
    # still affects a build, so include build inputs inside every solution project.
    for project in ET.parse(ROOT / "nami.slnx").iter("Project"):
        for directory, children, files in os.walk((ROOT / project.attrib["Path"]).parent):
            children[:] = [name for name in children if name not in {"bin", "obj", "artifacts", "node_modules", ".git"}]
            paths.extend(Path(directory, name) for name in files
                         if Path(name).suffix in {".cs", ".csproj", ".props", ".targets", ".proto", ".resx", ".json"}
                         or name == ".editorconfig")
    return digest(paths)


def outputs(configuration):
    files = list((ROOT / "artifacts/bin").glob(f"**/{configuration.lower()}/**/*"))
    files += [p for p in (ROOT / "artifacts/obj").rglob("*")
              if p.name == "project.assets.json" or p.name.endswith((".nuget.g.props", ".nuget.g.targets"))]
    return digest(p for p in files if p.is_file())


def execute(command, log, environment):
    with log.open("w", encoding="utf-8") as stream:
        stream.write("$ " + json.dumps(command) + "\n")
        stream.flush()
        with subprocess.Popen(command, cwd=ROOT, env=environment, stdout=stream, stderr=subprocess.STDOUT) as process:
            while True:
                try:
                    code = process.wait(timeout=30)
                    return code if code >= 0 else 128 - code
                except subprocess.TimeoutExpired:
                    print(f"INSPECT WAIT: {log.name}", flush=True)


def normalize_location(location):
    path, line, column = location.rsplit(":", 2)
    if path.startswith("file:"):
        path = unquote(urlparse(path).path)
    if Path(path).is_absolute():
        try:
            path = str(Path(path).relative_to(ROOT))
        except ValueError:
            pass
    return path, int(line), int(column)


def render(summary, findings, args, directory):
    context = summary["context"]
    print(f"INSPECT {summary['status']}: exit={summary['exitCode']} scope={context['scope']} configuration={context['configuration']} source={context.get('source', '?')[:12]}")
    print(f"Toolchain: SDK {context['sdk']}; InspectCode {context['tool']}")
    build = summary.get("build", {})
    print(f"Build: {build.get('status', 'not run')}; diagnostics={len(build.get('diagnostics', []))}")
    for diagnostic in build.get("diagnostics", [])[:args.limit]:
        print(f"BUILD: {diagnostic}")
    counts = summary.get("counts", {})
    print(f"Findings: {sum(counts.values())} errors={counts.get('error', 0)} warnings={counts.get('warning', 0)} notes={counts.get('note', 0)}")
    selected = [f for f in findings if (not args.rule or f['rule'] == args.rule)
                and (not args.level or f['level'] == args.level)]
    for finding in selected[args.offset:args.offset + args.limit]:
        print(f"{finding['level'].upper()} {finding['rule']} {finding['path']}:{finding['line']}:{finding['column']}: {finding['message'][:500]}")
    if len(selected) > args.offset + args.limit:
        print(f"More: ./eng/inspect.sh --results {shlex.quote(str(directory.relative_to(ROOT)))} --offset {args.offset + args.limit} --limit {args.limit}"
              + (f" --rule {shlex.quote(args.rule)}" if args.rule else "") + (f" --level {args.level}" if args.level else ""))
    for problem in summary.get("problems", [])[:args.limit]:
        print(f"PROBLEM: {' '.join(problem.split())[:1000]}")
    print(f"Evidence: {directory.relative_to(ROOT)}/summary.json")
    print(f"Query: ./eng/inspect.sh --results {directory.relative_to(ROOT)} [--rule ID] [--level warning] [--offset N]")


def scoped(path, selections):
    return not selections or any(path == choice or path.startswith(choice.rstrip('/') + '/') for choice in selections)


def main():
    parser = argparse.ArgumentParser(prog="./eng/inspect.sh", description=__doc__)
    parser.add_argument("--project", help="Exact project name from nami.slnx (or its .csproj path)")
    parser.add_argument("--file", action="append", default=[], help="Repository file or directory; repeat to combine")
    parser.add_argument("--configuration", choices=("Release", "Debug"), default="Release")
    parser.add_argument("--no-build", action="store_true", help="Require an unchanged build prepared by this entry point")
    parser.add_argument("--results", type=Path, help="Query a previous run without rerunning tools")
    parser.add_argument("--rule")
    parser.add_argument("--level", choices=tuple(RANK))
    parser.add_argument("--offset", type=int, default=0)
    parser.add_argument("--limit", type=int, default=20)
    args = parser.parse_args()
    if args.offset < 0 or not 1 <= args.limit <= 200:
        parser.error("--offset must be nonnegative; --limit must be between 1 and 200")
    if args.results:
        if args.project or args.file or args.no_build:
            parser.error("Stored run scope is immutable; query it with --rule, --level, --offset and --limit")
        directory = (ROOT / args.results).resolve()
        directory.relative_to(ROOT / "artifacts/inspection")
        summary = json.loads((directory / "summary.json").read_text())
        findings = [json.loads(line) for line in (directory / "findings.jsonl").read_text().splitlines()]
        stale = sources() != summary["context"].get("source")
        if stale:
            summary = dict(summary, status="STALE", problems=["Workspace differs from this recorded run. Run inspection again.", *summary.get("problems", [])])
        render(summary, findings, args, directory)
        return 2 if stale else summary["exitCode"]

    projects = {Path(p.attrib["Path"]).stem: p.attrib["Path"] for p in ET.parse(ROOT / "nami.slnx").iter("Project")}
    project = None
    if args.project:
        requested = str((ROOT / args.project).resolve().relative_to(ROOT)) if args.project.endswith('.csproj') else args.project
        project = next((name for name, path in projects.items() if requested in (name, path)), None)
        if not project:
            parser.error(f"Unknown project {args.project!r}; choose: {', '.join(sorted(projects))}")
    selections = []
    for name in args.file:
        path = (ROOT / name).resolve()
        relative = str(path.relative_to(ROOT))
        if not path.exists() or any(c in relative for c in '*?;'):
            parser.error(f"Expected an existing literal repository file/directory: {name}")
        candidates = [path] if path.is_file() else path.rglob("*.cs")
        def inspectable(candidate):
            parts = candidate.relative_to(ROOT).parts
            return (candidate.suffix == '.cs' and not candidate.name.endswith('.g.cs')
                    and not {'artifacts', 'obj', 'bin', 'Generated'}.intersection(parts)
                    and not candidate.is_relative_to(ROOT / 'internal/nami.contracts/Nami')
                    and any(candidate.is_relative_to((ROOT / project_path).parent) for project_path in projects.values()))
        if not any(inspectable(candidate) for candidate in candidates):
            parser.error(f"Scope has no inspectable C# source in nami.slnx: {name}")
        selections.append(relative)
    if project and selections:
        parser.error("Use --project or --file; each retains the full solution reference graph")

    base = ROOT / "artifacts/inspection"
    base.mkdir(parents=True, exist_ok=True)
    directory = Path(tempfile.mkdtemp(prefix="run.", dir=base))
    pin = json.loads((ROOT / "global.json").read_text())["sdk"]["version"]
    manifest = ROOT / ".config/dotnet-tools.json"
    tool = json.loads(manifest.read_text())["tools"]["jetbrains.resharper.globaltools"]["version"]
    context = dict(root=str(ROOT), sdk=pin, tool=tool, configuration=args.configuration,
                   scope=project or selections or "solution", source=sources(),
                   head=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip())
    summary = dict(context=context, status="SETUP_FAILED", exitCode=2, counts={}, problems=[])
    findings = []
    environment = dict(os.environ)
    for key in ("MSBuildSDKsPath", "MSBUILD_EXE_PATH", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER"):
        environment.pop(key, None)
    dotnet = os.environ.get("DOTNET_EXE") or ("/usr/local/share/dotnet/dotnet" if Path("/usr/local/share/dotnet/dotnet").is_file() else shutil.which("dotnet"))
    state_path = base / f"prepared-{args.configuration}.json"
    print(f"INSPECT START: {context['scope']} ({args.configuration}); evidence={directory.relative_to(ROOT)}", flush=True)
    try:
        if not dotnet:
            raise ValueError("dotnet is missing; install the SDK pinned in global.json")
        environment.update(DOTNET_ROOT=str(Path(dotnet).resolve().parent), DOTNET_CLI_UI_LANGUAGE="en-US",
                           PATH=str(Path(dotnet).resolve().parent) + os.pathsep + environment.get("PATH", ""))
        actual = subprocess.check_output([dotnet, "--version"], cwd=ROOT, env=environment, text=True).strip()
        if actual != pin:
            raise ValueError(f"SDK mismatch: global.json requires {pin}, selected host returned {actual}")
        context["dotnet"] = str(Path(dotnet).resolve())
        code = execute([dotnet, "tool", "restore", "--tool-manifest", str(manifest)], directory / "tool-restore.log", environment)
        if code:
            summary["exitCode"] = code
            raise ValueError("Pinned InspectCode restore failed; see tool-restore.log")
        if args.no_build:
            state = json.loads(state_path.read_text()) if state_path.exists() else {}
            expected = dict(source=context["source"], sdk=pin, dotnet=context["dotnet"], configuration=args.configuration)
            if any(state.get(key) != value for key, value in expected.items()) or state.get("outputs") != outputs(args.configuration):
                raise ValueError("No matching prepared build: sources, toolchain or outputs changed. Rerun without --no-build.")
            summary["build"] = dict(status="reused", diagnostics=state.get("diagnostics", []))
        else:
            state_path.unlink(missing_ok=True)
            print("INSPECT BUILD: full solution, bounded workers", flush=True)
            code = execute([dotnet, "build", str(ROOT / "nami.slnx"), "--configuration", args.configuration,
                            "-maxcpucount:" + environment.get("NAMI_BUILD_WORKERS", "2"), "-nodeReuse:false",
                            "-p:UseSharedCompilation=false", f"-bl:{directory}/build-{{}}.binlog"], directory / "build.log", environment)
            if code:
                summary.update(status="BUILD_FAILED", exitCode=code)
                lines = (directory / "build.log").read_text(errors="replace").splitlines()
                summary["problems"] = list(dict.fromkeys(line.strip() for line in lines if re.search(r"\berror\b", line, re.I)))[:20] or lines[-10:]
                raise ValueError("Build failed; inspection was not run. See build.log.")
            diagnostics = list(dict.fromkeys(line.strip() for line in (directory / "build.log").read_text(errors="replace").splitlines()
                                            if re.search(r"\bwarning\s+[A-Z]+\d+\s*:", line)))
            summary["build"] = dict(status="passed", diagnostics=diagnostics)
            if sources() != context["source"]:
                summary["status"] = "STALE"
                raise ValueError("Workspace changed during build; rerun against a stable checkout.")
            state = dict(source=context["source"], sdk=pin, dotnet=context["dotnet"], configuration=args.configuration,
                         outputs=outputs(args.configuration), diagnostics=diagnostics)
            write_json(state_path, state)
        print("INSPECT ANALYZE: pinned local tool, full solution reference graph", flush=True)
        command = [dotnet, "tool", "run", "jb", "--", "inspectcode", str(ROOT / "nami.slnx"), "--no-build", "--no-updates", "--swea",
                   "--jobs=" + environment.get("NAMI_BUILD_WORKERS", "2"),
                   "--severity=INFO", "--format=Sarif", f"--dotnetcoresdk={pin}", f"--properties=Configuration={args.configuration}",
                   "--disable-settings-layers=GlobalAll;GlobalPerProduct;SolutionPersonal;ProjectPersonal",
                   f"--caches-home={base}/cache/{pin}/{tool}/{args.configuration}", f"--output={directory}/inspectcode.sarif",
                   "--exclude=**/artifacts/**;**/obj/**;**/bin/**;**/*.g.cs;**/*.dll;**/.nuget/**;internal/nami.contracts/Nami/**;internal/nami.semconv/Generated/**"]
        if project:
            command.append(f"--project={project}")
        write_json(directory / "command.json", command)
        summary["status"] = "ANALYSIS_FAILED"
        code = execute(command, directory / "inspectcode.log", environment)
        raw, problems = collect(directory / "inspectcode.sarif", directory / "inspectcode.log", code)
        for level, rule, location, message in raw:
            if location == "<solution>":
                path, line, column = "<solution>", 1, 1
            else:
                path, line, column = normalize_location(location)
            if scoped(path, selections):
                findings.append(dict(level=level, rule=rule, path=path, line=line, column=column, message=message))
        if sources() != context["source"] or outputs(args.configuration) != state["outputs"]:
            problems.append("Workspace or build outputs changed during analysis; result cannot describe a stable build. Rerun.")
            state_path.unlink(missing_ok=True)
        summary.update(status="ANALYSIS_FAILED" if problems or code else "FINDINGS" if findings else "CLEAN",
                       exitCode=code if code else 2 if problems else 1 if findings else 0, problems=problems,
                       counts=dict(Counter(f["level"] for f in findings)))
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        summary["problems"].append(str(error))
    findings.sort(key=lambda f: (RANK[f["level"]], f["path"], f["line"], f["column"], f["rule"]))
    write_json(directory / "summary.json", summary)
    (directory / "findings.jsonl").write_text("".join(json.dumps(f) + "\n" for f in findings))
    render(summary, findings, args, directory)
    return summary["exitCode"]


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError) as error:
        print(f"INSPECT SETUP_FAILED: {error}", file=sys.stderr)
        raise SystemExit(2)
