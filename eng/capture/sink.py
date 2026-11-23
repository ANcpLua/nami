"""Minimal OTLP/HTTP sink (standard library only) for one telemetry capture.

Saves the raw body of every POST to /v1/metrics, /v1/logs and /v1/traces as
<seq>-<signal>.bin with a sidecar <seq>-<signal>.meta.json, and answers 200
with an empty export response. Port and folder come from SINK_PORT and SINK_OUT.
"""
import datetime, hashlib, http.server, json, os, sys, threading, time

HOST = "127.0.0.1"
PORT = int(os.environ.get("SINK_PORT", "4399"))
OUT = os.environ["SINK_OUT"]
SIGNALS = {"/v1/metrics": "metrics", "/v1/logs": "logs", "/v1/traces": "traces"}
_lock = threading.Lock()
_seq = 0


def next_seq():
    global _seq
    with _lock:
        _seq += 1
        return _seq


class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        sys.stderr.write("%s %s\n" % (datetime.datetime.now(datetime.timezone.utc).isoformat(), fmt % args))

    def _read_body(self):
        if "chunked" in (self.headers.get("Transfer-Encoding") or "").lower():
            parts = []
            while True:
                size = int(self.rfile.readline().split(b";", 1)[0].strip(), 16)
                if size == 0:
                    while self.rfile.readline() not in (b"\r\n", b"\n", b""):
                        pass
                    break
                parts.append(self.rfile.read(size))
                self.rfile.readline()
            return b"".join(parts)
        return self.rfile.read(int(self.headers.get("Content-Length") or 0))

    def _reply(self, code, body=b"", content_type="text/plain"):
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if body:
            self.wfile.write(body)

    def do_GET(self):
        self._reply(200, b"ok\n")

    def do_POST(self):
        arrival_ns = time.time_ns()
        path = self.path.split("?", 1)[0]
        body = self._read_body()
        signal = SIGNALS.get(path)
        if signal is None:
            self._reply(404, b"unknown path\n")
            return
        seq = next_seq()
        stem = os.path.join(OUT, "%05d-%s" % (seq, signal))
        with open(stem + ".bin", "wb") as f:
            f.write(body)
        meta = {
            "seq": seq, "signal": signal, "path": path,
            "received_at": datetime.datetime.fromtimestamp(arrival_ns / 1e9, datetime.timezone.utc).isoformat(),
            "received_at_unix_nano": arrival_ns,
            "content_type": self.headers.get("Content-Type"),
            "content_encoding": self.headers.get("Content-Encoding"),
            "transfer_encoding": self.headers.get("Transfer-Encoding"),
            "user_agent": self.headers.get("User-Agent"),
            "body_bytes": len(body),
            "body_sha256": hashlib.sha256(body).hexdigest(),
        }
        with open(stem + ".meta.json", "w") as f:
            json.dump(meta, f, indent=2)
            f.write("\n")
        if "json" in (self.headers.get("Content-Type") or "").lower():
            self._reply(200, b"{}", "application/json")
        else:
            self._reply(200, b"", "application/x-protobuf")


def main():
    os.makedirs(OUT, exist_ok=True)
    server = http.server.ThreadingHTTPServer((HOST, PORT), Handler)
    sys.stderr.write("listening on http://%s:%d\n" % (HOST, PORT))
    sys.stderr.flush()
    server.serve_forever()


if __name__ == "__main__":
    main()
