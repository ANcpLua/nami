// Tool refusals are values; Nami exposes no REST write API.
namespace Nami.Api.Contracts.Common;

  @doc("A typed tool refusal. The field and allowed values make invalid arguments repairable.")
  model ToolError {
    code: string;
    message: string;
    retryable: boolean;
    field?: string;
    allowed?: string[];
    hint?: string;
    @encodedName("application/json", "resource_type") resourceType?: string;
    @encodedName("application/json", "resource_id") resourceId?: string;
  }
