# Base images are pinned by the digest of their multi-architecture index: the same Dockerfile builds linux/amd64 and
# linux/arm64 natively (NativeAOT does not cross-compile here). Move a tag and its digest together.
FROM oven/bun:1.4.4@sha256:1d22548f177f4ff38e0c15baa3c2c46c6c2060ba2d1bc573bb4720b407548f1d AS mcp
WORKDIR /source
COPY src/nami.mcp/package.json src/nami.mcp/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/nami.mcp/ ./
RUN bun run build

FROM mcr.microsoft.com/dotnet/sdk:11.0.100@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea AS build
WORKDIR /source
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
COPY . .
RUN dotnet publish src/nami/nami.csproj -c Release -o /app -maxcpucount:2 -nodeReuse:false -p:UseSharedCompilation=false
RUN mkdir /symbols && cp /app/nami.dbg /symbols/
# The runtime image has no shell, so the data directory is made here and copied with its owner.
RUN mkdir /data

# The symbols of this build and nothing else, for whoever debugs a crash of the image's engine:
#   docker build -f src/nami/Dockerfile --target symbols --output type=local,dest=symbols .
# The container workflow archives them with every image it builds. The default target, the image, does not hold them.
FROM scratch AS symbols
COPY --from=build /symbols /

# Ubuntu 26.04 chiseled: glibc, libgcc and libstdc++ (all that the engine, Bun and libduckdb.so load), no shell, no
# package manager, user app (1654). Not -extra: the engine runs with InvariantGlobalization and reads no time zone.
FROM mcr.microsoft.com/dotnet/runtime-deps:11.0.0-resolute-chiseled@sha256:24534e1f18024d3f8026061537fa21ba424f46cc0cdab4297c5528937b1ae960
# /app and everything in it belong to root, so the process (user app) can read and run its engine and its bundle but
# not replace them. COPY makes the directory: WORKDIR would make it for the user this base image runs as, and the
# owner of a directory renames and removes what is in it.
COPY --from=build --chown=0:0 /app /app
COPY --from=mcp /usr/local/bin/bun /usr/local/bin/bun
COPY --from=mcp --chown=0:0 /source/dist/main.js /source/dist/healthcheck.js /app/mcp/
WORKDIR /app
# A fresh named volume mounted here takes this directory's owner, so the store is writable without root.
COPY --from=build --chown=1654:1654 /data /data
# ASPNETCORE_HTTP_PORTS: the base image sets 8080 for ASP.NET Core's default listener; the engine binds its own
# two, and Kestrel warned at every start that it overrides the address.
ENV NAMI_BIND_ADDRESS=0.0.0.0 NAMI_DATA_PATH=/data/nami.duckdb NODE_ENV=production BUN_RUNTIME_TRANSPILER_CACHE_PATH=0 \
    ASPNETCORE_HTTP_PORTS=
EXPOSE 4318 4317 5100
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --start-interval=2s --retries=3 \
  CMD ["/usr/local/bin/bun", "/app/mcp/healthcheck.js"]
ENTRYPOINT ["/usr/local/bin/bun", "/app/mcp/main.js", "--engine", "/app/nami"]
