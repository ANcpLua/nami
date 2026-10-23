namespace Nami.Collector.Ingestion;

internal static class AttributeKeySets
{
    private const string BaggagePrefix = CollectorSemanticAttributeCatalog.BaggagePrefix;

    internal static FrozenSet<string> SessionCorrelation => CollectorSemanticAttributeCatalog.SessionCorrelation;

    // First-match lookups must read the precedence arrays, not the sets above: FrozenSet
    // enumeration order is an implementation detail, so a set-driven lookup silently picks a
    // winner by hash layout instead of by the order declared in collector-semantic-policy.json.
    internal static string[] SessionCorrelationPrecedence =>
        CollectorSemanticAttributeCatalog.SessionCorrelationPrecedence;

    internal static string[] CodexSessionCorrelationPrecedence =>
        CollectorSemanticAttributeCatalog.CodexSessionCorrelationPrecedence;

    internal static string[] ProjectIdResourceKeyPrecedence =>
        CollectorSemanticAttributeCatalog.ProjectIdResourceKeyPrecedence;

    // A key a pinned third-party library emits is a tag, not vocabulary: it is forwarded exactly
    // as it arrived and never rewritten. The prefix and exact denials still apply to it — a
    // vendor is no more entitled to write a credential into storage than an application is — and
    // only the blunt token rule steps aside for it (see IsPersistable). The keys are RegistryPolicy.VendorKeys.
    private static bool IsVendorTag(string key) => CollectorSemanticAttributeCatalog.VendorKeys.Contains(key);

    internal static bool ShouldCaptureSpanAttribute(string key) =>
        CollectorSemanticAttributeCatalog.TryGetCapturedFieldPersistence(key, out _)
            ? IsPersistable(key)
            : IsPersistable(key) || SessionCorrelation.Contains(key) ||
              CollectorSemanticAttributeCatalog.SpanHotAttributeKeys.Contains(key);

    /// <summary>
    /// Whether the registry lets the collector persist <paramref name="key"/> at all, in
    /// <c>RegistryPolicy.IsPersisted</c>'s order, plus the collector's baggage denial.
    /// Registry membership does not determine whether an application attribute is valid.
    /// </summary>
    internal static bool IsPersistable(string key)
    {
        if (IsDeniedKeyOrPrefix(key))
            return false;

        if (key.StartsWith(BaggagePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        // Removing a canonical exception key from the raw-content deny table must
        // not reopen its formerly denied descendants or case variants. Eligibility
        // is exact; the generated registry identifies every key needing this guard.
        foreach (var exact in CollectorSemanticAttributeCatalog.ExceptionTextKeys)
        {
            if (key.Equals(exact, StringComparison.Ordinal)) continue;
            if (key.Equals(exact, StringComparison.OrdinalIgnoreCase) ||
                key.Length > exact.Length && key[exact.Length] == '.' && key.StartsWith(exact, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (CollectorSemanticAttributeCatalog.TryGetCapturedFieldPersistence(key, out var captured))
            return captured;

        // The token rule is deliberately blunt, so registry keys that merely contain a denied token
        // (db.query.summary, gen_ai.usage.input_tokens) opt out of it by exact name, and so does every
        // vendor pass-through key: the vendor spells its own tags, and a dozen of them
        // (messaging.masstransit.message_id, nservicebus.message_intent, execution.result) contain a
        // denied token while carrying nothing the token exists to catch. The exact and prefix denials
        // above are privacy rules and run ahead of this for a vendor key exactly as for any other key.
        if (CollectorSemanticAttributeCatalog.TokenExemptKeys.Contains(key) || IsVendorTag(key))
            return true;

        foreach (var token in CollectorSemanticAttributeCatalog.DeniedKeyTokens)
        {
            if (key.Contains(token, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    // Prefixes, not their registry expansion: an application-defined key under one of these
    // namespaces (user.email_address, enduser.tenant) is denied as surely as a registry key.
    private static bool IsDeniedKeyOrPrefix(ReadOnlySpan<char> key)
    {
        if (IsNotPersistedKeyOrTemplateChild(key))
            return true;

        foreach (var prefix in CollectorSemanticAttributeCatalog.NotPersistedPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> s_notPersistedKeys =
        CollectorSemanticAttributeCatalog.NotPersistedKeys.GetAlternateLookup<ReadOnlySpan<char>>();

    // A denied template key denies its children too: http.request.header denies http.request.header.<name>.
    private static bool IsNotPersistedKeyOrTemplateChild(ReadOnlySpan<char> key)
    {
        var candidate = key;
        while (true)
        {
            if (s_notPersistedKeys.Contains(candidate))
                return true;

            var dot = candidate.LastIndexOf('.');
            if (dot <= 0)
                return false;

            candidate = candidate[..dot];
        }
    }

}

internal static class AttributeLookupExtensions
{
    /// <summary>
    /// The record's session id, exactly as received: the first non-blank string among the session
    /// correlation keys, else, on a record whose scope name or service.name starts with the Codex
    /// producer prefix, the first Codex key holding a UUID string. Anything else is no session.
    /// </summary>
    internal static string? GetSessionId(
        this IReadOnlyDictionary<string, OtlpAttributeValue> attributes,
        string? scopeName,
        string? serviceName)
    {
        foreach (var key in AttributeKeySets.SessionCorrelationPrecedence)
        {
            if (attributes.GetValueOrDefault(key)?.AsString() is { } value && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        if (!IsCodexProducer(scopeName) && !IsCodexProducer(serviceName))
            return null;

        foreach (var key in AttributeKeySets.CodexSessionCorrelationPrecedence)
        {
            if (attributes.GetValueOrDefault(key)?.AsString() is { } value && Guid.TryParse(value, out _))
                return value;
        }

        return null;
    }

    private static bool IsCodexProducer(string? producer) =>
        producer?.StartsWith(CollectorSemanticAttributeCatalog.CodexProducerPrefix, StringComparison.OrdinalIgnoreCase) is true;

    internal static bool IsAny(
        this string key,
        FrozenSet<string> candidates) =>
        candidates.Contains(key);
}
