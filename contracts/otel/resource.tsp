import "../common/types.tsp";

using Nami.Api.Contracts.Common;

namespace Nami.Api.Contracts.OTel.Resource;

@doc("The telemetry producer and its resource attributes. Attribute names and values follow the producer's semantic-convention schema; the API does not duplicate that vocabulary.")
model Resource {
  @doc("Service name used by the Collector's service queries.")
  @encodedName("application/json", "service_name")
  serviceName: string;

  @doc("Resource attributes, including service, deployment, host, cloud and process metadata.")
  attributes?: Attribute[];

  @doc("Resource schema URL supplied by the producer.")
  @encodedName("application/json", "schema_url")
  schemaUrl?: string;

  @doc("Number of resource attributes dropped by the producer.")
  @encodedName("application/json", "dropped_attributes_count")
  droppedAttributesCount?: Count;

  @doc("Entities participating in this resource, keyed by its attributes.")
  @encodedName("application/json", "entity_refs")
  entityRefs?: EntityRef[];
}
