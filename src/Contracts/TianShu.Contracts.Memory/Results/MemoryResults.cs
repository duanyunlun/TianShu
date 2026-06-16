using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Memory;

/// <summary>
/// 记忆写入类操作结果。
/// Result of a memory mutation operation.
/// </summary>
public sealed record MemoryMutationResult(
    bool Success,
    MemoryRecordId? RecordId = null,
    MemoryLifecycleStatus? LifecycleStatus = null,
    string? DegradedReason = null,
    MemoryProviderCapability? UnsupportedCapability = null,
    MemoryMutationEffect Effect = MemoryMutationEffect.None,
    MemorySupersedeLink? SupersedeLink = null);

/// <summary>
/// 记忆查询结果。
/// Memory query result.
/// </summary>
public sealed record MemoryQueryResult
{
    /// <summary>
    /// 初始化记忆查询结果。
    /// Initializes a memory query result.
    /// </summary>
    public MemoryQueryResult(
        IReadOnlyList<FactMemoryRecord>? records = null,
        int totalCount = -1,
        IReadOnlyList<string>? degradedProviders = null,
        MemoryCitation? citation = null,
        IReadOnlyList<MemoryOverlayExplanation>? explanations = null,
        MemorySearchMode EffectiveSearchMode = MemorySearchMode.Structured)
    {
        Records = records ?? Array.Empty<FactMemoryRecord>();
        TotalCount = totalCount < 0 ? Records.Count : totalCount;
        DegradedProviders = degradedProviders ?? Array.Empty<string>();
        Citation = citation;
        Explanations = explanations ?? Array.Empty<MemoryOverlayExplanation>();
        this.EffectiveSearchMode = EffectiveSearchMode;
    }

    public IReadOnlyList<FactMemoryRecord> Records { get; }

    public int TotalCount { get; }

    public IReadOnlyList<string> DegradedProviders { get; }

    public MemoryCitation? Citation { get; }

    public IReadOnlyList<MemoryOverlayExplanation> Explanations { get; }

    public MemorySearchMode EffectiveSearchMode { get; }
}

/// <summary>
/// 记忆审核项中的审计摘要。
/// Audit summary included in a memory review item.
/// </summary>
public sealed record MemoryReviewAuditSummary(
    string Operation,
    MemoryMutationEffect Effect,
    string Actor,
    string Source,
    DateTimeOffset OccurredAt,
    string? Reason = null,
    IReadOnlyList<MemoryRiskReasonCode>? ReasonCodes = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public string Operation { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Operation, nameof(Operation));

    public string Actor { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Actor, nameof(Actor));

    public string Source { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Source, nameof(Source));

    public IReadOnlyList<MemoryRiskReasonCode> ReasonCodes { get; } = ReasonCodes ?? Array.Empty<MemoryRiskReasonCode>();

    public IReadOnlyDictionary<string, string> Metadata { get; } = Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// 面向 UI / CLI 的记忆审核项。
/// Memory review item for UI and CLI consumers.
/// </summary>
public sealed record MemoryReviewItem(
    FactMemoryRecord Record,
    MemoryCandidate? Candidate = null,
    IReadOnlyList<MemoryEvidenceRecord>? Evidence = null,
    IReadOnlyList<MemorySupersedeLink>? SupersedeLinks = null,
    IReadOnlyList<MemoryReviewAuditSummary>? Audit = null)
{
    public FactMemoryRecord Record { get; } = Record ?? throw new ArgumentNullException(nameof(Record));

    public IReadOnlyList<MemoryEvidenceRecord> Evidence { get; } = Evidence ?? Array.Empty<MemoryEvidenceRecord>();

    public IReadOnlyList<MemorySupersedeLink> SupersedeLinks { get; } = SupersedeLinks ?? Array.Empty<MemorySupersedeLink>();

    public IReadOnlyList<MemoryReviewAuditSummary> Audit { get; } = Audit ?? Array.Empty<MemoryReviewAuditSummary>();
}

/// <summary>
/// 记忆审核查询结果。
/// Memory review query result.
/// </summary>
public sealed record MemoryReviewQueryResult
{
    public MemoryReviewQueryResult(
        IReadOnlyList<MemoryReviewItem>? items = null,
        int totalCount = -1,
        IReadOnlyList<string>? degradedProviders = null)
    {
        Items = items ?? Array.Empty<MemoryReviewItem>();
        TotalCount = totalCount < 0 ? Items.Count : totalCount;
        DegradedProviders = degradedProviders ?? Array.Empty<string>();
    }

    public IReadOnlyList<MemoryReviewItem> Items { get; }

    public int TotalCount { get; }

    public IReadOnlyList<string> DegradedProviders { get; }
}

/// <summary>
/// 单轮记忆整理结果。
/// Result of a single memory consolidation pass.
/// </summary>
public sealed record MemoryConsolidationRunResult(
    int CandidatesScanned,
    int ProposalsCreated,
    bool LeaseAcquired = true,
    bool SkippedByLease = false,
    int CandidatesSkippedByCooldown = 0,
    int RetriesDeferred = 0,
    int FailuresRecorded = 0,
    string PermissionBoundary = "audit-only");
