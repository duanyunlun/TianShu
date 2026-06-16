using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// 上下文裁切运行时辅助件，集中配置读取、诊断投影与协议输入收口。
/// Runtime helpers for context slicing configuration, diagnostics, and protocol input routing.
/// </summary>
internal static class ContextSlicingRuntimeHelpers
{
    public const string DiagnosticsNotificationMethod = "turn/context_slicing/report";

    public static ContextBudgetProfile ResolveConfiguredBudgetProfile(Dictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var profile = ContextBudgetProfile.Default;
        return profile with
        {
            SoftBudgetTokens = ReadPositiveInt(config, "default_budget_tokens", profile.SoftBudgetTokens),
            ExpandedBudgetTokens = ReadPositiveInt(config, "expanded_budget_tokens", profile.ExpandedBudgetTokens),
            SafetyMarginTokens = ReadNonNegativeInt(config, "safety_margin_tokens", profile.SafetyMarginTokens),
            ToolOutputBudgetTokens = ReadPositiveInt(config, "tool_output_budget_tokens", profile.ToolOutputBudgetTokens),
            MemoryOverlayBudgetTokens = ReadPositiveInt(config, "memory_overlay_budget_tokens", profile.MemoryOverlayBudgetTokens),
            RecentTurnsBudgetTokens = ReadPositiveInt(config, "history_raw_turn_budget_tokens", profile.RecentTurnsBudgetTokens),
            SummaryBudgetTokens = ReadPositiveInt(config, "summary_budget_tokens", profile.SummaryBudgetTokens),
            ArtifactSnippetBudgetTokens = ReadPositiveInt(config, "artifact_snippet_budget_tokens", profile.ArtifactSnippetBudgetTokens),
        };
    }

    public static object BuildDiagnosticPayload(
        ContextSlicingReport report,
        int requestSequence,
        string? model,
        string? provider)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new
        {
            report.ThreadId,
            report.TurnId,
            requestSequence,
            model,
            provider,
            report.ProviderEffectiveContextLimitTokens,
            report.TianShuBudgetTokens,
            report.EstimatedTotalTokens,
            report.EstimatedIncludedTokens,
            overBudgetReasonCode = report.OverBudgetReasonCode?.ToString(),
            included = report.IncludedSegments.Select(ToDiagnosticEntry).ToArray(),
            summarized = report.SummarizedSegments.Select(ToDiagnosticEntry).ToArray(),
            referenceOnly = report.ReferenceOnlySegments.Select(ToDiagnosticEntry).ToArray(),
            dropped = report.DroppedSegments.Select(ToDiagnosticEntry).ToArray(),
        };
    }

    public static ContextSlicingReport WithRuntimeIdentity(
        ContextSlicingReport report,
        string threadId,
        string turnId,
        string? model,
        string? provider)
    {
        ArgumentNullException.ThrowIfNull(report);

        return report with
        {
            ThreadId = threadId,
            TurnId = turnId,
            ModelId = model,
            ProviderId = provider,
        };
    }

    public static ContextSegment CreateMemoryOverlaySegment(
        string id,
        string text,
        string sourceId,
        string? contextSignature,
        int estimatedTokens = 0)
        => ContextSegmentFactories.CreateMemoryOverlay(
            id,
            text,
            [new ContextSourceRef(ContextSourceKind.Memory, sourceId)],
            contextSignature,
            estimatedTokens: estimatedTokens);

    public static List<Dictionary<string, object?>> SliceProviderMessages(
        IReadOnlyList<Dictionary<string, object?>> messages,
        string? threadId = null,
        string? turnId = null,
        ContextBudgetProfile? budgetProfile = null,
        int? providerEffectiveContextLimitTokens = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var segments = new List<ContextSegment>(messages.Count);
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var role = message.TryGetValue("role", out var rawRole) ? rawRole as string : null;
            var (kind, priority, policy, sourceKind) = ResolveMessageSegmentShape(role, index, messages.Count);
            segments.Add(new ContextSegment
            {
                Id = $"provider-message-{index}",
                Kind = kind,
                Priority = priority,
                RetentionPolicy = policy,
                StructuredContent = message,
                EstimatedTokens = Math.Max(1, JsonSerializer.Serialize(message).Length / 3),
                Confidence = 1.0d,
                AuthorityWeight = priority == ContextSegmentPriority.Critical ? 100 : 30,
                RecencyWeight = index == messages.Count - 1 ? 100 : index,
                RelevanceScore = index == messages.Count - 1 ? 1.0d : 0.75d,
                SourceRefs = [new ContextSourceRef(sourceKind, Id: $"provider-message-{index}")],
                Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["providerMessageOrder"] = index,
                },
            });
        }

        var result = new ContextSlicePlanner(new ApproximateContextTokenEstimator()).Plan(new ContextSliceRequest
        {
            ThreadId = threadId ?? "current-thread",
            TurnId = turnId ?? "current-turn",
            BudgetProfile = budgetProfile ?? ContextBudgetProfile.Default,
            ProviderEffectiveContextLimitTokens = providerEffectiveContextLimitTokens,
            CandidateSegments = segments,
        });

        return result.IncludedSegments
            .OrderBy(static segment => segment.Metadata.TryGetValue("providerMessageOrder", out var rawOrder) && rawOrder is int order
                ? order
                : int.MaxValue)
            .Select(static segment => segment.StructuredContent)
            .OfType<Dictionary<string, object?>>()
            .ToList();
    }

    private static object ToDiagnosticEntry(ContextSegmentReportEntry entry)
        => new
        {
            entry.SegmentId,
            kind = entry.Kind.ToString(),
            priority = entry.Priority.ToString(),
            entry.EstimatedTokens,
            droppedReason = entry.DroppedReason?.ToString(),
            sourceRefs = entry.SourceRefs.Select(static source => new
            {
                kind = source.Kind.ToString(),
                source.Id,
                source.ThreadId,
                source.TurnId,
                source.ItemId,
                source.Path,
                source.StartLine,
                source.EndLine,
            }).ToArray(),
        };

    private static int ReadPositiveInt(Dictionary<string, object?> config, string leafName, int fallback)
    {
        var value = ReadConfiguredInt(config, leafName, fallback);
        return value > 0 ? value : fallback;
    }

    private static int ReadNonNegativeInt(Dictionary<string, object?> config, string leafName, int fallback)
    {
        var value = ReadConfiguredInt(config, leafName, fallback);
        return value >= 0 ? value : fallback;
    }

    private static int ReadConfiguredInt(Dictionary<string, object?> config, string leafName, int fallback)
    {
        if (TryReadContextValue(config, leafName, out var rawValue)
            && TryReadInt(rawValue, out var configuredValue))
        {
            return configuredValue;
        }

        return fallback;
    }

    private static bool TryReadContextValue(Dictionary<string, object?> config, string leafName, out object? value)
    {
        if (TryReadNestedValueExact(config, ["context", leafName], out value))
        {
            return true;
        }

        return TryReadValueExact(config, $"context.{leafName}", out value);
    }

    private static bool TryReadValueExact(Dictionary<string, object?> config, string propertyName, out object? value)
        => config.TryGetValue(propertyName, out value);

    private static bool TryReadNestedValueExact(
        Dictionary<string, object?> config,
        IReadOnlyList<string> propertyPath,
        out object? value)
    {
        var current = config;
        for (var index = 0; index < propertyPath.Count; index++)
        {
            if (!TryReadValueExact(current, propertyPath[index], out value))
            {
                return false;
            }

            if (index == propertyPath.Count - 1)
            {
                return true;
            }

            if (!TryAsDictionary(value, out current))
            {
                value = null;
                return false;
            }
        }

        value = null;
        return false;
    }

    private static bool TryAsDictionary(object? value, out Dictionary<string, object?> dictionary)
    {
        switch (value)
        {
            case Dictionary<string, object?> concrete:
                dictionary = concrete;
                return true;
            case IReadOnlyDictionary<string, object?> readOnly:
                dictionary = readOnly.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IDictionary<string, object?> mutable:
                dictionary = mutable.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                dictionary = ConvertJsonObject(element);
                return true;
            default:
                dictionary = null!;
                return false;
        }
    }

    private static bool TryReadInt(object? value, out int intValue)
    {
        switch (value)
        {
            case int typedInt:
                intValue = typedInt;
                return true;
            case long typedLong when typedLong is >= int.MinValue and <= int.MaxValue:
                intValue = (int)typedLong;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsedInt):
                intValue = parsedInt;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsedIntFromString):
                intValue = parsedIntFromString;
                return true;
            case string text when int.TryParse(text, out var parsedIntFromText):
                intValue = parsedIntFromText;
                return true;
            default:
                intValue = default;
                return false;
        }
    }

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonValue(property.Value);
        }

        return dictionary;
    }

    private static object? ConvertJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue
                    : element.GetRawText(),
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };

    private static (
        ContextSegmentKind Kind,
        ContextSegmentPriority Priority,
        ContextRetentionPolicy RetentionPolicy,
        ContextSourceKind SourceKind) ResolveMessageSegmentShape(string? role, int index, int count)
    {
        if (string.Equals(role, "developer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
        {
            return (
                ContextSegmentKind.DeveloperInstruction,
                ContextSegmentPriority.Critical,
                ContextRetentionPolicy.MustKeep,
                ContextSourceKind.Instruction);
        }

        if (index == count - 1 && string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return (
                ContextSegmentKind.CurrentUserInput,
                ContextSegmentPriority.Critical,
                ContextRetentionPolicy.MustKeep,
                ContextSourceKind.UserInput);
        }

        return (
            ContextSegmentKind.RecentTurn,
            ContextSegmentPriority.High,
            ContextRetentionPolicy.SummarizeIfDropped,
            ContextSourceKind.ConversationHistory);
    }
}
