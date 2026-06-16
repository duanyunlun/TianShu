using System.Globalization;
using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.AppHost.Tools.Runtime.Diagnostics;

internal sealed class KernelDiagnosticsTraceQueryService(KernelStateSqliteStore stateStore)
{
    public async Task<ExecutionTrace?> GetTraceAsync(
        string traceId,
        string? threadId,
        string? turnId,
        string? operationId,
        CancellationToken cancellationToken)
    {
        var resolved = ResolveQuery(traceId, threadId, turnId, operationId);
        var logs = await ReadCandidateLogsAsync(resolved, cancellationToken).ConfigureAwait(false);
        if (logs.Count == 0)
        {
            return null;
        }

        var auditRecords = new List<AuditRecord>(logs.Count);
        foreach (var log in logs)
        {
            var audit = BuildAuditRecord(log, resolved.OperationId);
            if (audit is not null)
            {
                auditRecords.Add(audit);
            }
        }

        if (auditRecords.Count == 0)
        {
            return null;
        }

        var effectiveTraceId = new ExecutionTraceId(Normalize(traceId) ?? $"trace:{resolved.TurnId ?? resolved.ThreadId ?? "apphost"}");
        var executionId = new ExecutionId($"execution:{effectiveTraceId.Value}");
        var startedAt = auditRecords.Min(static item => item.Timestamp);
        var completedAt = auditRecords.Max(static item => item.Timestamp);
        var succeeded = auditRecords.All(static item =>
            !string.Equals(ReadMetadataString(item.Metadata, "status"), "failed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Category, "diagnostics/operation/failed", StringComparison.OrdinalIgnoreCase));
        var attempt = new AttemptSummary(
            executionId,
            AttemptNumber: 1,
            Succeeded: succeeded,
            StartedAt: startedAt,
            CompletedAt: completedAt);

        return new ExecutionTrace(
            effectiveTraceId,
            executionId,
            [attempt],
            auditRecords.OrderBy(static item => item.Timestamp).ToArray());
    }

    public async Task<IReadOnlyList<AttemptSummary>> ListAttemptsAsync(string executionId, CancellationToken cancellationToken)
    {
        var traceId = Normalize(executionId);
        if (traceId?.StartsWith("execution:", StringComparison.OrdinalIgnoreCase) == true)
        {
            traceId = traceId["execution:".Length..];
        }

        if (string.IsNullOrWhiteSpace(traceId))
        {
            return [];
        }

        var trace = await GetTraceAsync(traceId, null, null, null, cancellationToken).ConfigureAwait(false);
        return trace?.Attempts ?? [];
    }

    private async Task<IReadOnlyList<KernelStateTurnLogRecord>> ReadCandidateLogsAsync(
        ResolvedDiagnosticsTraceQuery query,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(query.ThreadId))
        {
            var logs = await stateStore.ListTurnLogsAsync(query.ThreadId, cancellationToken).ConfigureAwait(false);
            return FilterLogs(logs, query).ToArray();
        }

        if (!string.IsNullOrWhiteSpace(query.TurnId))
        {
            var logs = await stateStore.ListTurnLogsByTurnIdAsync(query.TurnId, cancellationToken).ConfigureAwait(false);
            return FilterLogs(logs, query).ToArray();
        }

        return [];
    }

    private static IEnumerable<KernelStateTurnLogRecord> FilterLogs(
        IEnumerable<KernelStateTurnLogRecord> logs,
        ResolvedDiagnosticsTraceQuery query)
    {
        foreach (var log in logs)
        {
            if (!string.IsNullOrWhiteSpace(query.TurnId)
                && !string.Equals(log.TurnId, query.TurnId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(query.OperationId)
                || LogMatchesOperationId(log, query.OperationId))
            {
                yield return log;
            }
        }
    }

    private static AuditRecord? BuildAuditRecord(KernelStateTurnLogRecord log, string? operationIdFilter)
    {
        var metadata = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["threadId"] = StructuredValue.FromString(log.ThreadId),
            ["turnLogId"] = StructuredValue.FromNumber(log.Id.ToString(CultureInfo.InvariantCulture)),
            ["phase"] = StructuredValue.FromString(log.Phase),
            ["status"] = StructuredValue.FromString(log.Status),
        };
        AddString(metadata, "turnId", log.TurnId);
        AddString(metadata, "summary", log.Summary);

        try
        {
            using var document = JsonDocument.Parse(log.PayloadJson);
            var root = document.RootElement;
            var eventName = ReadString(root, "eventName") ?? log.Phase;
            var timestamp = ReadDateTimeOffset(root, "timestamp") ?? log.CreatedAt;
            AddString(metadata, "eventName", eventName);
            AddEnvelopeOperationMetadata(metadata, root);
            if (TryReadObject(root, "payload", out var payload))
            {
                AddStatsPayloadMetadata(metadata, eventName, payload);
            }

            return new AuditRecord(
                new AuditRecordId(BuildAuditRecordId(log, eventName)),
                NormalizeCategory(eventName),
                BuildAuditMessage(log, eventName, metadata),
                timestamp,
                new MetadataBag(metadata));
        }
        catch (JsonException ex)
        {
            if (!string.IsNullOrWhiteSpace(operationIdFilter))
            {
                return null;
            }

            AddString(metadata, "payloadError", ex.Message);
            AddNumber(metadata, "payloadChars", log.PayloadJson.Length);
            return new AuditRecord(
                new AuditRecordId($"turnlog:{log.Id.ToString(CultureInfo.InvariantCulture)}:unreadable"),
                "diagnostic_payload_unreadable",
                $"无法读取诊断 payload：{log.Phase}",
                log.CreatedAt,
                new MetadataBag(metadata));
        }
    }

    private static bool LogMatchesOperationId(KernelStateTurnLogRecord log, string operationId)
    {
        try
        {
            using var document = JsonDocument.Parse(log.PayloadJson);
            return TryReadObject(document.RootElement, "operation", out var operation)
                   && string.Equals(ReadString(operation, "operationId"), operationId, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AddEnvelopeOperationMetadata(Dictionary<string, StructuredValue> metadata, JsonElement root)
    {
        if (!TryReadObject(root, "operation", out var operation))
        {
            return;
        }

        AddString(metadata, "operationId", ReadString(operation, "operationId"));
        AddString(metadata, "operationName", ReadString(operation, "operationName"));
        AddString(metadata, "operationKind", ReadString(operation, "operationKind"));
        AddString(metadata, "operationThreadId", ReadString(operation, "threadId"));
        AddString(metadata, "operationTurnId", ReadString(operation, "turnId"));
        AddNumber(metadata, "requestSequence", ReadInt(operation, "requestSequence"));
    }

    private static void AddStatsPayloadMetadata(
        Dictionary<string, StructuredValue> metadata,
        string eventName,
        JsonElement payload)
    {
        if (string.Equals(eventName, DiagnosticStatisticsEventNames.ProviderRequestContextStats, StringComparison.Ordinal))
        {
            AddString(metadata, "model", ReadString(payload, "model"));
            AddString(metadata, "provider", ReadString(payload, "provider"));
            AddString(metadata, "transport", ReadString(payload, "transport"));
            AddString(metadata, "inputPropertyName", ReadString(payload, "inputPropertyName"));
            AddNumber(metadata, "serializedPayloadChars", ReadInt(payload, "serializedPayloadChars"));
            AddNumber(metadata, "estimatedPayloadTokens", ReadInt(payload, "estimatedPayloadTokens"));
            AddNumber(metadata, "providerRequestSequence", ReadInt(payload, "requestSequence"));
            AddCollectionMetadata(metadata, payload, "input", "input");
            AddCollectionMetadata(metadata, payload, "tools", "tools");
            if (TryReadObject(payload, "payloadArtifact", out var artifact))
            {
                AddString(metadata, "artifactId", ReadString(artifact, "artifactId"));
                AddString(metadata, "artifactKind", ReadString(artifact, "artifactKind"));
                AddString(metadata, "artifactFileName", ReadString(artifact, "fileName"));
                AddString(metadata, "artifactRelativePath", ReadString(artifact, "relativePath"));
                AddString(metadata, "artifactSha256", ReadString(artifact, "sha256"));
                AddNumber(metadata, "artifactBytes", ReadLong(artifact, "bytes"));
            }

            return;
        }

        if (string.Equals(eventName, DiagnosticStatisticsEventNames.ContextSlicingReport, StringComparison.Ordinal))
        {
            AddString(metadata, "model", ReadString(payload, "modelId"));
            AddString(metadata, "provider", ReadString(payload, "providerId"));
            AddNumber(metadata, "providerEffectiveContextLimitTokens", ReadInt(payload, "providerEffectiveContextLimitTokens"));
            AddNumber(metadata, "tianshuBudgetTokens", ReadInt(payload, "tianShuBudgetTokens"));
            AddNumber(metadata, "estimatedTotalTokens", ReadInt(payload, "estimatedTotalTokens"));
            AddNumber(metadata, "estimatedIncludedTokens", ReadInt(payload, "estimatedIncludedTokens"));
            AddNumber(metadata, "includedSegmentCount", ReadArrayCount(payload, "includedSegments"));
            AddNumber(metadata, "summarizedSegmentCount", ReadArrayCount(payload, "summarizedSegments"));
            AddNumber(metadata, "referenceOnlySegmentCount", ReadArrayCount(payload, "referenceOnlySegments"));
            AddNumber(metadata, "droppedSegmentCount", ReadArrayCount(payload, "droppedSegments"));
            AddString(metadata, "overBudgetReasonCode", ReadString(payload, "overBudgetReasonCode"));
            return;
        }

        if (string.Equals(eventName, DiagnosticStatisticsEventNames.RuntimeNotificationStats, StringComparison.Ordinal))
        {
            AddString(metadata, "method", ReadString(payload, "method"));
            AddString(metadata, "moduleName", ReadString(payload, "moduleName"));
            AddString(metadata, "itemId", ReadString(payload, "itemId"));
            AddString(metadata, "callId", ReadString(payload, "callId"));
            AddNumber(metadata, "requestId", ReadLong(payload, "requestId"));
            AddNumber(metadata, "serializedPayloadChars", ReadInt(payload, "serializedPayloadChars"));
            AddNumber(metadata, "estimatedPayloadTokens", ReadInt(payload, "estimatedPayloadTokens"));
        }
    }

    private static void AddCollectionMetadata(
        Dictionary<string, StructuredValue> metadata,
        JsonElement payload,
        string propertyName,
        string prefix)
    {
        if (!TryReadObject(payload, propertyName, out var collection))
        {
            return;
        }

        AddNumber(metadata, $"{prefix}Count", ReadInt(collection, "count"));
        AddNumber(metadata, $"{prefix}Chars", ReadInt(collection, "chars"));
        AddNumber(metadata, $"{prefix}EstimatedTokens", ReadInt(collection, "estimatedTokens"));
    }

    private static string BuildAuditRecordId(KernelStateTurnLogRecord log, string eventName)
    {
        var safeEventName = eventName.Replace('/', '_');
        return $"turnlog:{log.Id.ToString(CultureInfo.InvariantCulture)}:{safeEventName}";
    }

    private static string NormalizeCategory(string eventName)
        => string.Equals(eventName, DiagnosticStatisticsEventNames.ProviderRequestContextStats, StringComparison.Ordinal)
            ? "provider_request"
            : string.Equals(eventName, DiagnosticStatisticsEventNames.ContextSlicingReport, StringComparison.Ordinal)
                ? "context_slicing"
                : eventName;

    private static string BuildAuditMessage(
        KernelStateTurnLogRecord log,
        string eventName,
        IReadOnlyDictionary<string, StructuredValue> metadata)
    {
        if (string.Equals(eventName, DiagnosticStatisticsEventNames.ProviderRequestContextStats, StringComparison.Ordinal))
        {
            var sequence = ReadMetadataString(metadata, "providerRequestSequence") ?? ReadMetadataString(metadata, "requestSequence");
            var model = ReadMetadataString(metadata, "model");
            var tokens = ReadMetadataString(metadata, "estimatedPayloadTokens");
            var artifact = ReadMetadataString(metadata, "artifactFileName");
            return $"Provider 请求统计 #{sequence ?? "?"}，模型 {model ?? "<unknown>"}，估算 tokens {tokens ?? "?"}，artifact {artifact ?? "<none>"}";
        }

        if (string.Equals(eventName, DiagnosticStatisticsEventNames.ContextSlicingReport, StringComparison.Ordinal))
        {
            var included = ReadMetadataString(metadata, "estimatedIncludedTokens");
            var total = ReadMetadataString(metadata, "estimatedTotalTokens");
            return $"上下文裁切统计：included {included ?? "?"} / total {total ?? "?"} tokens";
        }

        return log.Summary ?? eventName;
    }

    private static ResolvedDiagnosticsTraceQuery ResolveQuery(
        string traceId,
        string? threadId,
        string? turnId,
        string? operationId)
    {
        var resolvedThreadId = Normalize(threadId);
        var resolvedTurnId = Normalize(turnId);
        var resolvedOperationId = Normalize(operationId);
        var normalizedTraceId = Normalize(traceId);

        if (!string.IsNullOrWhiteSpace(normalizedTraceId))
        {
            var text = normalizedTraceId!;
            if (text.StartsWith("trace:", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = text["trace:".Length..];
                if (string.IsNullOrWhiteSpace(resolvedTurnId))
                {
                    resolvedTurnId = Normalize(suffix);
                }
            }
            else if (text.StartsWith("turn:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(resolvedTurnId))
            {
                resolvedTurnId = Normalize(text["turn:".Length..]);
            }
        }

        return new ResolvedDiagnosticsTraceQuery(resolvedThreadId, resolvedTurnId, resolvedOperationId);
    }

    private static string? ReadMetadataString(MetadataBag metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value.GetString() : null;

    private static string? ReadMetadataString(IReadOnlyDictionary<string, StructuredValue> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value.GetString() : null;

    private static void AddString(Dictionary<string, StructuredValue> metadata, string key, string? value)
    {
        var normalized = Normalize(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            metadata[key] = StructuredValue.FromString(normalized);
        }
    }

    private static void AddNumber(Dictionary<string, StructuredValue> metadata, string key, int? value)
    {
        if (value.HasValue)
        {
            metadata[key] = StructuredValue.FromNumber(value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddNumber(Dictionary<string, StructuredValue> metadata, string key, long? value)
    {
        if (value.HasValue)
        {
            metadata[key] = StructuredValue.FromNumber(value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool TryReadObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (TryGetProperty(element, propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : int.TryParse(property.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value)
            ? value
            : long.TryParse(property.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static int? ReadArrayCount(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.GetArrayLength()
            : null;

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var property)
           && property.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private sealed record ResolvedDiagnosticsTraceQuery(string? ThreadId, string? TurnId, string? OperationId);
}
