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

@@csharpOutput(Nami.Models.Storage.NumericJson, #{
 directory: "src/nami.collector.storage/Generated", `namespace`: "Nami.Collector.Storage",
 file: "StorageNumericJson.g.cs", json: true, context: "StorageNumericJsonSerializerContext", typescript: false, schema: false
});

@@csharpOutput(Nami.Models.Configuration.Hosting, #{
 directory: "src/nami/Generated", `namespace`: "Nami.Hosting", file: "HostingOptions.g.cs",
 mutable: true, json: false, typescript: false, schema: false
});
@@csharpOutput(Nami.Models.Configuration.Retention, #{
 directory: "src/nami/Generated", `namespace`: "Nami.Retention", file: "RetentionOptions.g.cs",
 mutable: true, json: false, typescript: false, schema: false
});
@@csharpOutput(Nami.Models.Configuration.Auth, #{
 directory: "src/nami/Generated", `namespace`: "Nami.ApiKeys", file: "AuthOptions.g.cs",
 mutable: true, json: false, typescript: false, schema: false
});
@@csharpOutput(Nami.Models.Configuration.Cors, #{
 directory: "src/nami.collector.ingestion/Configuration/Generated", `namespace`: "Nami.Collector.Ingestion",
 file: "CorsOptions.g.cs", mutable: true, json: false, typescript: false, schema: false
});
@@csharpOutput(Nami.Models.Storage.Runtime, #{
 directory: "src/nami.collector.storage/Generated", `namespace`: "Nami.Collector.Storage",
 file: "StorageRuntime.g.cs", json: false, typescript: false, schema: false
});

@@modelProjection(Nami.Models.Storage.Rows.SpanStorageRow, Nami.Models.Ingestion.Records.SpanIngestionRecord, Nami.Models.Storage.Runtime.SpanStorageValues, Nami.Models.Storage.Runtime.SpanHotAttributeProjection);
@@modelProjection(Nami.Models.Storage.Rows.LogStorageRow, Nami.Models.Ingestion.Records.LogIngestionRecord, Nami.Models.Storage.Runtime.LogStorageValues);
@@modelProjection(Nami.Models.Storage.Rows.MetricSeriesRow, Nami.Models.Ingestion.Records.MetricPointIngestionRecord, Nami.Models.Storage.Runtime.MetricSeriesValues);

@@csharpOutput(Nami.Models.Configuration.Credentials, #{
 directory: "src/nami/Generated", `namespace`: "Nami.ApiKeys", file: "CredentialJson.g.cs",
 json: true, context: "CredentialJsonContext", strictJson: true, typescript: true, schema: false
});

@@csharpOutput(Nami.Models.Tools.Runtime, #{
 directory: "src/nami/Generated", `namespace`: "Nami.Tools", file: "ToolRuntime.g.cs",
 json: false, typescript: false, schema: false
});
@@modelProjection(Nami.Api.Contracts.OTel.Traces.Span, Nami.Models.Storage.Rows.SpanStorageRow, Nami.Models.Tools.Runtime.SpanApiValues);
@@modelProjection(Nami.Api.Contracts.OTel.Resource.Resource, Nami.Models.Storage.Rows.SpanStorageRow, Nami.Models.Tools.Runtime.ResourceApiValues);
@@projectionSource(Nami.Api.Contracts.OTel.Resource.Resource.schemaUrl, Nami.Models.Storage.Rows.SpanStorageRow.resourceSchemaUrl);
@@projectionSource(Nami.Api.Contracts.OTel.Resource.Resource.droppedAttributesCount, Nami.Models.Storage.Rows.SpanStorageRow.resourceDroppedAttributesCount);
@@modelProjection(Nami.Api.Contracts.Common.InstrumentationScope, Nami.Models.Storage.Rows.SpanStorageRow, Nami.Models.Tools.Runtime.ScopeApiValues);
@@projectionSource(Nami.Api.Contracts.Common.InstrumentationScope.schemaUrl, Nami.Models.Storage.Rows.SpanStorageRow.scopeSchemaUrl);
@@projectionSource(Nami.Api.Contracts.Common.InstrumentationScope.droppedAttributesCount, Nami.Models.Storage.Rows.SpanStorageRow.scopeDroppedAttributesCount);
@@modelProjection(Nami.Api.Contracts.OTel.Traces.SpanEvent, Nami.Models.Storage.Persistence.SpanEventJson, Nami.Models.Tools.Runtime.EventApiValues);
@@modelProjection(Nami.Api.Contracts.OTel.Traces.SpanLink, Nami.Models.Storage.Persistence.SpanLinkJson, Nami.Models.Tools.Runtime.LinkApiValues);
@@modelProjection(Nami.Api.Contracts.Common.EntityRef, Nami.Models.Storage.Persistence.StoredResourceEntityRef, Nami.Models.Tools.Runtime);

@@csharpOutput(Nami.Models.Contracts.Runtime, #{
 directory: "internal/nami.contracts/Generated", `namespace`: "Nami.Api.Contracts", file: "ContractIssue.g.cs",
 json: false, typescript: false, schema: false
});
