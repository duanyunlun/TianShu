namespace TianShu.AppHost.Tools.Runtime;

internal sealed record ContextSourceRef(
    ContextSourceKind Kind,
    string? Id = null,
    string? ThreadId = null,
    string? TurnId = null,
    string? ItemId = null,
    string? Path = null,
    int? StartLine = null,
    int? EndLine = null);

internal sealed record ContextTokenEstimate(
    int Tokens,
    string EstimatorName,
    bool IsApproximate = true);

internal sealed record ContextSegment
{
    public required string Id { get; init; }

    public required ContextSegmentKind Kind { get; init; }

    public required ContextSegmentPriority Priority { get; init; }

    public required ContextRetentionPolicy RetentionPolicy { get; init; }

    public string? Text { get; init; }

    public object? StructuredContent { get; init; }

    public IReadOnlyList<ContextSourceRef> SourceRefs { get; init; } = [];

    public int EstimatedTokens { get; init; }

    public double Confidence { get; init; } = 1.0d;

    public int AuthorityWeight { get; init; }

    public int RecencyWeight { get; init; }

    public double RelevanceScore { get; init; } = 1.0d;

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}

internal sealed record ContextBudgetProfile
{
    public const int DefaultSoftBudgetTokens = 50_000;
    public const int DefaultExpandedBudgetTokens = 80_000;
    public const int DefaultSafetyMarginTokens = 5_000;

    public string Name { get; init; } = "default";

    public int SoftBudgetTokens { get; init; } = DefaultSoftBudgetTokens;

    public int ExpandedBudgetTokens { get; init; } = DefaultExpandedBudgetTokens;

    public int SafetyMarginTokens { get; init; } = DefaultSafetyMarginTokens;

    public int CoreInstructionBudgetTokens { get; init; } = 8_000;

    public int CurrentTaskBudgetTokens { get; init; } = 8_000;

    public int RecentTurnsBudgetTokens { get; init; } = 15_000;

    public int ToolOutputBudgetTokens { get; init; } = 12_000;

    public int MemoryOverlayBudgetTokens { get; init; } = 6_000;

    public int ArtifactSnippetBudgetTokens { get; init; } = 10_000;

    public int SummaryBudgetTokens { get; init; } = 6_000;

    public static ContextBudgetProfile Default { get; } = new();

    public int GetEffectiveSoftBudgetTokens(int? providerEffectiveContextLimitTokens)
    {
        if (providerEffectiveContextLimitTokens is not > 0)
        {
            return Math.Max(1, SoftBudgetTokens);
        }

        var providerBudget = Math.Max(1, providerEffectiveContextLimitTokens.Value - Math.Max(0, SafetyMarginTokens));
        return Math.Max(1, Math.Min(SoftBudgetTokens, providerBudget));
    }
}

internal sealed record ContextSliceRequest
{
    public required string ThreadId { get; init; }

    public required string TurnId { get; init; }

    public string? ModelId { get; init; }

    public string? ProviderId { get; init; }

    public int? ProviderEffectiveContextLimitTokens { get; init; }

    public ContextBudgetProfile BudgetProfile { get; init; } = ContextBudgetProfile.Default;

    public string? ContextSignature { get; init; }

    public IReadOnlyDictionary<string, object?> CurrentTaskMetadata { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public IReadOnlyList<ContextSegment> CandidateSegments { get; init; } = [];

    public OverBudgetReasonCode? RequestedOverBudgetReason { get; init; }
}

internal sealed record DroppedContextSegment(
    ContextSegment Segment,
    DroppedContextReason Reason);

internal sealed record ContextSegmentReportEntry(
    string SegmentId,
    ContextSegmentKind Kind,
    ContextSegmentPriority Priority,
    int EstimatedTokens,
    DroppedContextReason? DroppedReason,
    IReadOnlyList<ContextSourceRef> SourceRefs);

internal sealed record ContextSlicingReport
{
    public required string ThreadId { get; init; }

    public required string TurnId { get; init; }

    public string? ModelId { get; init; }

    public string? ProviderId { get; init; }

    public int? ProviderEffectiveContextLimitTokens { get; init; }

    public required int TianShuBudgetTokens { get; init; }

    public required int EstimatedTotalTokens { get; init; }

    public required int EstimatedIncludedTokens { get; init; }

    public IReadOnlyList<ContextSegmentReportEntry> IncludedSegments { get; init; } = [];

    public IReadOnlyList<ContextSegmentReportEntry> SummarizedSegments { get; init; } = [];

    public IReadOnlyList<ContextSegmentReportEntry> ReferenceOnlySegments { get; init; } = [];

    public IReadOnlyList<ContextSegmentReportEntry> DroppedSegments { get; init; } = [];

    public OverBudgetReasonCode? OverBudgetReasonCode { get; init; }
}

internal sealed record ContextSliceResult
{
    public IReadOnlyList<ContextSegment> IncludedSegments { get; init; } = [];

    public IReadOnlyList<ContextSegment> SummarizedSegments { get; init; } = [];

    public IReadOnlyList<ContextSegment> ReferenceOnlySegments { get; init; } = [];

    public IReadOnlyList<DroppedContextSegment> DroppedSegments { get; init; } = [];

    public IReadOnlyDictionary<string, object?> MaterializationHints { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public required ContextSlicingReport Report { get; init; }
}

internal sealed record ContextSlicedResponsesConversationInput(
    List<object> Input,
    ContextSlicingReport Report);
