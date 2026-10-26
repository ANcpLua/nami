import "./ingestion.tsp";
namespace Nami.Shapes.Ingestion;
model ResourceAndScope {
  projectIdHint?: string;
  resourceAttributes: Nami.Models.Ingestion.NormalizedAttributes;
  resourceDroppedAttributesCount: uint32 = 0;
  resourceEntityRefs: Nami.Models.Ingestion.Records.ResourceEntityRefIngestionRecord[] = #[];
  resourceSchemaUrl?: string;
  scopeAttributes: Nami.Models.Ingestion.NormalizedAttributes = #{ values: #{}, renames: #[] };
  scopeDroppedAttributesCount: uint32 = 0;
  scopeName?: string;
  scopeSchemaUrl?: string;
  scopeVersion?: string;
  serviceName: string;
}
