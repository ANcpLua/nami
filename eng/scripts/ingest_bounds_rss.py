#!/usr/bin/env python3
"""What one export request at the bounds of IngestLimits costs the engine end to end: parse, convert and store.

For every composition and N: a fresh engine process with an empty store, one small export of the same signal to
initialize the path, idle, baseline VmHWM; then one request of exactly N protobuf messages over OTLP/HTTP protobuf
from each client at the same moment, idle again, peak VmHWM. Stored rows are the records sent minus what the
responses' partial_success rejected; a request the engine refuses (413) or turns away (503) stores nothing.

The compositions: empty and minimal valid spans, empty log records, number data points (all at one time, which
the store keeps as one observation) and distinct ones (each at its own time, each stored), empty ResourceSpans and
ScopeSpans wrappers, one span of small attributes, spans of the attribute density Claude Code sends, a mix, and a
fixed number of spans that fill N messages between them (the worst case inside both the record and the message
bound). The defaults of IngestLimits.MaxRequestRecords and MaxConcurrentRequests were chosen from this script's
numbers on 16 Nov 2026.

Like a3_rss.py this is a measurement, not a gate: run it on a quiet machine against a Release build. An engine
whose RSS passes --rss-cap-mib is stopped and reported as such, so a size that does not fit the machine does not
take it down.

usage: ingest_bounds_rss.py --engine <nami.dll | native nami binary> --messages N[,N...]
                            [--compositions name;name...] [--runs 1] [--clients 1] [--jsonl out.jsonl]
"""
import argparse, json, os, struct, sys, threading, time, urllib.request, urllib.error

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import a3_rss as a3  # noqa: E402  the engine process, the idle wait and the protobuf encoder

ld, text, varint, fixed64 = a3.ld, a3.text, a3.varint, a3.fixed64
NOW = time.time_ns()
RSS_CAP_KB = 1536 * 1024
SHIFT = 0   # set per client, so that concurrent clients send different spans and distinct data points

def span(i, attrs=0, extra=b""):
    i += SHIFT
    body = ld(1, (0x1000000000000000000000000000 + i).to_bytes(16, "big")) + ld(2, (1 + i).to_bytes(8, "big"))
    body += text(5, "s") + fixed64(7, NOW - 10**9) + fixed64(8, NOW)
    for a in range(attrs):
        body += ld(9, text(1, "k%d" % a) + ld(2, varint(3 << 3) + varint(a)))      # KeyValue + AnyValue(int)
    return body + extra

def traces(spans_bytes):                                     # request + ResourceSpans + ScopeSpans = 3 messages
    return ld(1, ld(2, b"".join(ld(2, s) for s in spans_bytes)))

def compositions(n):
    """name -> (signal, payload, records sent). Each payload holds exactly n messages."""
    point = fixed64(3, NOW) + varint(6 << 3 | 1) + struct.pack("<q", 7)             # NumberDataPoint: time, as_int
    def point_at(i):                                                                # its own time: its own stored point
        return fixed64(3, NOW + SHIFT + i) + varint(6 << 3 | 1) + struct.pack("<q", 7)
    per_mixed = 1 + 2 * 8 + 1 + 1 + 2                                               # span, 8 attributes, status, event, its attribute
    mixed = (n - 3) // per_mixed
    event = ld(11, text(2, "e") + ld(3, text(1, "ek") + ld(2, text(1, "v"))))        # Event{name, attributes[KeyValue{AnyValue}]}
    filler = (n - 3) - mixed * per_mixed                                            # messages left: spans without anything
    like = (n - 3) // 37                                                            # the 15 Nov fixture: 37 messages per record
    like_rest = (n - 3) - like * 37
    def filling(records):
        # records spans that fill n messages between them: as many attributes each as fit, the rest as bare members
        each = max(0, (n - 3 - records) // (2 * records))
        rest = (n - 3 - records) - 2 * each * records
        return ("traces", traces([span(i, attrs=each, extra=ld(9, text(1, "odd")) * (rest if i == 0 else 0))
                                  for i in range(records)]), records)
    return {
        "2500 spans filling the messages": filling(2500),
        "3000 spans filling the messages": filling(3000),
        "3500 spans filling the messages": filling(3500),
        "fixture-like spans": ("traces", traces([span(i, attrs=18) for i in range(like)] +
                                                [span(like + i) for i in range(like_rest)]), like + like_rest),
        "empty spans": ("traces", ld(1, ld(2, ld(2, b"") * (n - 3))), n - 3),
        "minimal valid spans": ("traces", traces([span(i) for i in range(n - 3)]), n - 3),
        "empty log records": ("logs", ld(1, ld(2, ld(2, b"") * (n - 3))), n - 3),
        "number data points": ("metrics", ld(1, ld(2, ld(2, text(1, "m") + ld(5, ld(1, point) * (n - 5))))), n - 5),
        "distinct number data points": ("metrics", ld(1, ld(2, ld(2, text(1, "m") + ld(5, b"".join(
            ld(1, point_at(i)) for i in range(n - 5)))))), n - 5),
        "empty ResourceSpans": ("traces", ld(1, b"") * (n - 1), 0),
        "empty ScopeSpans": ("traces", ld(1, ld(2, b"") * (n - 2)), 0),
        "one span, small attributes": ("traces", traces([span(0, attrs=(n - 4) // 2,
                                        extra=ld(15, b"") if (n - 4) % 2 else b"")]), 1),
        "mix": ("traces", traces([span(i, attrs=8, extra=ld(15, b"") + event) for i in range(mixed)] +
                                 [span(mixed + i) for i in range(filler)]), mixed + filler),
    }

def post(engine, signal, payload):
    request = urllib.request.Request(f"http://127.0.0.1:{engine.http_port}/v1/{signal}", data=payload,
                                     headers={"Content-Type": "application/x-protobuf"})
    try:
        with urllib.request.urlopen(request, timeout=900) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()

def rejected(body):
    """partial_success (field 1) . rejected_* (field 1, varint) of an Export*ServiceResponse."""
    try:
        if not body or body[0] != 0x0A: return 0
        length, at = 0, 1
        length, at = a3_varint(body, at)
        inner = body[at:at + length]
        if inner[:1] != b"\x08": return 0
        return a3_varint(inner, 1)[0]
    except Exception:
        return -1

def a3_varint(data, at):
    value, shift = 0, 0
    while True:
        byte = data[at]; at += 1
        value |= (byte & 0x7F) << shift
        if byte < 0x80: return value, at
        shift += 7

def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--engine", required=True, help="nami.dll (run with dotnet) or the native nami binary")
    parser.add_argument("--messages", required=True, help="the sizes N, comma-separated")
    parser.add_argument("--compositions", default="", help="names separated by ';' (default: all)")
    parser.add_argument("--runs", type=int, default=1)
    parser.add_argument("--clients", type=int, default=1, help="clients that send the request at the same moment")
    parser.add_argument("--jsonl", default=os.devnull, help="append one JSON line per run")
    parser.add_argument("--rss-cap-mib", type=int, default=1536)
    args = parser.parse_args()
    global SHIFT, RSS_CAP_KB
    RSS_CAP_KB = args.rss_cap_mib * 1024
    dll, out = args.engine, args.jsonl
    sizes = [int(x) for x in args.messages.split(",")]
    wanted = args.compositions.split(";") if args.compositions else None
    runs, clients = args.runs, args.clients
    with open(out, "a") as log:
        for n in sizes:
            for name, (signal, payload, records) in compositions(n).items():
                if wanted and name not in wanted: continue
                for run in range(1, runs + 1):
                    engine = a3.Engine(dll, {})
                    try:
                        # A small export of the same signal; the compositions with a fixed number of spans have none.
                        warm_signal, warm_payload, _ = compositions(40)[name if "filling" not in name else "minimal valid spans"]
                        post(engine, warm_signal, warm_payload)
                        engine.wait_idle()
                        baseline, _ = engine.status()
                        # One payload per client, of different spans, all sent at the same moment.
                        payloads = []
                        for client in range(clients):
                            SHIFT = client * 10_000_000
                            payloads.append(compositions(n)[name][1])
                        SHIFT = 0
                        started = time.monotonic()
                        answers = [{} for _ in payloads]
                        def send(index):
                            sent = time.monotonic()
                            status, body = post(engine, signal, payloads[index])
                            answers[index].update(status=status, body=body, seconds=round(time.monotonic() - sent, 2))
                        senders = [threading.Thread(target=send, args=(index,), daemon=True) for index in range(clients)]
                        for sender in senders: sender.start()
                        # The machine is shared: an engine that grows past the cap is stopped and recorded as such.
                        while any(sender.is_alive() for sender in senders):
                            time.sleep(0.2)
                            if engine.status()[1] > RSS_CAP_KB:
                                engine.proc.kill()
                                raise RuntimeError("stopped: RSS passed %d MiB while the request was ingested (baseline %d MiB)"
                                                   % (RSS_CAP_KB // 1024, baseline // 1024))
                        status, body = answers[0].get("status", 0), answers[0].get("body", b"")
                        answered = time.monotonic() - started
                        settled = "idle"
                        calm, last, waited = 0, engine.cpu_ticks(), 0
                        while calm < 3 and waited < 600:
                            time.sleep(1.0); waited += 1
                            now = engine.cpu_ticks()
                            calm = calm + 1 if now - last <= a3.IDLE_TICKS_PER_SECOND else 0
                            last = now
                            if engine.status()[1] > RSS_CAP_KB:
                                engine.proc.kill()
                                raise RuntimeError("stopped: RSS passed %d MiB after the answer (baseline %d MiB, answered %d in %.1f s)"
                                                   % (RSS_CAP_KB // 1024, baseline // 1024, status, answered))
                        if waited >= 600: settled = "not idle after 600 s"
                        peak, after = engine.status()
                        row = {"n": n, "composition": name, "run": run, "signal": signal, "wire_bytes": len(payload),
                               "clients": clients, "statuses": [a.get("status") for a in answers],
                               "client_seconds": [a.get("seconds") for a in answers],
                               "status": status, "records_sent": records * clients,
                               "stored_rows": sum((records - rejected(a.get("body", b""))) for a in answers if a.get("status") == 200),
                               "baseline_mib": round(baseline / 1024), "peak_mib": round(peak / 1024),
                               "additional_mib": round((peak - baseline) / 1024), "answered_s": round(answered, 2),
                               "settled": settled}
                    except Exception as error:
                        row = {"n": n, "composition": name, "run": run, "error": repr(error)[:300],
                               "stderr": engine.stderr[-3:]}
                    finally:
                        engine.close()
                    log.write(json.dumps(row) + "\n"); log.flush()
                    print(json.dumps(row), flush=True)

if __name__ == "__main__":
    main()
