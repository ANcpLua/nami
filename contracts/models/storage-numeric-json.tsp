import "./storage-json.tsp";
namespace Nami.Models.Storage.NumericJson;

// Standalone query projections also preserve OTLP nonfinite values. Their numeric
// policy is scoped to this root rather than disabling every storage JSON writer.
model Doubles is Nami.Models.Storage.Json.StoredFloat64[];
