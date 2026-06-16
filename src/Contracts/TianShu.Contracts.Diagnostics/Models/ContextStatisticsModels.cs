namespace TianShu.Contracts.Diagnostics;

/// <summary>
/// 诊断统计事件名常量。
/// Diagnostic statistics event name constants.
/// </summary>
public static class DiagnosticStatisticsEventNames
{
    public const string ContextSlicingReport = "turn/context_slicing/report";

    public const string ProviderRequestContextStats = "turn/provider_request/context_stats";

    public const string RuntimeNotificationStats = "runtime/notification/stats";
}

/// <summary>
/// Provider 请求上下文统计。
/// Provider request context statistics.
/// </summary>
public sealed record ProviderRequestContextStats
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string EventName { get; init; } = DiagnosticStatisticsEventNames.ProviderRequestContextStats;

    public required string ThreadId { get; init; }

    public required string TurnId { get; init; }

    public required int RequestSequence { get; init; }

    public string? Model { get; init; }

    public string? Provider { get; init; }

    public required string Transport { get; init; }

    public string? InputPropertyName { get; init; }

    public IReadOnlyList<string> TopLevelKeys { get; init; } = Array.Empty<string>();

    public required int SerializedPayloadChars { get; init; }

    public required int EstimatedPayloadTokens { get; init; }

    public ProviderRequestTextFieldStats? Instructions { get; init; }

    public ProviderRequestCollectionStats? Input { get; init; }

    public ProviderRequestCollectionStats? Tools { get; init; }

    public DiagnosticArtifactManifest? PayloadArtifact { get; init; }
}

/// <summary>
/// Provider 请求文本字段统计。
/// Provider request text field statistics.
/// </summary>
public sealed record ProviderRequestTextFieldStats
{
    public required string Key { get; init; }

    public required int Chars { get; init; }

    public required int EstimatedTokens { get; init; }
}

/// <summary>
/// Provider 请求集合字段统计。
/// Provider request collection field statistics.
/// </summary>
public sealed record ProviderRequestCollectionStats
{
    public required string Key { get; init; }

    public required int Count { get; init; }

    public required int Chars { get; init; }

    public required int EstimatedTokens { get; init; }

    public IReadOnlyList<ProviderRequestItemStats> Items { get; init; } = Array.Empty<ProviderRequestItemStats>();
}

/// <summary>
/// Provider 请求集合条目统计。
/// Provider request collection item statistics.
/// </summary>
public sealed record ProviderRequestItemStats
{
    public required int Index { get; init; }

    public string? Role { get; init; }

    public string? Type { get; init; }

    public required int ContentItemCount { get; init; }

    public required int Chars { get; init; }

    public required int EstimatedTokens { get; init; }
}

/// <summary>
/// Runtime notification 诊断统计，供 tool / governance / presentation 等散落事件统一归一。
/// Runtime notification diagnostic statistics used to normalize scattered tool/governance/presentation events.
/// </summary>
public sealed record RuntimeNotificationStats
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string EventName { get; init; } = DiagnosticStatisticsEventNames.RuntimeNotificationStats;

    public required string ModuleName { get; init; }

    public required string Method { get; init; }

    public string? ThreadId { get; init; }

    public string? TurnId { get; init; }

    public string? ItemId { get; init; }

    public string? CallId { get; init; }

    public long? RequestId { get; init; }

    public string? OperationCategory { get; init; }

    public string? ParameterSummary { get; init; }

    public string? PermissionDecision { get; init; }

    public string? ExecutionResult { get; init; }

    public string? ArtifactReference { get; init; }

    public string? RiskSource { get; init; }

    public string? PolicyRule { get; init; }

    public string? UserDecision { get; init; }

    public string? MemoryAuditId { get; init; }

    public string? DiagnosticOperationId { get; init; }

    public IReadOnlyList<string> PayloadTopLevelKeys { get; init; } = Array.Empty<string>();

    public required int SerializedPayloadChars { get; init; }

    public required int EstimatedPayloadTokens { get; init; }
}

/// <summary>
/// 记忆 provider 解析诊断统计。
/// Diagnostic statistics for memory provider resolution.
/// </summary>
public sealed record MemoryProviderResolutionStats
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string EventName { get; init; } = "memory/provider_resolution/stats";

    public string? MemorySpaceId { get; init; }

    public required string Capability { get; init; }

    public required int MatchedProviderCount { get; init; }

    public IReadOnlyList<string> DegradedProviders { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 记忆 mutation 诊断统计。
/// Diagnostic statistics for memory mutations.
/// </summary>
public sealed record MemoryMutationStats
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string EventName { get; init; } = "memory/mutation/stats";

    public required string OperationName { get; init; }

    public string? MemorySpaceId { get; init; }

    public string? MemoryRecordId { get; init; }

    public string? Key { get; init; }

    public required string Capability { get; init; }

    public required bool Success { get; init; }

    public string? Effect { get; init; }

    public string? LifecycleStatus { get; init; }

    public string? DegradedReason { get; init; }

    public string? UnsupportedCapability { get; init; }

    public string? GovernanceCheckpointKind { get; init; }

    public string? RiskSource { get; init; }

    public string? ApprovalQueueProjection { get; init; }

    public string? UserDecision { get; init; }

    public string? ExecutionResult { get; init; }
}
