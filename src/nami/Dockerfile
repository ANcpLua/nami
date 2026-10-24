FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.2 AS build
WORKDIR /source
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
COPY . .
RUN dotnet publish src/nami/nami.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/runtime-deps:11.0.0-rc.2
WORKDIR /app
COPY --from=build /app .
ENV NAMI_BIND_ADDRESS=0.0.0.0 NAMI_DATA_PATH=/data/nami.duckdb
EXPOSE 4318 4317 5100
ENTRYPOINT ["/app/nami"]
