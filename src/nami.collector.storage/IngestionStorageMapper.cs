namespace Nami.Collector.Storage;

internal static class IngestionStorageMapper
{
    public static List<SpanStorageRow> ToSpanStorageRows(TraceIngestionBatch batch,
        string? authenticatedProjectId = null)
    {
        return batch.Spans.Select(span => ToSpanStorageRow(span, authenticatedProjectId)).ToList();
    }

    public static List<LogStorageRow> ToLogStorageRows(LogIngestionBatch batch, string? authenticatedProjectId = null)
    {
        return batch.Logs.Select(log => ToLogStorageRow(log, authenticatedProjectId)).ToList();
    }

    private static SpanStorageRow ToSpanStorageRow(SpanIngestionRecord span, string? authenticatedProjectId)
    {
        if (span.EndTimeUnixNano < span.StartTimeUnixNano)
            throw new InvalidDataException("Span end time must not precede its start.");
        var durationNs = span.EndTimeUnixNano - span.StartTimeUnixNano;
        var projection = SpanHotAttributeProjectionFactory.FromAttributes(span.Attributes);
        var resourceEntityRefsJson = StorageJsonSerializerContext.SerializeResourceEntityRefs(span.ResourceEntityRefs);

        return ModelMappings.ToModel(span, new SpanStorageValues
        {
            AttributeRenames = CollectRenames(span),
            ProjectId = ProjectScope.ForIngest(span.ProjectIdHint, authenticatedProjectId),
            ParentSpanId = string.IsNullOrEmpty(span.ParentSpanId) ? null : span.ParentSpanId,
            SessionId = span.Attributes.GetSessionId(),
            Kind = (byte)(span.Kind ?? 0),
            DurationNs = durationNs,
            StatusCode = (byte)(span.StatusCode ?? 0),
            AttributesJson = PersistedAttributePolicy.SerializeAttributes(span.Attributes),
            ResourceJson = PersistedAttributePolicy.SerializeAttributes(span.ResourceAttributes),
            ResourceEntityRefsJson = resourceEntityRefsJson,
            EventsJson = SpanChildStorage.SerializeEvents(span.Events),
            LinksJson = SpanChildStorage.SerializeLinks(span.Links),
            ScopeAttributesJson = PersistedAttributePolicy.SerializeScopeAttributes(span.ScopeAttributes),
        }, projection);
    }

    // Most records rename nothing; collecting from empty lists allocated for every span.
    private static IReadOnlyList<AttributeRename> CollectRenames(SpanIngestionRecord span)
    {
        var any = span.Attributes.Renames.Count > 0 || span.ResourceAttributes.Renames.Count > 0 ||
                  span.ScopeAttributes.Renames.Count > 0;
        for (var i = 0; !any && i < span.Events.Count; i++) any = span.Events[i].Attributes.Renames.Count > 0;
        for (var i = 0; !any && i < span.Links.Count; i++) any = span.Links[i].Attributes.Renames.Count > 0;
        return any
            ? NormalizedAttributes.Collect([
                span.Attributes, span.ResourceAttributes, span.ScopeAttributes,
                .. span.Events.Select(static e => e.Attributes), .. span.Links.Select(static l => l.Attributes)
            ])
            : [];
    }

    private static LogStorageRow ToLogStorageRow(LogIngestionRecord log, string? authenticatedProjectId)
    {
        var projectId = ProjectScope.ForIngest(log.ProjectIdHint, authenticatedProjectId);
        var severityText = log.SeverityText;
        var severityNumber = (byte)log.SeverityNumber;
        var sessionId = log.Attributes.GetSessionId();
        // Identity uses complete canonical content; the retention rendering may be bounded.
        // Omitted suffixes contribute only to the digest and are never stored.
        var storedBody = StoredRecordMapper.ToStored(log.Body);
        var originalBody = storedBody is null ? null : EncodeBody(storedBody);
        // Ingest has bounded the body like any value: a string is cut to the cap, a list or map within the cap is kept
        // as sent, one over it is gone. Storage writes what it is given.
        var body = originalBody;
        var attributesJson = PersistedAttributePolicy.SerializeAttributes(log.Attributes);
        var resourceJson = PersistedAttributePolicy.SerializeAttributes(log.ResourceAttributes);
        var resourceEntityRefsJson = StorageJsonSerializerContext.SerializeResourceEntityRefs(log.ResourceEntityRefs);
        var scopeAttributesJson = PersistedAttributePolicy.SerializeScopeAttributes(log.ScopeAttributes);
        var resourceEntityRefsIdentity = GetResourceEntityRefsIdentity(
            log.ResourceEntityRefs,
            log.ResourceAttributes);

        return ModelMappings.ToModel(log, new LogStorageValues
        {
            AttributeRenames =
                NormalizedAttributes.Collect([log.Attributes, log.ResourceAttributes, log.ScopeAttributes]),
            ProjectId = projectId,
            LogId = StableStorageId("log", builder =>
            {
                builder.AppendSegment(projectId, true);
                builder.AppendSegment(log.TraceId, true);
                builder.AppendSegment(log.SpanId, true);
                builder.AppendSegment(log.EventName, true);
                builder.AppendSegment(sessionId, true);
                AppendIdentityPart(builder, log.TimeUnixNano);
                AppendIdentityPart(builder, log.TimeOrigin.ProducerObservedTime);
                // The record's place in its request: equal records of one request are rows of their own, and the
                // request sent again is the same rows (LogIngestionRecord.RequestOrdinal).
                if (log.RequestOrdinal is { } ordinal) AppendIdentityPart(builder, ordinal);
                AppendIdentityPart(builder, severityNumber);
                builder.AppendSegment(severityText, true);
                builder.AppendSegment(originalBody, true);
                builder.AppendSegment(log.ServiceName, true);
                builder.AppendSegment(attributesJson, true);
                builder.AppendSegment(resourceJson, true);
                builder.AppendSegment(log.ScopeName, true);
                builder.AppendSegment(log.ScopeVersion, true);
                builder.AppendSegment(log.ScopeSchemaUrl, true);
                builder.AppendSegment(scopeAttributesJson, true);
                AppendIdentityPart(builder, log.ScopeDroppedAttributesCount);
                builder.AppendSegment(log.ResourceSchemaUrl, true);
                AppendIdentityPart(builder, log.ResourceDroppedAttributesCount);
                AppendIdentityPart(builder, log.Flags);
                AppendIdentityPart(builder, log.DroppedAttributesCount);
                if (!string.IsNullOrEmpty(resourceEntityRefsIdentity))
                    builder.AppendSegment(resourceEntityRefsIdentity, true);
            }),
            SessionId = sessionId,
            TimeUnixNano = log.TimeUnixNano,
            ObservedTimeUnixNano = log.ObservedTimeUnixNano,
            SeverityNumber = severityNumber,
            SeverityText = severityText,
            Body = body,
            AttributesJson = attributesJson,
            ResourceJson = resourceJson,
            ResourceEntityRefsJson = resourceEntityRefsJson,
            ScopeAttributesJson = scopeAttributesJson,
            ReceivedTimeUnixNano = log.ReceivedTimeUnixNano,
        });
    }

    // The domain line is the prefix itself: a log id is "log_" and the hash of "log\n" and its segments.
    private static string StableStorageId(string prefix, Action<StringBuilder> appendParts) =>
        StorageIdentity.Compute(prefix, prefix + "_", appendParts);

    private static void AppendIdentityPart(StringBuilder builder, byte value) =>
        builder.AppendSegment(value.ToString(CultureInfo.InvariantCulture), true);

    private static void AppendIdentityPart(StringBuilder builder, int value) =>
        builder.AppendSegment(value.ToString(CultureInfo.InvariantCulture), true);

    private static void AppendIdentityPart(StringBuilder builder, ulong value) =>
        builder.AppendSegment(value.ToString(CultureInfo.InvariantCulture), true);

    private static void AppendIdentityPart(StringBuilder builder, ulong? value) =>
        builder.AppendSegment(value?.ToString(CultureInfo.InvariantCulture), true);

    private static string EncodeBody(StoredAttributeValue body) =>
        JsonSerializer.Serialize(body, StorageJsonSerializerContext.Default.StoredAttributeValue);

    private static string? GetResourceEntityRefsIdentity(
        IReadOnlyList<ResourceEntityRefIngestionRecord> entityRefs,
        NormalizedAttributes resourceAttributes)
    {
        if (entityRefs.Count is 0) return null;

        var identities = new List<string>(entityRefs.Count);
        foreach (var entityRef in entityRefs)
        {
            var identity = new StringBuilder();
            identity.AppendSegment(entityRef.Type, true);
            var idKeys = entityRef.IdKeys.Order(StringComparer.Ordinal).ToArray();
            AppendIdentityPart(identity, idKeys.Length);
            foreach (var key in idKeys)
            {
                if (!resourceAttributes.TryGetValue(key, out var value))
                {
                    throw new InvalidDataException(
                        $"Resource entity id_key '{key}' does not reference a persisted resource attribute.");
                }

                identity.AppendSegment(key, true);
                identity.AppendSegment(StoredAttributeValue.StableString(StoredRecordMapper.ToStored(value)), true);
            }

            identities.Add(identity.ToString());
        }

        identities.Sort(StringComparer.Ordinal);
        var result = new StringBuilder();
        AppendIdentityPart(result, identities.Count);
        foreach (var identity in identities)
            result.AppendSegment(identity, true);
        return result.ToString();
    }
}
