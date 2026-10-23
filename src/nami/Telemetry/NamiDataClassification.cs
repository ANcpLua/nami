using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;

namespace Nami.Collector.Telemetry;

internal static class NamiDataClassification
{
    public static DataClassification Pii => new("PII", "GDPR:PersonalData");

    public static DataClassification Secret => new("Secret", "Security:Credential");

    public static DataClassification PromptContent => new("PromptContent", "GenAI:UserContent");

    public static DataClassification InternalId => new("InternalId", "Internal:Identifier");
}

internal static class NamiRedactionExtensions
{
    public static IRedactionBuilder AddNamiRedactors(this IRedactionBuilder builder)
    {
        builder.SetRedactor<ErasingRedactor>(NamiDataClassification.Pii);

        builder.SetRedactor<ErasingRedactor>(NamiDataClassification.Secret);

        builder.SetRedactor<ErasingRedactor>(NamiDataClassification.PromptContent);

        builder.SetRedactor<NullRedactor>(NamiDataClassification.InternalId);

        return builder;
    }
}
