#!/usr/bin/env bash
# One run on a machine with Docker Desktop (macOS, or Windows from a WSL2 shell): builds the image for this
# machine and runs the container smoke test with the ready manifest on a bind mount. Beyond what CI proves on
# Linux this checks Docker Desktop's two layers: port forwarding from the host's 127.0.0.1 into the VM (every
# request of the smoke test) and the file-sharing layer (the manifest appears whole and disappears on stop).
#   eng/scripts/docker_desktop_check.sh
set -euo pipefail
docker info --format 'Docker: {{.OperatingSystem}}, {{.Architecture}}, server {{.ServerVersion}}'
exec "$(dirname "${BASH_SOURCE[0]}")/container_smoke.sh" --bind-ready
