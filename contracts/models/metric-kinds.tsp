import "../emitters/csharp/lib/main.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Ingestion.Primitives;
@csharpEnum("byte") enum MetricKind { Gauge: 1, Sum: 2, Histogram: 3, ExponentialHistogram: 4, Summary: 5 }
@csharpEnum("byte") enum MetricTemporality { Unspecified: 0, Delta: 1, Cumulative: 2 }
