#!/usr/bin/env bash
# Shared by repository entry points; keep builds and tool thread pools laptop-friendly.
NAMI_BUILD_WORKERS=${NAMI_BUILD_WORKERS:-2}
if [[ ! $NAMI_BUILD_WORKERS =~ ^[1-9][0-9]*$ ]]; then
    echo "NAMI_BUILD_WORKERS must be a positive integer." >&2
    exit 2
fi
export NAMI_BUILD_WORKERS
export DOTNET_PROCESSOR_COUNT=$NAMI_BUILD_WORKERS
export GOMAXPROCS=$NAMI_BUILD_WORKERS
