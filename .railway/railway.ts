import { defineRailway, github, preserve, service, volume } from "railway/iac";

export const partial = "nami";
export default defineRailway(() => {
  const data = volume("nami-data", { region: "europe-west4-drams3a", sizeMB: 10000 });
  service("nami", {
    source: github("ANcpLua/nami"),
    build: { builder: "DOCKERFILE", dockerfilePath: "src/nami/Dockerfile" },
    deploy: { healthcheckPath: "/health", healthcheckTimeout: 300, overlapSeconds: 0, drainingSeconds: 60 },
    volumeMounts: { "/data": data },
    env: {
      NAMI_BIND_ADDRESS: "0.0.0.0",
      NAMI_DATA_PATH: "/data/nami.duckdb",
      // Railway mounts a volume owned by root; the image's own user (app) applies to Docker everywhere else.
      RAILWAY_RUN_UID: "0",
      NAMI_PUBLIC_URL: preserve(),
      NAMI_OTLP_AUTH_MODE: preserve(),
      NAMI_OTLP_PROJECT_KEYS: preserve(),
    },
  });
});
