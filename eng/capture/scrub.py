# /// script
# requires-python = ">=3.12"
# dependencies = ["opentelemetry-proto==1.45.2"]
# ///
"""Turns a raw capture of eng/capture/capture.sh into the scrubbed fixture.

    uv run eng/capture/scrub.py <raw-dir> [<fixture-dir>]

<fixture-dir> defaults to tests/Nami.IntegrationTests/Fixtures/ClaudeCodeOtlp, which holds the
sessions s1 to s3. A capture of other sessions (SESSIONS of capture.sh) needs a folder of its own.
Every string and every trace and span id of every body goes through one replacement table shared
by both folders; the fixture's README lists what is replaced by what. The bodies are re-encoded
with deterministic serialization and written as NNNNN-<signal>.pb and .json with a manifest.json
per folder.

The files are written to a temporary folder first and checked there: none of the values the table
replaced and no name from environment.json is left in any file, no string matches a pattern of
FORBIDDEN (an at sign, a home or temporary path, a credential, an id that is not a placeholder),
every .pb decodes to exactly its .json, and every body keeps the structure of the original. Only
when every check passes do the files replace the fixture; otherwise the fixture stays as it was and
the script exits with 1. The replacement table is never written anywhere.

A check can only find what it has a pattern or a recorded value for. Read the diff of one logs and
one traces .json before committing a new capture.
"""
import base64
import collections
import datetime
import hashlib
import json
import os
import re
import shutil
import sys
import tempfile

from google.protobuf import json_format
from google.protobuf.descriptor import FieldDescriptor
from opentelemetry.proto.collector.logs.v1.logs_service_pb2 import ExportLogsServiceRequest
from opentelemetry.proto.collector.metrics.v1.metrics_service_pb2 import ExportMetricsServiceRequest
from opentelemetry.proto.collector.trace.v1.trace_service_pb2 import ExportTraceServiceRequest

REQUESTS = {"logs": ExportLogsServiceRequest, "metrics": ExportMetricsServiceRequest,
            "traces": ExportTraceServiceRequest}
# The signed-in sessions of the fixture in the repository.
FIXTURE_SESSIONS = ["s1", "s2", "s3"]
ID_FIELDS = {"trace_id", "span_id", "parent_span_id"}
ID_JSON_KEYS = {"traceId", "spanId", "parentSpanId"}

EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")
# An id can follow an underscore or a slash, so the edges are "no further character of the id", not \b.
UUID = re.compile(r"(?<![0-9a-fA-F])[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?![0-9a-fA-F])")
REQUEST_ID = re.compile(r"(?<![0-9A-Za-z])req_[0-9A-Za-z]{16,}")
TOOL_USE_ID = re.compile(r"(?<![0-9A-Za-z])toolu_[0-9A-Za-z]{16,}")
HEX_RUN = re.compile(r"(?<![0-9A-Za-z])[0-9a-fA-F]{7,40}(?![0-9A-Za-z])")
# Where Claude Code keeps a session's own files, such as the output of a background command:
# <tmp>/claude-<uid>/<the repository's path with dashes>/<session id>/tasks/<task>.output
CLAUDE_SESSION_FILES = re.compile(r"(?:/private)?/tmp/claude-\d+/[^/\s\"\\]+(?=/)")
SECRETS = [
    ("api-key", re.compile(r"\b(?:sk-(?:ant-|proj-)?[A-Za-z0-9_-]{16,}|gh[pousr]_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,})")),
    ("bearer", re.compile(r"\bBearer\s+[A-Za-z0-9._~+/=-]{8,}")),
    ("jwt", re.compile(r"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*")),
]
# What must not be left in any string of a written file, whatever the table says. The id patterns accept
# only the placeholders this script writes; a manifest's sha256 values are left out of the search.
FORBIDDEN = [
    ("an at sign", re.compile(rb"@")),
    ("a home path", re.compile(rb"/Users/|/home/(?!fixture)")),
    ("a temporary path", re.compile(rb"/var/folders|/private/|/tmp/|/tmp\b")),
    ("a credential", re.compile(rb"sk-ant-|sk-proj-|Bearer |eyJ[A-Za-z0-9_-]{8,}\.")),
    ("a UUID that is no placeholder", re.compile(
        rb"(?<![0-9a-fA-F])(?!00000000-0000-4000-8000-)[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?![0-9a-fA-F])")),
    ("a request, tool use or message id that is no placeholder", re.compile(
        rb"(?<![0-9A-Za-z])(?:req_(?!011FIXTURE)|toolu_(?!01FIXTURE)|msg_)[0-9A-Za-z]{16,}")),
    ("a 40-digit hex value that is no placeholder", re.compile(
        rb"(?<![0-9A-Za-z])(?!c0ffee0)[0-9a-fA-F]{40}(?![0-9A-Za-z])")),
    ("a 64-digit hex value that is no placeholder", re.compile(
        rb"(?<![0-9A-Za-z])(?!0{48})[0-9a-fA-F]{64}(?![0-9A-Za-z])")),
]


class Table:
    """Real value to replacement, numbered per kind in the order the values are first met."""

    def __init__(self, environment):
        self.fakes = collections.defaultdict(dict)
        self.known = {}  # exact values that no pattern recognises: kind by value
        self.commits = set()
        self.identifiers = {}  # hex form of every trace and span id: its kind
        self.paths = []  # (real prefix, replacement), longest first
        repository = {environment["repository"], environment["repositoryAsGiven"]}
        temporary = {environment["temporaryDirectory"], environment["temporaryDirectory"].removeprefix("/private"),
                     os.path.dirname(environment["repository"]), os.path.dirname(environment["repositoryAsGiven"])}
        paths = [(p, "/workspace/calc") for p in repository] + [(p, "/scratch") for p in temporary if p not in ("", "/")]
        paths.append((environment["home"], "/home/fixture"))
        self.paths = sorted(set(paths), key=lambda pair: -len(pair[0]))
        self.hosts = [h for h in environment["hosts"] if h and h != "localhost"]
        # Names of the person: looked for, never replaced, because nothing in the telemetry should hold one.
        names = [environment["user"], *environment["names"]]
        self.names = sorted({part.lower() for name in names for part in [name, *name.split()] if len(part) >= 4})

    def fake(self, kind, real, make):
        fakes = self.fakes[kind]
        if real not in fakes:
            fakes[real] = make(len(fakes) + 1)
        return fakes[real]

    def learn(self, key, value):
        """An attribute whose value is an id by its key alone."""
        if not isinstance(value, str) or not value:
            return
        if key == "user.id":
            self.known[value] = "user.id"
        elif key == "user.account_id":
            self.known[value] = "user.account_id"
        elif key == "agent_id":
            self.known[value] = "agent_id"
        elif key in ("vcs.ref.head.revision", "git_commit_id") and re.fullmatch(r"[0-9a-f]{40}", value):
            self.commits.add(value)

    def learn_identifier(self, real):
        if real:
            self.identifiers[real.hex()] = "trace id" if len(real) == 16 else "span id"
        return real

    def identifier(self, real):
        """A trace, span or parent span id, as bytes."""
        return bytes.fromhex(self._identifier(real.hex())) if real else real

    def _identifier(self, real):
        return self.fake(self.identifiers[real], real, lambda n: "fade" + format(n, "x").rjust(len(real) - 4, "0"))

    def _commit(self, real):
        return self.fake("commit", real, lambda n: "c0ffee" + format(n, "x").rjust(34, "0"))

    def text(self, value):
        value = CLAUDE_SESSION_FILES.sub("/scratch/claude", value)
        for real, replacement in self.paths:
            value = value.replace(real, replacement)
        for real, kind in sorted(self.known.items(), key=lambda pair: -len(pair[0])):
            if real in value:
                value = value.replace(real, self.fake(kind, real, {
                    "user.id": lambda n: format(n, "x").rjust(64, "0"),
                    "user.account_id": lambda n: "user_scrubbed" + format(n, "04d"),
                    "agent_id": lambda n: "afade" + format(n, "x").rjust(len(real) - 5, "0"),
                }[kind]))
        value = EMAIL.sub(lambda m: self.fake("e-mail address", m[0], lambda n: f"scrubbed-email-{n}"), value)
        value = UUID.sub(lambda m: self.fake("UUID", m[0].lower(),
                                             lambda n: "00000000-0000-4000-8000-" + format(n, "012x")), value)
        value = REQUEST_ID.sub(lambda m: self.fake("request id", m[0], lambda n: (
            "req_011FIXTURE" + str(n).rjust(len(m[0]) - 14, "0"))), value)
        value = TOOL_USE_ID.sub(lambda m: self.fake("tool use id", m[0], lambda n: (
            "toolu_01FIXTURE" + str(n).rjust(len(m[0]) - 15, "0"))), value)
        value = HEX_RUN.sub(self._hex, value)
        for kind, pattern in SECRETS:
            value = pattern.sub(lambda m, kind=kind: self.fake(kind, m[0], lambda n: f"<scrubbed-{kind}-{n}>"), value)
        for host in self.hosts:
            if host in value:
                value = value.replace(host, self.fake("host name", host, lambda n: f"fixture-host-{n}"))
        return value

    def _hex(self, match):
        """A commit SHA, whole or abbreviated, or the hex form of a trace or span id."""
        run = match[0].lower()
        for commit in sorted(self.commits):
            if commit.startswith(run):
                return self._commit(commit)[:len(run)]
        return self._identifier(run) if run in self.identifiers else run

    def real_values(self):
        """Every real value the table replaced, as the bytes to look for in the written files."""
        found = []
        for kind, fakes in self.fakes.items():
            for real in fakes:
                found.append((kind, real.encode()))
                if kind in ("trace id", "span id"):
                    found.append((kind, bytes.fromhex(real)))
        for real, _ in self.paths:
            found.append(("path", real.encode()))
        found += [("host name", host.encode()) for host in self.hosts]
        return [(kind, real) for kind, real in found if len(real) >= 6]


def walk(message, text, identifier):
    """Applies text to every string and identifier to every trace and span id inside message."""
    for field, value in message.ListFields():
        repeated = field.is_repeated if hasattr(field, "is_repeated") else field.label == FieldDescriptor.LABEL_REPEATED
        if field.type == FieldDescriptor.TYPE_MESSAGE:
            for item in (value if repeated else [value]):
                walk(item, text, identifier)
        elif field.type == FieldDescriptor.TYPE_STRING:
            if repeated:
                value[:] = [text(item) for item in value]
            else:
                setattr(message, field.name, text(value))
        elif field.type == FieldDescriptor.TYPE_BYTES:
            if field.name not in ID_FIELDS:
                raise SystemExit(f"unexpected bytes field {field.full_name}: decide how to scrub it")
            setattr(message, field.name, identifier(value))


def attributes(message, found):
    """Collects every (key, string value) pair, also those inside the JSON text of an attribute."""
    for field, value in message.ListFields():
        if field.type != FieldDescriptor.TYPE_MESSAGE:
            continue
        repeated = field.is_repeated if hasattr(field, "is_repeated") else field.label == FieldDescriptor.LABEL_REPEATED
        for item in (value if repeated else [value]):
            if item.DESCRIPTOR.name == "KeyValue" and item.value.WhichOneof("value") == "string_value":
                found.append((item.key, item.value.string_value))
                if item.value.string_value.startswith("{"):
                    try:
                        inner = json.loads(item.value.string_value)
                    except ValueError:
                        inner = {}
                    found.extend((k, v) for k, v in inner.items() if isinstance(v, str))
            attributes(item, found)


def shape(message):
    """What scrubbing must keep: the fields that are set, attribute keys, numbers and the lengths of ids."""
    parts = []
    for field, value in message.ListFields():
        repeated = field.is_repeated if hasattr(field, "is_repeated") else field.label == FieldDescriptor.LABEL_REPEATED
        for item in (value if repeated else [value]):
            if field.type == FieldDescriptor.TYPE_MESSAGE:
                parts.append((field.name, shape(item)))
            elif field.type == FieldDescriptor.TYPE_BYTES:
                parts.append((field.name, len(item)))
            elif field.type == FieldDescriptor.TYPE_STRING:
                parts.append((field.name, item if field.name in ("key", "name", "schema_url", "version") else None))
            else:
                parts.append((field.name, item))
    return parts


def session_of(message):
    found = []
    attributes(message, found)
    sessions = {value for key, value in found if key == "session.id"}
    if len(sessions) != 1:
        raise SystemExit(f"a body belongs to {len(sessions)} sessions, expected exactly one")
    return sessions.pop()


def otlp_json(message):
    """OTLP/JSON: lowerCamelCase names, hex trace and span ids, enums as integers, 64-bit integers as strings."""

    def hexed(node):
        if isinstance(node, dict):
            return {k: base64.b64decode(v).hex() if k in ID_JSON_KEYS else hexed(v) for k, v in node.items()}
        return [hexed(item) for item in node] if isinstance(node, list) else node

    return json.dumps(hexed(json_format.MessageToDict(message, use_integers_for_enums=True)), indent=2) + "\n"


def from_otlp_json(text, request):
    def based(node):
        if isinstance(node, dict):
            return {k: base64.b64encode(bytes.fromhex(v)).decode() if k in ID_JSON_KEYS else based(v)
                    for k, v in node.items()}
        return [based(item) for item in node] if isinstance(node, list) else node

    return json_format.ParseDict(based(json.loads(text)), request())


def main():
    if len(sys.argv) not in (2, 3):
        raise SystemExit(__doc__)
    raw = os.path.abspath(sys.argv[1])
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    default = os.path.join(root, "tests", "Nami.IntegrationTests", "Fixtures", "ClaudeCodeOtlp")
    fixture = os.path.realpath(sys.argv[2]) if len(sys.argv) == 3 else default
    environment = json.load(open(os.path.join(raw, "environment.json")))
    # The folders in the order they are scrubbed, each with its session labels in the order of arrival.
    sets = {"not-signed-in": ["A", "B"], "signed-in": environment["signedInSessions"]}
    if sets["signed-in"] != FIXTURE_SESSIONS and fixture == os.path.realpath(default):
        raise SystemExit(f"this capture holds the sessions {' '.join(sets['signed-in'])}; the fixture in the "
                         f"repository holds {' '.join(FIXTURE_SESSIONS)}. Name another folder as the second argument")
    table = Table(environment)

    # Read everything first, so that an id named by its key in one body is known in every body.
    bodies = {}
    for name in sets:
        bodies[name] = []
        for file in sorted(f for f in os.listdir(os.path.join(raw, name)) if f.endswith(".bin")):
            stem = file.removesuffix(".bin")
            signal = stem.split("-", 1)[1]
            original = REQUESTS[signal]()
            original.ParseFromString(open(os.path.join(raw, name, file), "rb").read())
            found = []
            attributes(original, found)
            for key, value in found:
                table.learn(key, value)
            walk(original, lambda value: value, table.learn_identifier)
            meta = json.load(open(os.path.join(raw, name, stem + ".meta.json")))
            bodies[name].append((stem, signal, original, meta))

    staging = tempfile.mkdtemp(prefix="nami-scrub.")
    try:
        summary, failures, written = write_and_check(raw, staging, table, bodies, sets)
        if not failures:
            for name in sets:
                directory = os.path.join(fixture, name)
                os.makedirs(directory, exist_ok=True)
                for old in os.listdir(directory):
                    if re.fullmatch(r"\d{5}-(logs|metrics|traces)\.(pb|json)|manifest\.json|cli-results\.json", old):
                        os.remove(os.path.join(directory, old))
                for file in os.listdir(os.path.join(staging, name)):
                    shutil.copyfile(os.path.join(staging, name, file), os.path.join(directory, file))
    finally:
        shutil.rmtree(staging)

    versions = {open(os.path.join(raw, name, "claude-version.txt")).read().strip() for name in sets}
    first = min(meta["received_at"] for name in sets for _, _, _, meta in bodies[name])
    print(f"Claude Code {', '.join(sorted(versions))}, first body received {first}")
    for name, counts in summary.items():
        print(f"{name}: {sum(counts.values())} bodies ({counts['logs']} logs, {counts['metrics']} metrics, "
              f"{counts['traces']} traces)")
    print("replaced: " + ", ".join(f"{len(fakes)} {kind}" for kind, fakes in sorted(table.fakes.items())))
    if failures:
        print("\n".join(sorted(set(failures))))
        print(f"{fixture} was not changed")
        raise SystemExit(1)
    print(f"checked {written} files: no real value, no name, no forbidden pattern, every .pb equals its .json")
    print(f"wrote them to {fixture}")


def write_and_check(raw, staging, table, bodies, sets):
    """Writes the scrubbed fixture into staging and returns its counts, what the checks found, and the file count."""
    written = []
    summary = {}
    for name, labels in sets.items():
        directory = os.path.join(staging, name)
        os.makedirs(directory)
        sessions = {}
        captures = []
        for stem, signal, original, meta in bodies[name]:
            real_session = session_of(original)
            scrubbed = REQUESTS[signal]()
            scrubbed.CopyFrom(original)
            walk(scrubbed, table.text, table.identifier)
            if shape(scrubbed) != shape(original):
                raise SystemExit(f"{name}/{stem}: scrubbing changed the structure of the body")
            if real_session not in sessions:
                if len(sessions) == len(labels):
                    raise SystemExit(f"{name}: more sessions than the labels {labels}")
                sessions[real_session] = labels[len(sessions)]
            payload = scrubbed.SerializeToString(deterministic=True)
            text = otlp_json(scrubbed)
            if from_otlp_json(text, REQUESTS[signal]) != scrubbed:
                raise SystemExit(f"{name}/{stem}: the JSON form does not decode to the protobuf form")
            for extension, content in ((".pb", payload), (".json", text.encode())):
                path = os.path.join(directory, stem + extension)
                open(path, "wb").write(content)
                written.append(path)
            captures.append({
                "file": stem + ".pb", "json": stem + ".json", "signal": signal, "session": sessions[real_session],
                "sessionId": session_of(scrubbed), "path": meta["path"], "contentType": meta["content_type"],
                "contentEncoding": meta["content_encoding"], "userAgent": meta["user_agent"],
                "receivedAt": meta["received_at"], "receivedAtUnixNano": str(meta["received_at_unix_nano"]),
                "bytes": len(payload), "sha256": hashlib.sha256(payload).hexdigest()})
        if len(sessions) != len(labels):
            raise SystemExit(f"{name}: {len(sessions)} sessions, expected the {len(labels)} of {labels}")
        path = os.path.join(directory, "manifest.json")
        open(path, "w").write(json.dumps({"captures": captures}, indent=2) + "\n")
        written.append(path)
        summary[name] = collections.Counter(capture["signal"] for capture in captures)

        results = []
        for real_session, label in sessions.items() if name == "signed-in" else []:
            result = json.load(open(os.path.join(raw, name, label + ".stdout.json")))
            if result["session_id"] != real_session:
                raise SystemExit(f"{label}: the CLI's session id is not the session id of its telemetry")
            results.append({
                "session": label, "sessionId": table.text(real_session), "subtype": result["subtype"],
                "isError": result["is_error"], "terminalReason": result["terminal_reason"],
                "numTurns": result["num_turns"], "durationMs": result["duration_ms"],
                "durationApiMs": result["duration_api_ms"], "totalCostUsd": round(result["total_cost_usd"], 8),
                "subagentsSpawned": result["subagent_stats"]["spawned"],
                "modelUsage": {model: {
                    "inputTokens": usage["inputTokens"], "outputTokens": usage["outputTokens"],
                    "cacheReadInputTokens": usage["cacheReadInputTokens"],
                    "cacheCreationInputTokens": usage["cacheCreationInputTokens"],
                    "costUSD": round(usage["costUSD"], 8)} for model, usage in result["modelUsage"].items()}})
        if results:
            path = os.path.join(directory, "cli-results.json")
            open(path, "w").write(json.dumps({
                "source": "claude -p --output-format json, final result object; result text and other ids omitted",
                "sessions": results}, indent=2) + "\n")
            written.append(path)

    failures = []
    real_values = table.real_values()
    for path in written:
        content = open(path, "rb").read()
        where = os.path.relpath(path, staging)
        for kind, real in real_values:
            if real in content:
                failures.append(f"{where}: a real {kind} is left")
        for name in table.names:
            if name.encode() in content.lower():
                failures.append(f"{where}: a name from environment.json is left")
        strings = [content]
        if path.endswith("manifest.json"):
            strings = [re.sub(rb'"sha256": "[0-9a-f]{64}"', b'"sha256": ""', content)]
        if path.endswith(".pb"):
            # A pattern is looked for in the strings of a protobuf body, not in its length prefixes and numbers.
            signal = os.path.basename(path).removesuffix(".pb").split("-", 1)[1]
            decoded = REQUESTS[signal]()
            decoded.ParseFromString(content)
            if from_otlp_json(open(path.removesuffix(".pb") + ".json").read(), REQUESTS[signal]) != decoded:
                failures.append(f"{where}: does not decode to its .json")
            strings = []
            walk(decoded, lambda value: strings.append(value.encode()) or value, lambda value: value)
        for what, pattern in FORBIDDEN:
            if any(pattern.search(text) for text in strings):
                failures.append(f"{where}: {what} is left")
    return summary, failures, len(written)


if __name__ == "__main__":
    main()
