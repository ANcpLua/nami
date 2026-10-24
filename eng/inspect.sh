#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$script_dir/.." && pwd)
# shellcheck source=eng/scripts/limits.sh
source "$script_dir/scripts/limits.sh"
configuration=Release
build=1
project=
while (($#)); do
    case "$1" in
        --configuration)
            [[ $# -ge 2 && -n $2 ]] || { echo "--configuration requires a value" >&2; exit 2; }
            configuration=$2
            shift 2 ;;
        --no-build) build=0; shift ;;
        --project)
            [[ $# -ge 2 && -n $2 ]] || { echo "--project requires a project name or pattern" >&2; exit 2; }
            project=$2
            shift 2 ;;
        --help|-h)
            echo "Usage: eng/inspect.sh [--configuration Release|Debug] [--no-build] [--project NAME]"
            echo "Completed tool run: 0 = clean; 1 = findings; 2 = incomplete analysis. Failing build/tool exit codes are preserved."
            exit 0 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

# Use the repository's SDK through the installed host, including from a GUI's restricted PATH.
if [[ -n ${DOTNET_EXE:-} ]]; then
    dotnet_exe=$DOTNET_EXE
elif [[ $(uname -s) == Darwin && -x /usr/local/share/dotnet/dotnet ]]; then
    dotnet_exe=/usr/local/share/dotnet/dotnet
else
    dotnet_exe=$(command -v dotnet || true)
fi
[[ -n $dotnet_exe && -x $dotnet_exe ]] || { echo "The SDK from global.json is required; install the repository toolchain." >&2; exit 2; }
PATH="$(dirname "$dotnet_exe"):$PATH"
export PATH
jb_exe=$(command -v jb || true)
if [[ -z $jb_exe && -x $HOME/.dotnet/tools/jb ]]; then jb_exe=$HOME/.dotnet/tools/jb; fi
[[ -n $jb_exe ]] || { echo "JetBrains InspectCode (jb) is required; install it with the machine's dotnet-tools setup." >&2; exit 2; }
command -v python3 >/dev/null || { echo "Python 3 is required to validate the SARIF report." >&2; exit 2; }

cd "$root"
sdk_version=$("$dotnet_exe" --version)
mkdir -p artifacts/inspection
run_dir=$(mktemp -d "$root/artifacts/inspection/run.XXXXXX")
report=$run_dir/inspectcode.sarif
log=$run_dir/inspectcode.log
printf 'INSPECTCODE RUNNING: %s (%s, SDK %s, projects %s)\n' "$root/nami.slnx" "$configuration" "$sdk_version" "${project:-all}"
printf 'Report: %s\nLog: %s\n' "$report" "$log"

if ((build)); then
    set +e
    "$dotnet_exe" build "$root/nami.slnx" --configuration "$configuration" \
        "-maxcpucount:$NAMI_BUILD_WORKERS" -nodeReuse:false -p:UseSharedCompilation=false "-bl:$run_dir/build-{}.binlog" \
        2>&1 | tee "$run_dir/build.log"
    build_status=${PIPESTATUS[0]}
    set -e
    if ((build_status)); then
        printf 'INSPECTCODE NOT RUN: build exited with %d; log: %s\n' "$build_status" "$run_dir/build.log" >&2
        exit "$build_status"
    fi
fi

# Building is explicit above (or owned by the caller); InspectCode must not repeat it.
scope=()
if [[ -n $project ]]; then scope+=("--project=$project"); fi
set +e
"$jb_exe" inspectcode "$root/nami.slnx" --no-build --no-updates --swea \
    --severity=INFO --format=Sarif "--dotnetcoresdk=$sdk_version" \
    '--exclude=**/artifacts/**;**/obj/**;**/bin/**;**/*.g.cs;**/*.dll;**/.nuget/**;internal/nami.contracts/Nami/**;internal/nami.semconv/Generated/**' \
    "--caches-home=$root/artifacts/inspection/cache" ${scope[@]+"${scope[@]}"} \
    "--properties=Configuration=$configuration" "--output=$report" >"$log" 2>&1
inspection_status=$?
set -e
python3 "$script_dir/scripts/inspect_report.py" "$report" "$log" "$inspection_status"
