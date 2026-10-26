// Build-only mapping; wire definitions remain language independent.
import "./emitters/csharp/lib/main.tsp";
using Nami.Api.Schema.Emit.CSharp;
@@csharpNamespace(Nami.Api.Contracts.Engine, "Nami.Api.Contracts.Engine");
@@csharpNamespace(Nami.Api.Contracts, "Nami.Api.Contracts");
@@csharpNamespace(Nami.Api.Contracts.Common, "Nami.Api.Contracts.Common");
@@csharpNamespace(Nami.Api.Contracts.Health, "Nami.Api.Contracts.Health");
@@csharpNamespace(Nami.Api.Contracts.Tools, "Nami.Api.Contracts.Tools");
@@csharpNamespace(Nami.Api.Contracts.OTel, "Nami.Api.Contracts.OTel");
@@csharpNamespace(
  Nami.Api.Contracts.OTel.Enums,
  "Nami.Api.Contracts.OTel.Enums"
);
@@csharpNamespace(Nami.Api.Contracts.OTel.Logs, "Nami.Api.Contracts.OTel.Logs");
@@csharpNamespace(
  Nami.Api.Contracts.OTel.Resource,
  "Nami.Api.Contracts.OTel.Resource"
);
@@csharpNamespace(
  Nami.Api.Contracts.OTel.Traces,
  "Nami.Api.Contracts.OTel.Traces"
);
@@csharpNamespace(
  Nami.Api.Contracts.OTel.Metrics,
  "Nami.Api.Contracts.OTel.Metrics"
);

@@csharpOutput(Nami.Models.Storage.Rows, #{
  directory: "src/nami.collector.storage/Generated",
  `namespace`: "Nami.Collector.Storage",
  file: "StorageRows.g.cs",
  json: false,
  typescript: false,
  schema: false
});

@@csharpOutput(Nami.Models.Storage.Json, #{
  directory: "src/nami.collector.storage/Generated", `namespace`: "Nami.Collector.Storage",
  file: "StoredMetricRecords.g.cs", json: true, context: "StorageJsonSerializerContext", typescript: false, schema: false
});

@@csharpOutput(Nami.Models.Ingestion, #{
  directory: "src/nami.collector.ingestion/Generated", `namespace`: "Nami.Collector.Ingestion",
  file: "IngestionModels.g.cs", json: false, typescript: false, schema: false
});

@@tool(Nami.Api.Contracts.Tools.SourcesStatusInput, "sources_status", Nami.Api.Contracts.Tools.SourcesStatusResult, Nami.Api.Contracts.Common.ToolError);
@@tool(Nami.Api.Contracts.Tools.ErrorsRecentInput, "errors_recent", Nami.Api.Contracts.Tools.ErrorsRecentResult, Nami.Api.Contracts.Common.ToolError);
@@tool(Nami.Api.Contracts.Tools.TracesGetInput, "traces_get", Nami.Api.Contracts.Tools.TracesGetResult, Nami.Api.Contracts.Common.ToolError);
@@tool(Nami.Api.Contracts.Tools.TracesSlowestInput, "traces_slowest", Nami.Api.Contracts.Tools.TracesSlowestResult, Nami.Api.Contracts.Common.ToolError);
@@tool(Nami.Api.Contracts.Tools.LogsSearchInput, "logs_search", Nami.Api.Contracts.Tools.LogsSearchResult, Nami.Api.Contracts.Common.ToolError);
@@tool(Nami.Api.Contracts.Tools.ChangesRecentInput, "changes_recent", Nami.Api.Contracts.Tools.ChangesRecentResult, Nami.Api.Contracts.Common.ToolError);
@@wireRoot(Nami.Api.Contracts.Engine.EngineRequest);
@@wireRoot(Nami.Api.Contracts.Engine.EngineResponse);
@@wireRoot(Nami.Api.Contracts.Engine.EngineLimits);

@@csharpOutput(Nami.Models.Ingestion.Metrics, #{
 directory: "src/nami.collector.ingestion/Generated", `namespace`: "Nami.Collector.Ingestion.Models",
 file: "MetricModels.g.cs", json: false, typescript: false, schema: false
});

@@storageProjection(Nami.Models.Ingestion.Metrics.MetricPointData, Nami.Models.Storage.Json.StoredMetricPointData);
@@storageProjection(Nami.Models.Ingestion.Metrics.MetricNumberValue, Nami.Models.Storage.Json.StoredNumberValue);
@@storageProjection(Nami.Models.Ingestion.Metrics.AttributeIntValue, Nami.Models.Storage.Json.StoredIntValue);
@@storageProjection(Nami.Models.Ingestion.Metrics.AttributeDoubleValue, Nami.Models.Storage.Json.StoredDoubleValue);
@@storageProjection(Nami.Models.Ingestion.Metrics.MetricNumberPointData, Nami.Models.Storage.Json.StoredNumberPointData);
@@storageProjection(Nami.Models.Ingestion.Metrics.MetricHistogramPointData, Nami.Models.Storage.Json.StoredHistogramPointData);
@@storageProjection(Nami.Models.Ingestion.Metrics.MetricExponentialHistogramPointData, Nami.Models.Storage.Json.StoredExponentialHistogramPointData);
@@storageProjection(Nami.Models.Ingestion.Metrics.MetricExponentialBuckets, Nami.Models.Storage.Json.StoredExponentialBuckets);
@@storageProjection(Nami.Models.Ingestion.Metrics.MetricSummaryPointData, Nami.Models.Storage.Json.StoredSummaryPointData);
@@storageProjection(Nami.Models.Ingestion.Metrics.MetricQuantileValue, Nami.Models.Storage.Json.StoredQuantileValue);
@@storageProjection(Nami.Models.Ingestion.Metrics.MetricExemplar, Nami.Models.Storage.Json.StoredExemplar);
@@storageProjection(Nami.Models.Ingestion.Metrics.Attribute, Nami.Models.Storage.Json.StoredAttribute);
@@storageProjectionUsing(Nami.Models.Ingestion.OtlpAttributeValue, "StoredRecordMapper.ToStored");

@@csharpOutput(Nami.Models.Configuration, #{
 directory: "src/nami.collector.ingestion/Generated", `namespace`: "Nami.Collector.Ingestion.Configuration",
 file: "CollectorConfigurationKeys.g.cs", json: false, typescript: false, schema: false
});

@@wireRoot(Nami.Api.Contracts.Engine.EngineCommand);

@@attributeProjection(Nami.Models.Storage.Json.StoredAttributeValue, Nami.Api.Contracts.Common.AttributeValue);

@@csharpOutput(Nami.Models.Ingestion.Records, #{
 directory: "src/nami.collector.ingestion/Generated", `namespace`: "Nami.Collector.Ingestion",
 file: "IngestionRecords.g.cs", json: false, typescript: false, schema: false
});

@@csharpOutput(Nami.Models.Ingestion.Primitives, #{
 directory: "src/nami.collector.ingestion/Generated", `namespace`: "Nami.Collector.Ingestion.Primitives",
 file: "MetricShapes.g.cs", json: false, typescript: false, schema: false
});

@@csharpOutput(Nami.Models.Storage.Persistence, #{
 directory: "src/nami.collector.storage/Generated", `namespace`: "Nami.Collector.Storage",
 file: "StoredDocuments.g.cs", json: true, context: "StorageJsonSerializerContext", typescript: false, schema: false
});
@@storageProjection(Nami.Models.Ingestion.Records.ResourceEntityRefIngestionRecord, Nami.Models.Storage.Persistence.StoredResourceEntityRef);

@@storageAttributeProjection(Nami.Models.Ingestion.OtlpAttributeValue, Nami.Models.Storage.Json.StoredAttributeValue);
