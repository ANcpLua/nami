#!/usr/bin/env bash

set -eo pipefail
SCRIPT_DIR=$(cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd)

# Quiet unless --verbose or NAMI_VERBOSE=1: eng/scripts/quiet.sh prints the target table, PASS or
# FAIL and the failing target's last lines, and keeps the whole output in
# artifacts/logs/build-<targets>.log. --help and the targets whose output is the point
# (Print) print as before.
verbose=${NAMI_VERBOSE:-0}
arguments=()
name=build
for argument in "$@"; do
    case $argument in
        --verbose) verbose=1; continue ;;
        --help | -h | Print) verbose=1 ;;
        -*) parameters=1 ;;
        *) [[ -n ${parameters:-} ]] || name+="-$argument" ;;
    esac
    arguments+=("$argument")
done
if [[ $verbose != 1 ]]; then
    exec "$SCRIPT_DIR/scripts/quiet.sh" "${name//[^A-Za-z0-9._-]/_}" -- "$SCRIPT_DIR/build.sh" ${arguments[@]+"${arguments[@]}"}
fi
set -- ${arguments[@]+"${arguments[@]}"}

bash --version 2>&1 | head -n 1

BUILD_PROJECT_FILE="$SCRIPT_DIR/build/build.csproj"
TEMP_DIRECTORY="$SCRIPT_DIR/../.fallout/temp"

DOTNET_GLOBAL_FILE="$SCRIPT_DIR/../global.json"
DOTNET_INSTALL_URL="https://dot.net/v1/dotnet-install.sh"
DOTNET_CHANNEL="STS"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

function FirstJsonValue {
    perl -nle 'print $1 if m{"'"$1"'": "([^"]+)",?}' <<< "${@:2}"
}

if [ -x "$(command -v dotnet)" ] && dotnet --version &>/dev/null; then
    DOTNET_EXE="$(command -v dotnet)"
    # Git Bash on Windows resolves dotnet.exe without its extension, and Fallout rejects a DOTNET_EXE that does not exist.
    if [[ -f "$DOTNET_EXE.exe" ]]; then
        DOTNET_EXE="$DOTNET_EXE.exe"
    fi
    export DOTNET_EXE
else
    DOTNET_INSTALL_FILE="$TEMP_DIRECTORY/dotnet-install.sh"
    mkdir -p "$TEMP_DIRECTORY"
    curl -Lsfo "$DOTNET_INSTALL_FILE" "$DOTNET_INSTALL_URL"
    chmod +x "$DOTNET_INSTALL_FILE"

    if [[ -f "$DOTNET_GLOBAL_FILE" ]]; then
        DOTNET_VERSION=$(FirstJsonValue "version" "$(cat "$DOTNET_GLOBAL_FILE")")
        if [[ "$DOTNET_VERSION" == ""  ]]; then
            unset DOTNET_VERSION
        fi
    fi

    DOTNET_DIRECTORY="$TEMP_DIRECTORY/dotnet-unix"
    if [[ -z ${DOTNET_VERSION+x} ]]; then
        "$DOTNET_INSTALL_FILE" --install-dir "$DOTNET_DIRECTORY" --channel "$DOTNET_CHANNEL" --no-path
    else
        "$DOTNET_INSTALL_FILE" --install-dir "$DOTNET_DIRECTORY" --version "$DOTNET_VERSION" --no-path
    fi
    export DOTNET_EXE="$DOTNET_DIRECTORY/dotnet"
    export PATH="$DOTNET_DIRECTORY:$PATH"
fi

echo "Microsoft (R) .NET SDK version $("$DOTNET_EXE" --version)"

# Dash-form switches: Git Bash on the Windows release legs rewrites an argument that starts with / as a path.
"$DOTNET_EXE" build "$BUILD_PROJECT_FILE" -nodeReuse:false -p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet
"$DOTNET_EXE" run --project "$BUILD_PROJECT_FILE" --no-build -- "$@"
