using Nami.Api.Contracts;
using ContractJson = Nami.Api.Contracts.ContractJson;

namespace Nami.Collector.Hosting;

/// <summary>Validates a contract before committing an HTTP response.</summary>
internal static class ContractHttp
{
    internal static IResult Json<T>(T value, int? statusCode = null, string? contentType = null) =>
        Results.Json(ContractJson.Serialize(value), ContractTypes.TypeInfo<JsonElement>(),
            contentType: contentType, statusCode: statusCode);
}
