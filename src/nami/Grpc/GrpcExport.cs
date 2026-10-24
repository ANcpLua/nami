using StatusCode = Grpc.Core.StatusCode;

namespace Nami.Grpc;

internal static class GrpcExport
{
    // Single source of truth for the OTLP gRPC export error contract: cancellation -> Cancelled,
    // shutdown or a post-decode processing failure -> Unavailable so exporters retry;
    // malformed payload (bad id lengths) -> InvalidArgument so exporters do not retry.
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> export, string dataKind)
    {
        try
        {
            return await export().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Request was cancelled"));
        }
        catch (ObjectDisposedException)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Service is shutting down"));
        }
        catch (InvalidDataException)
        {
            // Deliberately no exception detail: collector responses never carry exception messages.
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Malformed {dataKind} payload."));
        }
        catch (Exception)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, $"Failed to process {dataKind} data."));
        }
    }
}
