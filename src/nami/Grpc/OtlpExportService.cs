namespace Nami.Grpc;

/// <summary>
///     Base of the three OTLP export services. <c>OtlpExportMethodProvider</c> binds their Export method with OTLP's
///     own request decoding. The binder that AddGrpc registers for every gRPC service in the process runs for these
///     three as well: it calls the bind method named here and gets nothing to add. Without a bind method it warns at
///     start-up for each of them, and taking the binder out of the container instead leaves every other gRPC service
///     without methods.
/// </summary>
[BindServiceMethod(typeof(OtlpExportService), nameof(BindService))]
internal abstract class OtlpExportService
{
    public static void BindService(ServiceBinderBase binder, OtlpExportService? service)
    {
    }
}
