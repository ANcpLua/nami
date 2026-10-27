import "../emitters/csharp/lib/main.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Contracts.Runtime;

@csharpForm("public sealed record")
model ContractIssue {
  field: string;
  keyword: string;
  message: string;
  allowed?: string[];
  hint?: string;
}
