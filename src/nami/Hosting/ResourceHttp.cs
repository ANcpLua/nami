using WireJson = Nami.Api.Contracts.ContractJson;
using Nami.Collector.Storage;

namespace Nami.Collector.Hosting;

/// <summary>
/// The shared write base (SCH-26) every user-resource route uses: <c>Idempotency-Key</c> on
/// creating POSTs, <c>If-Match</c> on PATCH and DELETE (428 when missing, 412 when stale), and the
/// <c>ETag</c>/<c>Location</c> headers. A body is read with its contract's bounds by
/// <c>CollectorEndpointExtensions.ReadResourceBodyAsync</c>.
/// </summary>
internal static class ResourceHttp
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int MaxIdempotencyKeyLength = 255;

    /// <summary>A key older than this reads as absent and may be reused.</summary>
    internal static readonly TimeSpan s_idempotencyKeyLifetime = TimeSpan.FromHours(24);

    internal static IResult InvalidBody(string message) =>
        ContractErrorResults.Validation("body", message, "body.invalid");

    internal static IResult? ReadIdempotencyKey(HttpRequest request, out string? key)
    {
        key = null;
        if (!request.Headers.TryGetValue(IdempotencyKeyHeader, out var values))
            return null;

        if (values.Count != 1 || string.IsNullOrEmpty(values[0]) || values[0]!.Length > MaxIdempotencyKeyLength)
        {
            return ContractErrorResults.Validation(
                IdempotencyKeyHeader,
                $"Idempotency-Key must be one value of 1 to {MaxIdempotencyKeyLength} characters.",
                "idempotency_key.invalid");
        }

        key = values[0];
        return null;
    }

    /// <summary>
    /// Reads <c>If-Match</c>. RFC 9110 strong comparison: <c>*</c> matches any current version,
    /// and a weak tag never matches. A bare, unquoted tag is read as the quoted tag it names, so a
    /// client that copied <c>etag</c> without its quotes still matches.
    /// </summary>
    internal static IResult? ReadIfMatch(HttpRequest request, out EntityTagCondition condition)
    {
        condition = default;
        var raw = string.Join(',', request.Headers.IfMatch.ToArray());
        if (string.IsNullOrWhiteSpace(raw))
            return ContractErrorResults.PreconditionRequired();

        var tags = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part is "*")
            {
                condition = new EntityTagCondition(true, []);
                return null;
            }

            tags.Add(part.StartsWith('"') || part.StartsWith("W/", StringComparison.Ordinal) ? part : $"\"{part}\"");
        }

        condition = new EntityTagCondition(false, tags);
        return null;
    }

    /// <summary>A new strong entity tag: 96 random bits, quoted as the ETag header requires.</summary>
    internal static string NewEtag() => $"\"{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}\"";

    /// <summary>A new opaque resource id; time-ordered so ids sort roughly by creation.</summary>
    internal static string NewId(string prefix) => prefix + Guid.CreateVersion7().ToString("N");

    /// <summary>SHA-256 of the canonical re-serialization, so whitespace or member order never changes it.</summary>
    internal static string Fingerprint<T>(T body) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(WireJson.Serialize(body).GetRawText())));

    internal static IResult WithEtag<T>(HttpContext context, string etag, T body)
    {
        context.Response.Headers.ETag = etag;
        return Results.Json(WireJson.Serialize(body), global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::System.Text.Json.JsonElement>());
    }

    internal static IResult Created<T>(
        HttpContext context,
        string location,
        string etag,
        T body)
    {
        context.Response.Headers.ETag = etag;
        context.Response.Headers.Location = location;
        return Results.Json(WireJson.Serialize(body), global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::System.Text.Json.JsonElement>(), statusCode: StatusCodes.Status201Created);
    }

    internal static IResult IdempotencyConflict(string key) =>
        ContractErrorResults.Conflict(
            key,
            "This Idempotency-Key was already used with a different request body.");

    internal static ulong NowUnixNano(TimeProvider timeProvider) =>
        NamiTimeConversions.ToUnixNanoUnsigned(timeProvider.GetUtcNow());

    internal static ulong IdempotencyNotBefore(TimeProvider timeProvider) =>
        NamiTimeConversions.ToUnixNanoUnsigned(timeProvider.GetUtcNow() - s_idempotencyKeyLifetime);
}
