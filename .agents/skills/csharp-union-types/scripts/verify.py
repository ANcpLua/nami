#!/usr/bin/env python3
"""Check bundled compiler/runtime probes without fetching external source."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile


SDK = "11.0.100-rc.2.26504.105"
PACKAGE = Path(__file__).resolve().parents[1]

# Stable names describe behavior; source output is data, never instructions.
CHECKS = [
    {"name": "examples_compile_csharp15", "project": "Examples"},
    {"name": "functional_result_composition", "project": "Examples", "define": "FUNCTIONAL_EXAMPLES", "contains": [
        "functional-map-bind-match-and-short-circuit-verified",
        "functional-laws-and-pure-pipeline-verified",
        "functional-default-arguments-and-exceptions-verified"]},
    {"name": "examples_runtime_csharp15", "project": "Examples", "contains": [
        "outcome-cases-and-default-verified", "length-units-and-default-verified",
        "scalar-values-and-empty-states-verified",
        "generic-result-success-failure-and-default-verified",
        "scalar-construction-and-matching-allocated-bytes: 0"]},
    {"name": "generic_result_rejects_wrong_case_type", "project": "Examples",
     "define": "WRONG_GENERIC_CASE", "error": "CS0029"},
    {"name": "generic_result_requires_failure_case", "project": "Examples",
     "define": "MISSING_GENERIC_CASE", "error": "CS8509"},
    {"name": "default_null_runtime", "project": "QLang", "contains": [
        "NoNullArm(default) THROWS SwitchExpressionException",
        "WithNullArm(default) => null arm"]},
    {"name": "provider_conversions", "project": "QLang2", "contains": [
        "static abstract provider: True, True, default is null: True",
        "HasValue provider: True, default is null: True"]},
    {"name": "target_typed_conversions", "project": "QConv", "contains": [
        "Task.FromResult<U>(new A()) : A", "U? = null : no value"]},
    {"name": "interface_matches_contents", "project": "QIface", "contains": [
        "open T: False (int), True (Marked : IMarker)",
        "cat is IPet: True; bird is IPet: True; default is IPet: False"]},
    {"name": "roslyn_union_case_metadata", "project": "QRoslyn", "contains": [
        "compile errors: 0",
        "FourEyesApproval: TypeKind=Struct, UnionCaseTypes.IsDefault=False, [NotRequired, PendingApproval, PartlyApproved]",
        "IntOrString: TypeKind=Struct, UnionCaseTypes.IsDefault=False, [int, string]",
        "Shape: TypeKind=Struct, UnionCaseTypes.IsDefault=False, [Circle, Square]",
        "Plain: TypeKind=Class, UnionCaseTypes.IsDefault=False, []"]},
    {"name": "source_generated_json", "project": "QSrcGen", "contains": [
        'source-gen, plain "hello" => JsonException',
        'source-gen, strict attr "hello" => String(hello)',
        'ser Plain("x") => "x"', 'ser Strict(7) => 7']},
    {"name": "closed_json_roundtrip", "project": "QClosedJson", "contains": [
        '"$type":"NotRequired"', '"$type":"PendingApproval"',
        "no discriminator => NotSupportedException",
        "unknown discriminator => JsonException"], "counts": {"same: True": 5}},
    {"name": "self_type_rejected", "project": "QLang", "define": "RC1_IS_UNION_TYPE", "error": "CS8121"},
    {"name": "ambiguous_interfaces_rejected", "project": "QConv", "define": "AMBIG_SIBLINGS", "error": "CS0457"},
    {"name": "task_inference_rejected", "project": "QConv", "define": "INFER_TASK", "error": "CS0029"},
    {"name": "missing_case_is_error_when_promoted", "project": "QClosed", "define": "NONEXHAUSTIVE", "error": "CS8509", "promote": True},
    {"name": "missing_null_is_error_when_promoted", "project": "QClosed", "define": "NULLABLE_INPUT", "error": "CS8655", "promote": True},
    {"name": "guarded_coverage_is_error_when_promoted", "project": "QClosed", "define": "WHEN_ARM", "error": "CS8846", "promote": True},
]

CHECKS.extend([
    {"name": "polyfills_netstandard_compile", "project": "PolyfillConsumer", "framework": "netstandard2.0"},
    {"name": "polyfills_net10_runtime", "project": "PolyfillConsumer", "framework": "net10.0", "contains": [
        "polyfilled-union-types-verified", "case-default-and-attribute-verified"]},
    {"name": "polyfills_net11_framework_runtime", "project": "PolyfillConsumer", "framework": "net11.0", "contains": [
        "framework-union-types-verified", "case-default-and-attribute-verified"]},
])
CHECKS.extend(
    {"name": name, "project": "Diagnostics", "define": define, "error": diagnostic}
    for name, define, diagnostic in [
        ("declaration_requires_case", "NO_CASES", "CS9370"),
        ("declaration_rejects_nonboxable_case", "NONBOXABLE_CASE", "CS9371"),
        ("declaration_rejects_instance_field", "INSTANCE_FIELD", "CS9373"),
        ("declaration_rejects_public_case_constructor", "PUBLIC_CONSTRUCTOR", "CS9374"),
        ("declaration_requires_initializer", "MISSING_INITIALIZER", "CS9375"),
        ("custom_union_requires_creation", "MISSING_CREATION", "CS9385"),
        ("provider_requires_value", "MISSING_PROVIDER_VALUE", "CS9386"),
        ("declaration_rejects_provider", "FORBIDDEN_PROVIDER", "CS9387"),
        ("duplicate_arm_is_unreachable", "UNREACHABLE_ARM", "CS8510"),
        ("closed_name_is_reserved", "CLOSED_NAME", "CS9380"),
        ("closed_cannot_be_sealed", "SEALED_CLOSED", "CS9381"),
        ("closed_generic_parameter_must_be_bound", "UNBOUND_CLOSED_PARAMETER", "CS9383"),
        ("closed_is_already_abstract", "ABSTRACT_CLOSED", "CS9384"),
        ("closed_metadata_requires_type_array", "BAD_CLOSED_METADATA", "CS9395"),
    ]
)
CHECKS.append({"name": "closed_rejects_external_derivation", "project": "QClosed",
               "define": "DERIVE_CLOSED", "error": "CS9382"})


def execute(command, cwd, env):
    result = subprocess.run(command, cwd=cwd, env=env, capture_output=True, text=True, timeout=120)
    return result.returncode, result.stdout + result.stderr


def check(case, workspace, env, binlogs):
    project = workspace / "probes" / case["project"] / (case["project"] + ".csproj")
    command = ["dotnet", "build", str(project), "--configuration", "Release",
               "--no-incremental", "--nologo", "--verbosity", "minimal", "-maxcpucount:2",
               "-nodeReuse:false", "-p:BuildInParallel=false", "-p:UseSharedCompilation=false",
               "-bl:" + str(binlogs / (case["name"] + "-{}.binlog"))]
    if "define" in case:
        command.append("-p:DefineConstants=" + case["define"])
    if "framework" in case:
        command.extend(["--framework", case["framework"]])
    if case.get("promote"):
        command.append("-p:WarningsAsErrors=" + case["error"])
    code, output = execute(command, workspace, env)
    errors = set(re.findall(r"\berror (CS\d+|[A-Z]+\d+)\b", output))
    diagnostics = []
    for line in output.splitlines():
        match = re.search(r"\b(error|warning) (CS\d+): (.*?)(?: \[[^\]]+\])?$", line)
        if match:
            item = {"severity": match[1], "id": match[2], "message": match[3]}
            if item not in diagnostics:
                diagnostics.append(item)
    failures = []
    result = {"name": case["name"], "build_exit": code, "error_codes": sorted(errors),
              "diagnostics": diagnostics}
    if "error" in case:
        if code == 0 or errors != {case["error"]}:
            failures.append("Expected a failed build with only " + case["error"])
    elif code != 0:
        failures.append("Expected successful compilation")
    elif case.get("contains") or case.get("counts"):
        dll = project.parent / "bin" / "Release" / case.get("framework", "net11.0") / (case["project"] + ".dll")
        run_code, run_output = execute(["dotnet", str(dll)], workspace, env)
        result["run_exit"] = run_code
        output += "\nRUNTIME:\n" + run_output
        normalized = re.sub(r"\s+", " ", run_output)
        if run_code != 0:
            failures.append("Expected runtime exit 0")
        for expected in case.get("contains", []):
            if expected not in normalized:
                failures.append("Missing runtime evidence: " + expected)
        for expected, count in case.get("counts", {}).items():
            if run_output.count(expected) != count:
                failures.append(f"Expected {count} occurrences of {expected!r}")
    result.update(passed=not failures, failures=failures,
                  output=output.replace(str(workspace), "<temporary>"))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    binlogs = args.report.resolve().parent / "binlogs"
    binlogs.mkdir(parents=True, exist_ok=True)
    report = {"sdk": SDK,
              "started_at_utc": datetime.now(timezone.utc).isoformat(), "checks": []}
    try:
        env = dict(os.environ, DOTNET_PROCESSOR_COUNT="2", DOTNET_CLI_TELEMETRY_OPTOUT="1",
                   DOTNET_CLI_UI_LANGUAGE="en", MSBUILDDISABLENODEREUSE="1")
        with tempfile.TemporaryDirectory(prefix="csharp-union-verification-") as temporary:
            workspace = Path(temporary)
            (workspace / "global.json").write_text(json.dumps({"sdk": {
                "version": SDK, "rollForward": "disable", "allowPrerelease": True}}))
            code, version = execute(["dotnet", "--version"], workspace, env)
            if code or version.strip() != SDK:
                raise ValueError("Install SDK " + SDK + " before running these version-specific checks")
            code, installed = execute(["dotnet", "--list-sdks"], workspace, env)
            sdk_match = re.search(r"^" + re.escape(SDK) + r" \[(.+)\]$", installed, re.MULTILINE)
            if code or not sdk_match:
                raise ValueError("Cannot locate the pinned compiler")
            compiler_dir = Path(sdk_match[1]) / SDK / "Roslyn" / "bincore"
            code, compiler_version = execute(["dotnet", str(compiler_dir / "csc.dll"), "-version"], workspace, env)
            if code:
                raise ValueError("Cannot identify the pinned compiler")
            report["compiler_version"] = compiler_version.strip()
            projects = {case["project"] for case in CHECKS} - {"Examples", "PolyfillConsumer", "Diagnostics"}
            projects.add("QClosedLib")
            for project in sorted(projects):
                shutil.copytree(PACKAGE / "probes" / project, workspace / "probes" / project,
                                ignore=shutil.ignore_patterns("bin", "obj", "artifacts"))
            for filename in ["Directory.Build.props", "Directory.Build.targets"]:
                shutil.copyfile(PACKAGE / "probes" / filename, workspace / "probes" / filename)
            examples = workspace / "probes" / "Examples"
            shutil.copytree(PACKAGE / "examples", examples)
            shutil.copytree(PACKAGE / "verification" / "Examples", examples, dirs_exist_ok=True)
            (examples / "Examples.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<OutputType>Exe</OutputType><TargetFramework>net11.0</TargetFramework><LangVersion>15.0</LangVersion>'
                '<Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>'
                '</PropertyGroup></Project>')
            shutil.copytree(PACKAGE / "polyfills", workspace / "polyfills")
            for name in ["PolyfillConsumer", "Diagnostics"]:
                shutil.copytree(PACKAGE / "verification" / name, workspace / "probes" / name,
                                ignore=shutil.ignore_patterns("bin", "obj"))
            for case in CHECKS:
                try:
                    result = check(case, workspace, env, binlogs)
                except (OSError, subprocess.TimeoutExpired) as error:
                    result = {"name": case["name"], "passed": False, "failures": [str(error)]}
                report["checks"].append(result)
                print(("PASS " if result["passed"] else "FAIL ") + case["name"], flush=True)
                if not result["passed"]:
                    print("; ".join(result["failures"]), flush=True)
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        report["setup_error"] = str(error)
        print("FAIL setup: " + str(error), file=sys.stderr)
    report["passed"] = (len(report["checks"]) == len(CHECKS)
                        and all(result["passed"] for result in report["checks"])
                        and "setup_error" not in report)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n")
    passed = sum(result["passed"] for result in report["checks"])
    print(f"{passed}/{len(CHECKS)} checks passed. Report: {args.report}")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
