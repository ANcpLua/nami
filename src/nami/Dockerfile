FROM oven/bun:1.4.3 AS mcp
WORKDIR /source
COPY src/nami.mcp/package.json src/nami.mcp/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/nami.mcp/ ./
RUN bun run build

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.2 AS build
WORKDIR /source
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
COPY . .
RUN dotnet publish src/nami/nami.csproj -c Release -o /app -maxcpucount:2 -nodeReuse:false -p:UseSharedCompilation=false

FROM mcr.microsoft.com/dotnet/runtime-deps:11.0.0-rc.2
WORKDIR /app
COPY --from=build /app .
COPY --from=mcp /usr/local/bin/bun /usr/local/bin/bun
COPY --from=mcp /source/dist/main.js ./mcp/main.js
ENV NAMI_BIND_ADDRESS=0.0.0.0 NAMI_DATA_PATH=/data/nami.duckdb
EXPOSE 4318 4317 5100
ENTRYPOINT ["/usr/local/bin/bun", "/app/mcp/main.js", "--engine", "/app/nami"]
