"""Validate one fresh InspectCode run and print every diagnostic kind with an example."""

import json
import re
import sys
from collections import Counter
from pathlib import Path
from urllib.parse import unquote


def location(result, artifacts):
    locations = result.get("locations", [])
    if not locations:
        return "<solution>"
    physical = locations[0].get("physicalLocation", {})
    artifact = physical.get("artifactLocation", {})
    if "uri" not in artifact and "index" in artifact:
        artifact = artifacts[artifact["index"]].get("location", {})
    path = unquote(artifact.get("uri", "<solution>"))
    region = physical.get("region", {})
    return f"{path}:{region.get('startLine', 1)}:{region.get('startColumn', 1)}"


def collect(report_path, log_path, process_exit):
    problems = []
    findings = []
    try:
        log = log_path.read_text(encoding="utf-8-sig", errors="replace")
    except OSError as error:
        log = ""
        problems.append(f"Cannot read tool log: {error}")
    plain = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", log)
    for line in plain.splitlines():
        if (re.match(r"(?:Roslyn error\b|Warning: Roslyn Source Generator error\b|Error:|ERROR\b)", line)
                or re.search(r"(?:Unable to resolve reference|Referenced project .+ not found|\[MsBuild\].*\b(?:CS|MSB|NETSDK)\d+:)", line)):
            problems.append(" ".join(line.split())[:350])
    try:
        document = json.loads(report_path.read_text(encoding="utf-8-sig"))
        if document.get("version") != "2.1.0":
            raise ValueError("Expected SARIF 2.1.0")
        runs = document.get("runs")
        if not isinstance(runs, list) or not runs:
            raise ValueError("SARIF contains no analysis runs")
        for run in runs:
            driver = run.get("tool", {}).get("driver", {})
            if driver.get("name") != "InspectCode":
                raise ValueError("Report was not produced by InspectCode")
            invocations = run.get("invocations")
            if not invocations or any(invocation.get("executionSuccessful") is not True for invocation in invocations):
                raise ValueError("InspectCode did not report a successful analysis invocation")
            for invocation in invocations:
                for kind in ("toolExecutionNotifications", "toolConfigurationNotifications"):
                    for notification in invocation.get(kind, []):
                        if notification.get("level") == "error":
                            problems.append(notification.get("message", {}).get("text", "Tool reported an error"))
            rules = {rule["id"]: rule for rule in driver.get("rules", [])}
            results = run.get("results")
            if not isinstance(results, list):
                raise ValueError("SARIF is missing its results array")
            for result in results:
                rule_id = result.get("ruleId", "<unknown>")
                level = result.get("level", rules.get(rule_id, {}).get("defaultConfiguration", {}).get("level", "warning"))
                if level not in ("error", "warning", "note", "none"):
                    raise ValueError(f"Unknown diagnostic level: {level}")
                message = result.get("message", {}).get("text") or result.get("message", {}).get("markdown", "")
                findings.append((level, rule_id, location(result, run.get("artifacts", [])), " ".join(message.split())))
    except (OSError, ValueError, TypeError, KeyError, IndexError, AttributeError) as error:
        problems.append(f"Invalid or missing report: {error}")

    return findings, list(dict.fromkeys(problems))


def summarize(report_path, log_path, process_exit, output):
    findings, problems = collect(report_path, log_path, process_exit)
    levels = Counter(finding[0] for finding in findings)
    groups = Counter((finding[0], finding[1]) for finding in findings)
    examples = {(finding[0], finding[1]): finding for finding in reversed(findings)}
    rank = {"error": 0, "warning": 1, "note": 2, "none": 3}
    for level, rule_id in sorted(groups, key=lambda key: (rank[key[0]], key[1])):
        _, _, path, message = examples[level, rule_id]
        print(f"{level.upper()} {rule_id} x{groups[level, rule_id]} {path}: {message}", file=output)
    for problem in dict.fromkeys(problems):
        print(f"TOOL ERROR: {problem}", file=output)
    if process_exit:
        print(f"TOOL EXIT: {process_exit}", file=output)
    status = "FAILED" if problems or process_exit else "FINDINGS" if findings else "CLEAN"
    print(f"INSPECTCODE {status}: total={len(findings)} errors={levels['error']} warnings={levels['warning']} notes={levels['note']} none={levels['none']}", file=output)
    print(f"Report: {report_path}\nLog: {log_path}", file=output)
    return process_exit if process_exit else 2 if problems else 1 if findings else 0


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit("Usage: inspect_report.py REPORT LOG PROCESS_EXIT")
    raise SystemExit(summarize(Path(sys.argv[1]), Path(sys.argv[2]), int(sys.argv[3]), sys.stdout))
