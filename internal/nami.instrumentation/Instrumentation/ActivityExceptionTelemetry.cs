using ErrorAttributes = Nami.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using ExceptionAttributes = Nami.Telemetry.SemanticConventions.Attributes.Exception.ExceptionAttributes;
using NamiAttributes = Nami.Telemetry.SemanticConventions.Incubating.Attributes.Nami.NamiAttributes;

namespace Nami.Instrumentation.Instrumentation;

public static class ActivityExceptionTelemetry
{
    private const string ErrorType = ErrorAttributes.Type;
    private const string ExceptionType = ExceptionAttributes.Type;

    public const string ExceptionSource = NamiAttributes.ExceptionSource;

    public static void Record(
        Activity? activity,
        Exception exception,
        string? errorType = null)
    {
        if (activity is null)
            return;

        ApplyError(activity, exception, errorType);
        activity.AddEvent(new ActivityEvent("exception", tags: CreateTags(exception)));
    }

    public static void ApplyError(
        Activity? activity,
        Exception exception,
        string? errorType = null)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error);
        activity.SetTag(ErrorType, ResolveErrorType(exception, errorType));
    }

    public static ActivityTagsCollection CreateTags(Exception exception) =>
        new()
        {
            { ExceptionType, exception.GetType().FullName }
        };

    private static string ResolveErrorType(Exception exception, string? errorType = null) =>
        !string.IsNullOrWhiteSpace(errorType)
            ? errorType
            : exception.GetType().FullName ?? exception.GetType().Name;
}
