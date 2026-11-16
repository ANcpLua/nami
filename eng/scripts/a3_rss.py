#!/usr/bin/env python3
"""Engine audit A3 as a measurement: what one export costs the engine in memory.

The A3 export is one span whose single attribute is an array of four million empty AnyValues: 7.6 MiB as
protobuf, 11.4 MiB as JSON, well inside the 16 MiB request bound. On 15 Nov it took the engine from 138 MiB to
872 MiB peak RSS while it was ingested (audits/2026-11-15, engine-expansion.txt).

For each encoding (protobuf over HTTP, JSON over HTTP, gRPC) and each run this script starts the engine as its
own process with an empty store, sends one ordinary span to initialize the path, waits until the process is
idle, and reads VmHWM and VmRSS from /proc/<pid>/status: the baseline. Then it sends the A3 export, waits until
the process is idle again (the store commits after the response), and reads VmHWM again: the peak. Afterwards
it asks traces_get for the span and reports what was kept and what was counted as dropped.

It is a measurement, not a gate: RSS depends on the machine and on what else runs on it. Run it on a quiet
machine (in the agents' container: while holding the heavy lock) and never from CI's Check.

usage: a3_rss.py --engine <nami.dll | native nami binary> [--values 4000000] [--runs 3]
                 [--encodings protobuf,json,grpc] [--label text] [--json out.json] [--markdown out.md]
                 [--env KEY=VALUE ...] [--keep-payloads dir]
"""
import argparse
import json
import os
import queue
import shutil
import socket
import statistics
import struct
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request

TRACE_SERVICE = "opentelemetry.proto.collector.trace.v1.TraceService"
WIDE_TRACE = 0x4BF92F3577B34DA6A3CE929D0E0E4736
WARM_TRACE = 0x0AF7651916CD43DD8448EB211C80319C
WIDE_KEY = "gen_ai.input.messages"
SMALL_KEY = "acme.small"


# --- a minimal OTLP protobuf encoder (field numbers from opentelemetry-proto 1.x) ---------------------------

def varint(n):
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        if n:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)


def ld(field, payload):
    return varint(field << 3 | 2) + varint(len(payload)) + payload


def text(field, value):
    return ld(field, value.encode())


def fixed64(field, value):
    return varint(field << 3 | 1) + struct.pack("<Q", value)


def key_value(key, any_value):
    return text(1, key) + ld(2, any_value)


def span_pb(trace, span_id, start, end, name, attrs=()):
    body = ld(1, trace.to_bytes(16, "big")) + ld(2, span_id.to_bytes(8, "big"))
    body += text(5, name) + varint(6 << 3) + varint(2) + fixed64(7, start) + fixed64(8, end)
    return body + b"".join(ld(9, key_value(k, v)) for k, v in attrs)


def traces_pb(spans):
    resource = ld(1, key_value("service.name", text(1, "a3")))
    scope = ld(1, text(1, "a3-rss")) + b"".join(ld(2, s) for s in spans)
    return ld(1, ld(1, resource) + ld(2, scope))


def span_json(trace, span_id, start, end, name, attrs_json=""):
    return ('{"traceId":"%032x","spanId":"%016x","name":"%s","kind":2,"startTimeUnixNano":"%d",'
            '"endTimeUnixNano":"%d","attributes":[%s]}' % (trace, span_id, name, start, end, attrs_json))


def traces_json(spans):
    return ('{"resourceSpans":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"a3"}}]},'
            '"scopeSpans":[{"scope":{"name":"a3-rss"},"spans":[%s]}]}]}' % ",".join(spans)).encode()


def a3_exports(values, now):
    """The A3 export in both encodings: a span with the wide attribute and a small one beside it."""
    empties_pb = ld(5, ld(1, b"") * values)
    wide_pb = span_pb(WIDE_TRACE, 1, now - 10**9, now, "wide",
                      [(WIDE_KEY, empties_pb), (SMALL_KEY, text(1, "fits"))])
    empties_json = '{"key":"%s","value":{"arrayValue":{"values":[%s]}}}' % (WIDE_KEY, ",".join(["{}"] * values))
    small_json = '{"key":"%s","value":{"stringValue":"fits"}}' % SMALL_KEY
    wide_json = span_json(WIDE_TRACE, 1, now - 10**9, now, "wide", empties_json + "," + small_json)
    return traces_pb([wide_pb]), traces_json([wide_json])


def warm_exports(now):
    warm_pb = span_pb(WARM_TRACE, 2, now - 10**9, now, "warm", [(SMALL_KEY, text(1, "fits"))])
    warm_json = span_json(WARM_TRACE, 2, now - 10**9, now, "warm",
                          '{"key":"%s","value":{"stringValue":"fits"}}' % SMALL_KEY)
    return traces_pb([warm_pb]), traces_json([warm_json])


# --- the engine as its own process ----------------------------------------------------------------------

def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


class Engine:
    def __init__(self, binary, extra_env):
        self.work = tempfile.mkdtemp(prefix="nami-a3-")
        self.http_port, self.grpc_port = free_port(), free_port()
        env = dict(os.environ, NAMI_OTLP_AUTH_MODE="Unsecured", NAMI_OTLP_PORT=str(self.http_port),
                   NAMI_GRPC_PORT=str(self.grpc_port), NAMI_DATA_PATH=os.path.join(self.work, "nami.duckdb"),
                   NAMI_RETENTION_DAYS="0", DOTNET_ENVIRONMENT="Production", ASPNETCORE_ENVIRONMENT="Production",
                   DOTNET_gcServer="0")
        env.update(extra_env)
        command = [os.environ.get("DOTNET_EXE", "dotnet"), binary] if binary.endswith(".dll") else [binary]
        self.proc = subprocess.Popen(command + ["--engine"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                     stderr=subprocess.PIPE, env=env)
        self.frames = queue.Queue()
        self.stderr = []
        threading.Thread(target=self._out, daemon=True).start()
        threading.Thread(target=self._err, daemon=True).start()
        ready = self.frames.get(timeout=120)
        if ready is None:
            raise RuntimeError("the engine exited before it was ready: " + "\n".join(self.stderr[-10:]))
        self.revision = ready["revision"]
        self.seq = 0

    def _out(self):
        for line in self.proc.stdout:
            try:
                self.frames.put(json.loads(line))
            except ValueError:
                pass
        self.frames.put(None)

    def _err(self):
        for line in self.proc.stderr:
            self.stderr.append(line.decode("utf-8", "replace").rstrip())

    def status(self):
        fields = dict(line.split(":", 1) for line in open(f"/proc/{self.proc.pid}/status") if ":" in line)
        return int(fields["VmHWM"].split()[0]), int(fields["VmRSS"].split()[0])

    def cpu_ticks(self):
        stat = open(f"/proc/{self.proc.pid}/stat").read()
        fields = stat[stat.rindex(")") + 2:].split()
        return int(fields[11]) + int(fields[12])  # utime + stime

    def wait_idle(self, deadline=300.0, quiet=3):
        """Until the process has used (almost) no CPU for `quiet` consecutive seconds."""
        end = time.monotonic() + deadline
        calm, last = 0, self.cpu_ticks()
        while calm < quiet:
            if time.monotonic() > end:
                raise RuntimeError("the engine did not become idle")
            time.sleep(1.0)
            now = self.cpu_ticks()
            calm = calm + 1 if now - last <= 2 else 0
            last = now

    def post(self, payload, content_type):
        request = urllib.request.Request(f"http://127.0.0.1:{self.http_port}/v1/traces", data=payload,
                                         headers={"Content-Type": content_type})
        try:
            with urllib.request.urlopen(request, timeout=600) as response:
                return str(response.status)
        except urllib.error.HTTPError as error:
            return str(error.code)

    def grpc(self, message):
        """A unary gRPC Export with curl over cleartext HTTP/2; returns 'grpc <status>'."""
        frame = os.path.join(self.work, "frame.bin")
        with open(frame, "wb") as f:
            f.write(b"\x00" + struct.pack(">I", len(message)) + message)
        dump = os.path.join(self.work, "headers.txt")
        subprocess.run(["curl", "-sS", "--http2-prior-knowledge", "-X", "POST", "-H", "content-type: application/grpc",
                        "-H", "te: trailers", "--data-binary", "@" + frame, "-D", dump, "-o", os.devnull,
                        f"http://127.0.0.1:{self.grpc_port}/{TRACE_SERVICE}/Export"], check=True, timeout=600)
        status = "?"
        for line in open(dump, encoding="utf-8", errors="replace"):
            if line.lower().startswith("grpc-status:"):
                status = line.split(":", 1)[1].strip()
        return "grpc " + status

    def send(self, encoding, protobuf, as_json):
        if encoding == "protobuf":
            return self.post(protobuf, "application/x-protobuf")
        if encoding == "json":
            return self.post(as_json, "application/json")
        return self.grpc(protobuf)

    def call(self, name, args, timeout=600):
        self.seq += 1
        frame = {"id": str(self.seq), "revision": self.revision, "method": "call", "name": name, "input": args,
                 "credential": None, "traceparent": None, "tracestate": None}
        self.proc.stdin.write((json.dumps(frame) + "\n").encode())
        self.proc.stdin.flush()
        while True:
            reply = self.frames.get(timeout=timeout)
            if reply is None:
                raise RuntimeError("the engine exited: " + "\n".join(self.stderr[-10:]))
            if reply.get("id") == str(self.seq):
                return reply

    def close(self):
        if self.proc.poll() is None:
            try:
                self.proc.stdin.close()
                self.proc.wait(timeout=60)
            except (OSError, subprocess.TimeoutExpired):
                self.proc.kill()
                self.proc.wait()
        shutil.rmtree(self.work, ignore_errors=True)


def stored_span(engine):
    """What traces_get says about the wide span: its attribute keys and what was counted as dropped."""
    try:
        reply = engine.call("traces_get", {"trace_id": "%032x" % WIDE_TRACE})
    except (RuntimeError, queue.Empty) as error:
        return {"traces_get": "failed: %s" % error}
    if reply.get("is_error"):
        return {"traces_get": "error " + str(reply.get("result", {}).get("code"))}
    found = {"traces_get": "ok"}

    def walk(node):
        if isinstance(node, dict):
            if node.get("name") == "wide" or "dropped_attributes_count" in node or "attributes" in node:
                for name in ("dropped_attributes_count", "truncated_attributes_count"):
                    if name in node and name not in found:
                        found[name] = node[name]
                attributes = node.get("attributes")
                if isinstance(attributes, dict) and "attribute_keys" not in found:
                    found["attribute_keys"] = sorted(attributes)
                    wide = attributes.get(WIDE_KEY)
                    found["wide_kept"] = (len(wide) if isinstance(wide, (list, str)) else wide is not None) \
                        if WIDE_KEY in attributes else False
            for value in node.values():
                walk(value)
        elif isinstance(node, list):
            for value in node:
                walk(value)

    walk(reply.get("result"))
    return found


def measure(binary, encoding, protobuf, as_json, warm, extra_env):
    engine = Engine(binary, extra_env)
    try:
        warm_status = engine.send(encoding, *warm)
        engine.wait_idle()
        baseline_hwm, baseline_rss = engine.status()
        started = time.monotonic()
        status = engine.send(encoding, protobuf, as_json)
        answered = time.monotonic() - started
        engine.wait_idle()
        settled = time.monotonic() - started - 3  # wait_idle ends three quiet seconds after the work
        peak_hwm, rss_after = engine.status()
        stored = stored_span(engine)
        return {"encoding": encoding, "warm_up": warm_status, "status": status,
                "baseline_hwm_kb": baseline_hwm, "baseline_rss_kb": baseline_rss, "peak_hwm_kb": peak_hwm,
                "rss_after_kb": rss_after, "additional_peak_kb": peak_hwm - baseline_hwm,
                "answered_s": round(answered, 2), "settled_s": round(max(settled, answered), 2), "stored": stored,
                "stderr_tail": engine.stderr[-3:]}
    finally:
        engine.close()


def mib(kb):
    return "%.0f" % (kb / 1024)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--engine", required=True, help="nami.dll (run with dotnet) or the native nami binary")
    parser.add_argument("--values", type=int, default=4_000_000)
    parser.add_argument("--runs", type=int, default=3)
    parser.add_argument("--encodings", default="protobuf,json,grpc")
    parser.add_argument("--label", default="")
    parser.add_argument("--json")
    parser.add_argument("--markdown")
    parser.add_argument("--env", action="append", default=[], metavar="KEY=VALUE")
    parser.add_argument("--keep-payloads", metavar="DIR")
    args = parser.parse_args()

    now = time.time_ns()
    protobuf, as_json = a3_exports(args.values, now)
    warm = warm_exports(now)
    if args.keep_payloads:
        os.makedirs(args.keep_payloads, exist_ok=True)
        open(os.path.join(args.keep_payloads, "a3.pb"), "wb").write(protobuf)
        open(os.path.join(args.keep_payloads, "a3.json"), "wb").write(as_json)
    sizes = {"protobuf": len(protobuf), "json": len(as_json), "grpc": len(protobuf) + 5}
    extra_env = dict(item.split("=", 1) for item in args.env)

    results = []
    for encoding in args.encodings.split(","):
        for run in range(1, args.runs + 1):
            result = measure(args.engine, encoding, protobuf, as_json, warm, extra_env)
            result["run"] = run
            result["input_bytes"] = sizes[encoding]
            results.append(result)
            print("%-8s run %d: %s, baseline %s MiB, peak %s MiB, additional %s MiB, answered %.2fs, settled %.2fs, %s"
                  % (encoding, run, result["status"], mib(result["baseline_hwm_kb"]), mib(result["peak_hwm_kb"]),
                     mib(result["additional_peak_kb"]), result["answered_s"], result["settled_s"],
                     json.dumps(result["stored"], sort_keys=True)), flush=True)

    lines = ["| Encoding | Input | Status | Baseline RSS (VmHWM) | Peak RSS (VmHWM) | Additional peak | Kept / dropped | "
             "Answered | Settled |", "|---|---|---|---|---|---|---|---|---|"]
    for encoding in args.encodings.split(","):
        runs = [r for r in results if r["encoding"] == encoding]
        stored = runs[-1]["stored"]
        kept = "wide kept: %s, dropped_attributes_count: %s (%s)" % (
            stored.get("wide_kept", "?"), stored.get("dropped_attributes_count", "?"), stored.get("traces_get"))
        lines.append("| %s | %.2f MiB | %s | %s | %s | %s (max %s) | %s | %.2f s | %.2f s |" % (
            encoding, sizes[encoding] / 1048576, "/".join(sorted({r["status"] for r in runs})),
            " / ".join(mib(r["baseline_hwm_kb"]) for r in runs) + " MiB",
            " / ".join(mib(r["peak_hwm_kb"]) for r in runs) + " MiB",
            " / ".join(mib(r["additional_peak_kb"]) for r in runs) + " MiB",
            mib(max(r["additional_peak_kb"] for r in runs)) + " MiB", kept,
            statistics.median(r["answered_s"] for r in runs), statistics.median(r["settled_s"] for r in runs)))
    table = "\n".join(lines)
    print()
    print("%s: %d values, %d run(s) per encoding" % (args.label or args.engine, args.values, args.runs))
    print(table)
    if args.json:
        json.dump({"label": args.label, "engine": args.engine, "values": args.values, "results": results},
                  open(args.json, "w"), indent=1)
    if args.markdown:
        open(args.markdown, "w").write("%s: %d values, %d run(s) per encoding\n\n%s\n"
                                       % (args.label or args.engine, args.values, args.runs, table))
    return 0


if __name__ == "__main__":
    sys.exit(main())
