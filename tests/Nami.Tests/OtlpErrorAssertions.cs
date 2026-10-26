using System.Text;
using Google.Protobuf;
using RpcStatus = Google.Rpc.Status;

namespace Nami.Tests;

/// <summary>The OTLP/HTTP failure body: a google.rpc.Status encoded like the request.</summary>
internal static class OtlpErrorAssertions
{
    /// <summary>
    ///     Requires a nonempty Status body of <paramref name="mediaType" /> on <paramref name="response" /> and returns
    ///     it.
    /// </summary>
    internal static async Task<RpcStatus> HasOtlpErrorAsync(this Assert _, HttpResponseMessage response,
        CancellationToken cancellationToken,
        string mediaType = OtlpReceiver.ProtobufMediaType)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.AreEqual(mediaType, response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        Assert.IsNotEmpty(body);
        return mediaType == "application/json"
            ? JsonParser.Default.Parse<RpcStatus>(Encoding.UTF8.GetString(body))
            : RpcStatus.Parser.ParseFrom(body);
    }
}
