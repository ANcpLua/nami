"""Anchor the Lean models to the code they model.

Lists every site in the modelled files that writes state a model owns, with a stable id
(file|member|pattern|ordinal) and a hash of the enclosing member, and compares the list with
the citations in cited.json, where each site names the model transition or the declared
abstraction that covers it. Roslyn's state-writes.cs sees field assignments only; most of this
state changes through method calls (_pending.TryAdd, this.pending.delete) and half of it is
TypeScript, so this script matches the write patterns below instead.

usage: python3 eng/proofs/anchor/anchor.py [--expect eng/proofs/anchor/cited.json]
Exit: 0 ok; 5 a pattern matches nothing (wrong pattern, not evidence); 6 citations out of date.
"""
import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PATTERNS = {
    "src/nami.mcp/src/engine.ts": {
        "stopped": r"this\.stopped\s*=",
        "hasExited": r"this\.hasExited\s*=",
        "shutdown": r"this\.shutdown\s*=",
        "sequence": r"\+\+this\.sequence",
        "pending": r"this\.pending\.(?:set|delete|clear)\(",
        "settle": r"\b(?:resolve|reject|request\.resolve|request\.reject)\(",
        "writesClosed": r"this\.writesClosed\.abort\(",
        "signal": r"this\.child\.kill\(",
        "stdinEnd": r"this\.child\.stdin\.end\(",
        "timer": r"\bsetTimeout\(",
        "exitHook": r"process\.(?:once|removeListener)\(\"exit\"",
        "fail": r"this\.fail\(",
        "close": r"this\.close\(\)",
        "frameBuffer": r"\bbuffer\s*=",
        "throw": r"\bthrow new ",
        "failed": r"this\.reportFailed\(",
        "sharedHealth": r"this\.sharedHealth\s*=",
        "withdraw": r"withdraw\.abort\(",
    },
    "src/nami.mcp/src/main.ts": {
        "closing": r"\bclosing\s*=",
        "engineCall": r"engine\.(?:request|health|close)\(",
        "healthAnswer": r"Response\.json\(",
        "signalHandler": r"process\.on(?:ce)?\(\"SIG|process\.stdin\.once\(\"end\"",
        "exit": r"process\.exit\(|process\.exitCode\s*=",
        "failed": r"engine\.failed",
        "transport": r"closeTransport\s*=",
        "serve": r"Bun\.serve\(|serveStdio\(",
        "ready": r"await engine\.ready",
    },
    "src/nami.mcp/src/server.ts": {
        "failureCode": r"const code = failure instanceof EngineError",
        "abortRethrow": r"signal\.throwIfAborted\(\)",
        "engineCall": r"engine\.(?:call|request)\(",
        "refusal": r"refused\.(?:set|get)\(",
    },
    "src/nami/Hosting/EngineProtocol.cs": {
        "pending": r"_pending\.(?:TryAdd|TryRemove|TryGetValue)\(",
        "cancelCall": r"active\.Cancel\(\)",
        "lifetime": r"_lifetime\.CancelAsync\(\)",
        "worker": r"_execution\.(?:WaitAsync|Release)\(",
        "tasks": r"_tasks\.(?:Add|RemoveAll)\(",
        "throw": r"throw new InvalidDataException",
        "respond": r"await Respond\(",
        "reply": r"await Reply\(",
        "drop": r"\bDrop\(\"",
        "skipping": r"\bskipping\s*=",
        "advance": r"_reader\.AdvanceTo\(",
        "loopEnd": r"^\s*break;",
        "readCompleted": r"read\.IsCompleted",
        "catchCancel": r"catch \(OperationCanceledException\)",
        "disposed": r"_disposed\s*=",
        "sourceCancel": r"_source\.Cancel\(\)",
    },
    "src/nami/Program.cs": {
        "run": r"EngineProtocol\.RunAsync\(",
        "stop": r"app\.StopAsync\(\)",
    },
}
KEYWORDS = {"if", "while", "for", "switch", "catch", "return", "foreach", "using", "lock", "else", "try",
            "finally", "new", "await", "function", "typeof", "throw", "nameof", "when"}
TS_METHOD = re.compile(r"^\s*(?:(?:private|public|protected|static|async|readonly)\s+)*([A-Za-z_]\w*)\s*(?:<[^>]*>)?\(")
TS_INLINE = [re.compile(r"\basync\s+([A-Za-z_]\w*)\s*\([^)]*\)\s*\{"),
             re.compile(r"\bconst\s+([A-Za-z_]\w*)\s*=\s*(?:async\s*)?\(.*=>\s*\{"),
             re.compile(r"\b(?:async\s+)?function\s+([A-Za-z_]\w*)\s*\(")]
CS_METHOD = re.compile(r"^\s*(?:(?:private|public|internal|protected|static|async|sealed|override)\s+)+[\w<>?,\[\]. ]+?\s+([A-Za-z_]\w*)\s*\(")


def braces(line):
    text = re.sub(r"\"(?:[^\"\\]|\\.)*\"|'(?:[^'\\]|\\.)*'|`[^`]*`|//.*$", "", line)
    return text.count("{") - text.count("}")


def header_name(lines, j, cs):
    """The method or function that the block opened at line j belongs to, if any."""
    if cs:
        for k in (j, j - 1):
            match = CS_METHOD.search(lines[k]) if k >= 0 else None
            if match and match.group(1) not in KEYWORDS:
                return k, match.group(1)
        return None
    for pattern in TS_INLINE:
        match = pattern.search(lines[j])
        if match:
            return j, match.group(1)
    for k in range(j, max(j - 4, -1), -1):  # a signature may span lines up to the opening brace
        match = TS_METHOD.search(lines[k])
        if match and match.group(1) not in KEYWORDS and (k == j or "{" not in lines[k]):
            return k, match.group(1)
    return None


def member_of(lines, index, cs):
    """The enclosing method or function of line index: its name and its text."""
    depth, need = 0, 1
    for j in range(index - 1, -1, -1):
        depth += braces(lines[j])
        if depth >= need:
            found = header_name(lines, j, cs)
            if found:
                start, name = found
                level, opened = 0, False
                for end in range(start, len(lines)):
                    level += braces(lines[end])
                    opened = opened or level > 0
                    if opened and level <= 0:
                        break
                return name, "\n".join(lines[start:end + 1])
            need = depth + 1
    return "<top level>", lines[index]


def inventory():
    sites, empty = [], []
    for file, patterns in PATTERNS.items():
        lines = (ROOT / file).read_text(encoding="utf-8").splitlines()
        cs = file.endswith(".cs")
        for key, pattern in patterns.items():
            found = [i for i, line in enumerate(lines) if re.search(pattern, line)]
            if not found:
                empty.append(f"{file}|{key}")
            counts = {}
            for i in found:
                member, body = member_of(lines, i, cs)
                counts[member] = counts.get(member, 0) + 1
                digest = hashlib.sha256(" ".join(body.split()).encode()).hexdigest()[:16]
                sites.append({"id": f"{file}|{member}|{key}|{counts[member]}", "line": i + 1,
                              "memberHash": digest, "text": lines[i].strip()[:120]})
    return sites, empty


def main():
    sites, empty = inventory()
    print(json.dumps({"count": len(sites), "sites": sites}, indent=1))
    if empty:
        for item in empty:
            print(f"EMPTY {item}", file=sys.stderr)
        return 5
    if "--expect" in sys.argv:
        cited = json.loads(Path(sys.argv[sys.argv.index("--expect") + 1]).read_text(encoding="utf-8"))
        by_id = {site["id"]: site for site in sites}
        problems = [f"UNCITED {site['id']}" for site in sites if site["id"] not in cited]
        problems += [f"STALE {key}" for key in cited if key not in by_id]
        problems += [f"DRIFTED {key}" for key, entry in cited.items()
                     if key in by_id and entry["memberHash"] != by_id[key]["memberHash"]]
        for problem in problems:
            print(problem, file=sys.stderr)
        if problems:
            return 6
        print(f"OK: all {len(sites)} sites cited, none stale or drifted.", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
