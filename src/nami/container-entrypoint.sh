#!/bin/sh
set -eu

chown nami:nami /data
exec setpriv --reuid=nami --regid=nami --init-groups /app/nami.collector "$@"
