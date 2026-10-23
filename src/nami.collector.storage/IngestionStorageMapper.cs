namespace Nami.Collector.Storage;

internal static class IngestionStorageMapper
{
    private const int MaxPersistedLogBodyUtf8Bytes = 64 * 1024;
    private const string TruncatedLogBodySuffix = "...[truncated]";

    public static List<SpanStorageRow> ToSpanStorageRows(TraceIngestionBatch batch, string? authenticatedProjectId = null)
    {
        var rows = new List<SpanStorageRow>(batch.Spans.Count);

        foreach (var span in batch.Spans)
            rows.Add(ToSpanStorageRow(span, authenticatedProjectId));

        return rows;
    }

    public static List<LogStorageRow> ToLogStorageRows(LogIngestionBatch batch, string? authenticatedProjectId = null)
    {
        var rows = new List<LogStorageRow>(batch.Logs.Count);

        foreach (var log in batch.Logs)
            rows.Add(ToLogStorageRow(log, authenticatedProjectId));

        return rows;
    }

    private static SpanStorageRow ToSpanStorageRow(SpanIngestionRecord span, string? authenticatedProjectId)
    {
        if (span.EndTimeUnixNano < span.StartTimeUnixNano)
            throw new InvalidDataException("Span end time must not precede its start.");
        var durationNs = span.EndTimeUnixNano - span.StartTimeUnixNano;
        var projection = StorageAttributeProjection.ExtractSpanHotAttributes(span);
        var resourceEntityRefsJson = StorageJson.SerializeResourceEntityRefs(span.ResourceEntityRefs);

        return new SpanStorageRow
        {
            ProjectId = ProjectScope.ForIngest(span.ProjectIdHint, authenticatedProjectId),
            SpanId = span.SpanId,
            TraceId = span.TraceId,
            ParentSpanId = string.IsNullOrEmpty(span.ParentSpanId) ? null : span.ParentSpanId,
            SessionId = projection.SessionId,
            Name = span.Name,
            Kind = (byte)(span.Kind ?? 0),
            StartTimeUnixNano = span.StartTimeUnixNano,
            EndTimeUnixNano = span.EndTimeUnixNano,
            DurationNs = durationNs,
            StatusCode = (byte)(span.StatusCode ?? 0),
            ServiceName = span.ServiceName,
            GenAiProviderName = projection.GenAiProviderName,
            GenAiOperationName = projection.GenAiOperationName,
            GenAiOutputType = projection.GenAiOutputType,
            GenAiRequestModel = projection.GenAiRequestModel,
            GenAiResponseModel = projection.GenAiResponseModel,
            GenAiInputTokens = projection.GenAiInputTokens,
            GenAiOutputTokens = projection.GenAiOutputTokens,
            GenAiCacheReadInputTokens = projection.GenAiCacheReadInputTokens,
            GenAiCacheWriteInputTokens = projection.GenAiCacheWriteInputTokens,
            GenAiReasoningTokens = projection.GenAiReasoningTokens,
            AttributesJson = PersistedAttributePolicy.SerializeSpanAttributes(span.Attributes, projection),
            ResourceJson = PersistedAttributePolicy.SerializeAttributes(span.ResourceAttributes),
            ResourceEntityRefsJson = resourceEntityRefsJson,
            SchemaUrl = span.SchemaUrl,
            StatusMessage = span.StatusMessage,
            EventsJson = SpanChildStorage.SerializeEvents(span.Events),
            LinksJson = SpanChildStorage.SerializeLinks(span.Links),
            ScopeName = span.ScopeName,
            ScopeVersion = span.ScopeVersion,
            ScopeSchemaUrl = span.ScopeSchemaUrl,
            ScopeAttributesJson = PersistedAttributePolicy.SerializeScopeAttributes(span.ScopeAttributes),
            ScopeDroppedAttributesCount = span.ScopeDroppedAttributesCount,
            ResourceSchemaUrl = span.ResourceSchemaUrl,
            ResourceDroppedAttributesCount = span.ResourceDroppedAttributesCount,
            TraceState = span.TraceState,
            Flags = span.Flags,
            DroppedAttributesCount = span.DroppedAttributesCount,
            DroppedEventsCount = span.DroppedEventsCount,
            DroppedLinksCount = span.DroppedLinksCount
        };
    }

    private static LogStorageRow ToLogStorageRow(LogIngestionRecord log, string? authenticatedProjectId)
    {
        var projectId = ProjectScope.ForIngest(log.ProjectIdHint, authenticatedProjectId);
        var severityText = log.SeverityText;
        var severityNumber = (byte)log.SeverityNumber;
        var sessionId = log.Attributes.GetSessionId(log.ScopeName, log.ServiceName);
        // Identity uses complete canonical content; the retention rendering may be bounded.
        // Omitted suffixes contribute only to the digest and are never stored.
        var storedBody = StoredRecordMapper.ToStored(log.Body);
        var originalBody = storedBody is null ? null : EncodeBody(storedBody);
        var body = ToPersistedLogBody(storedBody, originalBody);
        var attributesJson = PersistedAttributePolicy.SerializeAttributes(log.Attributes);
        var resourceJson = PersistedAttributePolicy.SerializeAttributes(log.ResourceAttributes);
        var resourceEntityRefsJson = StorageJson.SerializeResourceEntityRefs(log.ResourceEntityRefs);
        var scopeAttributesJson = PersistedAttributePolicy.SerializeScopeAttributes(log.ScopeAttributes);
        var resourceEntityRefsIdentity = GetResourceEntityRefsIdentity(
            log.ResourceEntityRefs,
            log.ResourceAttributes);

        return new LogStorageRow
        {
            ProjectId = projectId,
            LogId = StableStorageId("log", builder =>
            {
                builder.AppendSegment(projectId, terminate: true);
                builder.AppendSegment(log.TraceId, terminate: true);
                builder.AppendSegment(log.SpanId, terminate: true);
                builder.AppendSegment(log.EventName, terminate: true);
                builder.AppendSegment(sessionId, terminate: true);
                AppendIdentityPart(builder, log.TimeUnixNano);
                AppendIdentityPart(builder, log.ObservedTimeProvided ? log.ObservedTimeUnixNano : null);
                if (log.UntimedRequestOrdinal is { } ordinal) AppendIdentityPart(builder, ordinal);
                AppendIdentityPart(builder, severityNumber);
                builder.AppendSegment(severityText, terminate: true);
                builder.AppendSegment(originalBody, terminate: true);
                builder.AppendSegment(log.ServiceName, terminate: true);
                builder.AppendSegment(attributesJson, terminate: true);
                builder.AppendSegment(resourceJson, terminate: true);
                builder.AppendSegment(log.ScopeName, terminate: true);
                builder.AppendSegment(log.ScopeVersion, terminate: true);
                builder.AppendSegment(log.ScopeSchemaUrl, terminate: true);
                builder.AppendSegment(scopeAttributesJson, terminate: true);
                AppendIdentityPart(builder, log.ScopeDroppedAttributesCount);
                builder.AppendSegment(log.ResourceSchemaUrl, terminate: true);
                AppendIdentityPart(builder, log.ResourceDroppedAttributesCount);
                AppendIdentityPart(builder, log.Flags);
                AppendIdentityPart(builder, log.DroppedAttributesCount);
                if (!string.IsNullOrEmpty(resourceEntityRefsIdentity))
                    builder.AppendSegment(resourceEntityRefsIdentity, terminate: true);
            }),
            TraceId = log.TraceId,
            SpanId = log.SpanId,
            EventName = log.EventName,
            SessionId = sessionId,
            TimeUnixNano = log.TimeUnixNano,
            ObservedTimeUnixNano = log.ObservedTimeUnixNano,
            SeverityNumber = severityNumber,
            SeverityText = severityText,
            Body = body,
            ServiceName = log.ServiceName,
            AttributesJson = attributesJson,
            ResourceJson = resourceJson,
            ResourceEntityRefsJson = resourceEntityRefsJson,
            ScopeName = log.ScopeName,
            ScopeVersion = log.ScopeVersion,
            ScopeSchemaUrl = log.ScopeSchemaUrl,
            ScopeAttributesJson = scopeAttributesJson,
            ScopeDroppedAttributesCount = log.ScopeDroppedAttributesCount,
            ResourceSchemaUrl = log.ResourceSchemaUrl,
            ResourceDroppedAttributesCount = log.ResourceDroppedAttributesCount,
            Flags = log.Flags,
            DroppedAttributesCount = log.DroppedAttributesCount,
            ReceivedTimeUnixNano = log.ReceivedTimeUnixNano
        };
    }

    private static string StableStorageId(string prefix, Action<StringBuilder> appendParts)
    {
        var builder = new StringBuilder(512);
        builder.Append(prefix).Append('\n');
        appendParts(builder);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return prefix + "_" + Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    private static void AppendIdentityPart(StringBuilder builder, byte value) =>
        builder.AppendSegment(value.ToString(CultureInfo.InvariantCulture), terminate: true);

    private static void AppendIdentityPart(StringBuilder builder, int value) =>
        builder.AppendSegment(value.ToString(CultureInfo.InvariantCulture), terminate: true);

    private static void AppendIdentityPart(StringBuilder builder, ulong value) =>
        builder.AppendSegment(value.ToString(CultureInfo.InvariantCulture), terminate: true);

    private static void AppendIdentityPart(StringBuilder builder, ulong? value) =>
        builder.AppendSegment(value?.ToString(CultureInfo.InvariantCulture), terminate: true);

    /// <summary>
    /// The stored body is a persisted attribute value, so a reader decodes it as one. A string body
    /// is truncated before encoding; any other body that exceeds the limit is stored as its
    /// truncated canonical text, so the column always holds valid JSON.
    /// </summary>
    private static string? ToPersistedLogBody(StoredAttributeValue? body, string? original)
    {
        if (body is null)
            return null;

        if (body is StoredAttributeValue.StringValue text)
            return Encoding.UTF8.GetByteCount(text.Value) <= MaxPersistedLogBodyUtf8Bytes
                ? original : EncodeBody(new StoredAttributeValue.StringValue(TruncateBodyText(text.Value)));

        var encoded = original ?? throw new InvalidDataException("Nonempty log bodies require canonical content.");
        return FitsBodyBudget(encoded)
            ? encoded
            : EncodeBody(new StoredAttributeValue.StringValue(TruncateBodyText(StoredAttributeValue.CanonicalText(body))));
    }

    private static string EncodeBody(StoredAttributeValue body) =>
        JsonSerializer.Serialize(body, StorageJsonSerializerContext.Default.StoredAttributeValue);

    private static bool FitsBodyBudget(string encoded)
    {
        if (Encoding.UTF8.GetByteCount(encoded) <= MaxPersistedLogBodyUtf8Bytes) return true;
        // Match the string-body budget: count decoded UTF-8 text, not JSON escape
        // expansion. A surrogate-pair escape must not erase an otherwise bounded map.
        // Structural bytes still count, so a large collection cannot bypass the cap.
        using var document = JsonDocument.Parse(encoded);
        var remaining = MaxPersistedLogBodyUtf8Bytes;
        return FitsBodyBudget(document.RootElement, ref remaining);
    }

    private static bool FitsBodyBudget(JsonElement value, ref int remaining)
    {
        if (value.ValueKind is JsonValueKind.String)
        {
            var size = Encoding.UTF8.GetByteCount(value.GetString()!) + 2;
            if (size > remaining) return false;
            remaining -= size;
            return true;
        }
        if (value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
        {
            var size = value.GetRawText().Length;
            if (size > remaining) return false;
            remaining -= size;
            return true;
        }
        if (remaining < 2) return false;
        remaining -= 2;
        var separator = 0;
        if (value.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (separator > remaining) return false;
                remaining -= separator;
                if (!FitsBodyBudget(item, ref remaining)) return false;
                separator = 1;
            }
        }
        else
        {
            foreach (var property in value.EnumerateObject())
            {
                var size = Encoding.UTF8.GetByteCount(property.Name) + 3 + separator;
                if (size > remaining) return false;
                remaining -= size;
                if (!FitsBodyBudget(property.Value, ref remaining)) return false;
                separator = 1;
            }
        }
        return true;
    }

    private static string TruncateBodyText(string raw)
    {
        if (Encoding.UTF8.GetByteCount(raw) <= MaxPersistedLogBodyUtf8Bytes)
            return raw;

        var remainingBytes = MaxPersistedLogBodyUtf8Bytes - Encoding.UTF8.GetByteCount(TruncatedLogBodySuffix);
        var builder = new StringBuilder(Math.Min(raw.Length, MaxPersistedLogBodyUtf8Bytes));
        foreach (var rune in raw.EnumerateRunes())
        {
            if (rune.Utf8SequenceLength > remainingBytes)
                break;

            builder.Append(rune.ToString());
            remainingBytes -= rune.Utf8SequenceLength;
        }

        return builder.Append(TruncatedLogBodySuffix).ToString();
    }

    private static string? GetResourceEntityRefsIdentity(
        IReadOnlyList<ResourceEntityRefIngestionRecord> entityRefs,
        IReadOnlyDictionary<string, OtlpAttributeValue> resourceAttributes)
    {
        if (entityRefs.Count is 0) return null;

        var identities = new List<string>(entityRefs.Count);
        foreach (var entityRef in entityRefs)
        {
            var identity = new StringBuilder();
            identity.AppendSegment(entityRef.Type, terminate: true);
            var idKeys = entityRef.IdKeys.Order(StringComparer.Ordinal).ToArray();
            AppendIdentityPart(identity, idKeys.Length);
            foreach (var key in idKeys)
            {
                if (!resourceAttributes.TryGetValue(key, out var value))
                {
                    throw new InvalidDataException(
                        $"Resource entity id_key '{key}' does not reference a persisted resource attribute.");
                }

                identity.AppendSegment(key, terminate: true);
                identity.AppendSegment(StoredAttributeValue.CanonicalText(StoredRecordMapper.ToStored(value)), terminate: true);
            }
            identities.Add(identity.ToString());
        }

        identities.Sort(StringComparer.Ordinal);
        var result = new StringBuilder();
        AppendIdentityPart(result, identities.Count);
        foreach (var identity in identities)
            result.AppendSegment(identity, terminate: true);
        return result.ToString();
    }

}
