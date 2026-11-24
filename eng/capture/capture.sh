#!/bin/bash
# Records the OpenTelemetry that Claude Code sends during short `claude -p` sessions, as raw
# material for tests/Nami.IntegrationTests/Fixtures/ClaudeCodeOtlp (its README says how the
# fixture is re-recorded):
#
#   bash eng/capture/capture.sh <out-dir>
#   SESSIONS="s4 s5 s6" bash eng/capture/capture.sh <out-dir>
#
# <out-dir> must lie outside every git repository: the raw bodies hold the account's e-mail
# address and ids. eng/capture/scrub.py turns them into the fixture; delete <out-dir> afterwards.
#
#   <out-dir>/not-signed-in   sessions A and B, started with an empty CLAUDE_CONFIG_DIR, so they
#                             never reach the API (no cost). B inherits a TRACEPARENT.
#   <out-dir>/signed-in       the sessions named in SESSIONS on the signed-in account, in that order
#                             in one repository (Haiku, about one cent). The default, s1 s2 s3, is
#                             the fixture. s4 to s7 are further cases; the fixture's README says
#                             what each shows.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
if [ $# -ne 1 ]; then echo "usage: capture.sh <out-dir outside any git repository>"; exit 2; fi
mkdir -p "$1" || exit 1
out="$(cd "$1" && pwd)"
if git -C "$out" rev-parse --is-inside-work-tree > /dev/null 2>&1; then
  echo "$out is inside a git repository; the raw bodies must stay outside"; exit 2
fi
if [ -n "$(ls -A "$out")" ]; then
  echo "$out is not empty; a capture needs a folder of its own"; exit 2
fi
claude_bin="$(command -v claude || true)"
py="$(command -v python3 || true)"
if [ -z "$claude_bin" ]; then echo "claude is not on PATH"; exit 1; fi
if [ -z "$py" ]; then echo "python3 is not on PATH"; exit 1; fi
port=4399
sessions="${SESSIONS:-s1 s2 s3}"
for label in $sessions; do
  if [ ! -f "$here/prompts/$label.txt" ]; then echo "no prompt eng/capture/prompts/$label.txt"; exit 2; fi
done

work="$(mktemp -d /tmp/nami-capture.XXXXXX)"
sink_pid=""
stop_sink() {
  if [ -n "$sink_pid" ]; then kill "$sink_pid" 2>/dev/null; wait "$sink_pid" 2>/dev/null; sink_pid=""; fi
}
cleanup() {
  stop_sink
  # The signed-in sessions keep their transcripts under the name Claude Code derives from the repository's path.
  transcripts="$HOME/.claude/projects/$(cd "$work/repo" 2>/dev/null && pwd -P | sed 's/[^A-Za-z0-9]/-/g')"
  case "$transcripts" in
    */.claude/projects/*-nami-capture-*-repo) rm -rf "$transcripts" ;;
  esac
  rm -rf "$work"
}
trap cleanup EXIT

# What scrub.py replaces besides the ids it finds in the telemetry: the repository's path, the
# temporary directory, the home directory and the machine's names. The names of the person are
# recorded too: scrub.py refuses a fixture that holds one.
mkdir -p "$work/repo" "$work/empty-config"
"$py" - "$out/environment.json" "$work/repo" $sessions <<'PY'
import getpass, json, os, pwd, socket, subprocess, sys, tempfile
git_name = subprocess.run(["git", "config", "--global", "user.name"], capture_output=True, text=True).stdout.strip()
full_name = pwd.getpwuid(os.getuid()).pw_gecos.split(",")[0].strip()
json.dump({"repository": os.path.realpath(sys.argv[2]), "repositoryAsGiven": sys.argv[2],
           "temporaryDirectory": os.path.realpath(tempfile.gettempdir()), "home": os.path.expanduser("~"),
           "signedInSessions": sys.argv[3:], "user": getpass.getuser(),
           "names": sorted({name for name in (git_name, full_name) if name}),
           "hosts": sorted({socket.gethostname(), socket.gethostname().split(".")[0]})},
          open(sys.argv[1], "w"), indent=2)
PY

cd "$work/repo" || exit 1
git init -q
git config user.name "fixture"
git config user.email "fixture@example.invalid"
printf 'def add(a, b):\n    return a - b\n\n\ndef mul(a, b):\n    return a * b\n' > calc.py
cat > test_calc.py <<'PY'
import unittest

from calc import add, mul


class CalcTests(unittest.TestCase):
    def test_add(self):
        self.assertEqual(add(2, 3), 5)

    def test_mul(self):
        self.assertEqual(mul(2, 3), 6)


if __name__ == "__main__":
    unittest.main()
PY
printf '# calc\nA tiny repository for a telemetry capture.\n' > README.md
# nami's claim check recognises the gate by its exact command, so the repository has one.
mkdir -p eng
printf '#!/bin/sh\nexec python3 -m unittest -q "$@"\n' > eng/test.sh
chmod +x eng/test.sh
git add -A && git commit -q -m "Start the calc fixture"

start_sink() {
  mkdir -p "$out/$1"
  "$claude_bin" --version > "$out/$1/claude-version.txt" 2>&1 || true
  SINK_PORT=$port SINK_OUT="$out/$1" "$py" "$here/sink.py" 2> "$out/$1/sink.log" &
  sink_pid=$!
  sleep 1
}

# One session in a clean environment: only the variables named here reach Claude Code. The first
# argument is the folder, the second the session label and prompt, the rest are extra variables.
run() {
  set_name="$1"; label="$2"; prompt="$3"; shift 3
  more_tools=""
  if [ "$prompt" = s7 ]; then
    # s7 asks for a project skill. It exists only while s7 runs, so every other session starts
    # without it.
    mkdir -p .claude/skills/calc-summary
    printf '%s\n' '---' 'name: calc-summary' \
      'description: Summarizes calc.py in one line. Use when asked for the calc summary.' '---' '' \
      'Read calc.py with the Read tool. Reply with one line that names the functions it defines, nothing else.' \
      > .claude/skills/calc-summary/SKILL.md
    more_tools="Skill"
  fi
  echo "Session $label ..."
  env -i HOME="$HOME" USER="$USER" LOGNAME="$USER" SHELL=/bin/sh TERM=xterm-256color LANG=C.UTF-8 \
      TMPDIR="${TMPDIR:-/tmp}" PATH="$(dirname "$claude_bin"):$(dirname "$py"):/usr/bin:/bin:/usr/sbin:/sbin" \
    DISABLE_AUTOUPDATER=1 \
    CLAUDE_CODE_ENABLE_TELEMETRY=1 \
    OTEL_METRICS_EXPORTER=otlp OTEL_LOGS_EXPORTER=otlp OTEL_TRACES_EXPORTER=otlp \
    CLAUDE_CODE_ENHANCED_TELEMETRY_BETA=1 \
    OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf \
    OTEL_EXPORTER_OTLP_ENDPOINT="http://127.0.0.1:$port" \
    OTEL_METRIC_EXPORT_INTERVAL=2000 OTEL_LOGS_EXPORT_INTERVAL=1000 OTEL_TRACES_EXPORT_INTERVAL=1000 \
    OTEL_LOG_TOOL_DETAILS=1 \
    OTEL_RESOURCE_ATTRIBUTES=gen_ai.agent.name=fixture-builder \
    "$@" \
    "$claude_bin" -p "$(cat "$here/prompts/$prompt.txt")" --model haiku \
      --setting-sources project --strict-mcp-config \
      --max-budget-usd 1.00 --max-turns 15 --output-format json \
      --allowedTools "Bash(python3 -m unittest:*)" "Bash(./eng/test.sh:*)" "Bash(git commit:*)" \
        Read Edit Task Agent BashOutput $more_tools \
      > "$out/$set_name/$label.stdout.json" 2> "$out/$set_name/$label.stderr.txt" < /dev/null
  echo "  exit $?"
  if [ "$prompt" = s7 ]; then rm -rf .claude; fi
  sleep 4
}

# Without a credential first: these sessions change nothing in the repository.
start_sink not-signed-in
run not-signed-in A s1 CLAUDE_CONFIG_DIR="$work/empty-config"
run not-signed-in B s1 CLAUDE_CONFIG_DIR="$work/empty-config" \
  TRACEPARENT=00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01
sleep 6
stop_sink

start_sink signed-in
for label in $sessions; do
  run signed-in "$label" "$label"
done
sleep 6
stop_sink

for set_name in not-signed-in signed-in; do
  n=$(find "$out/$set_name" -name '*.bin' | wc -l | tr -d ' ')
  echo "$set_name: $n telemetry bodies"
done
if [ "$sessions" = "s1 s2 s3" ]; then
  echo "Raw capture in $out. Next: uv run eng/capture/scrub.py $out"
else
  echo "Raw capture in $out. Next: uv run eng/capture/scrub.py $out <folder outside the fixture>"
fi
