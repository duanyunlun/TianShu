using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Memory;

/// <summary>
/// 记忆形成路径。
/// Memory formation path.
/// </summary>
public enum MemoryFormationPath
{
    Unknown = 0,
    DirectInstruction = 1,
    AnalogicalTransfer = 2,
    ExploratoryLearning = 3,
    ExternalImport = 4,
    RepeatedUsage = 5,
}

/// <summary>
/// 记忆证据类型。
/// Memory evidence kind.
/// </summary>
public enum MemoryEvidenceKind
{
    Unknown = 0,
    UserCorrection = 1,
    ToolResult = 2,
    TestResult = 3,
    CommandFailure = 4,
    CommandSuccess = 5,
    ConversationObservation = 6,
    ExternalFact = 7,
    Artifact = 8,
    ProviderSignal = 9,
}

/// <summary>
/// 记忆验证证据类型。
/// Memory validation evidence kind.
/// </summary>
public enum MemoryValidationEvidenceKind
{
    Unknown = 0,
    CommandSucceeded = 1,
    CommandFailed = 2,
    TestPassed = 3,
    TestFailed = 4,
    ToolResult = 5,
    UserConfirmed = 6,
    UserRejected = 7,
    ProviderSignal = 8,
    ArtifactVerified = 9,
}

/// <summary>
/// 记忆风险原因码。
/// Memory risk reason code.
/// </summary>
public enum MemoryRiskReasonCode
{
    None = 0,
    MissingSource = 1,
    ScopeEscalation = 2,
    ConflictsWithActiveFact = 3,
    SensitiveContent = 4,
    LongTermBehaviorChange = 5,
    ProviderCapabilityMissing = 6,
    LowConfidence = 7,
    WeakSource = 8,
    SingleUnverifiedFailure = 9,
    ReadOnlySpace = 10,
    SecretLikeContent = 11,
}

/// <summary>
/// 候选提升决策类型。
/// Memory promotion decision kind.
/// </summary>
public enum MemoryPromotionDecisionKind
{
    AutoEvidence = 0,
    AutoCandidate = 1,
    AutoPromote = 2,
    NeedsReview = 3,
    Reject = 4,
    SupersedeProposal = 5,
}

/// <summary>
/// 录入决策类型。
/// Memory ingestion decision kind.
/// </summary>
public enum MemoryIngestionDecisionKind
{
    AcceptEvidence = 0,
    CreateCandidate = 1,
    Promote = 2,
    NeedsReview = 3,
    Reject = 4,
    SupersedeProposal = 5,
}

/// <summary>
/// 记忆验证证据。
/// Memory validation evidence.
/// </summary>
public sealed record MemoryValidationEvidence
{
    public MemoryValidationEvidence(
        string evidenceId,
        MemoryValidationEvidenceKind kind,
        string summary,
        MemorySourceRef? source = null,
        DateTimeOffset capturedAt = default,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        EvidenceId = IdentifierGuard.AgainstNullOrWhiteSpace(evidenceId, nameof(evidenceId));
        Kind = kind;
        Summary = IdentifierGuard.AgainstNullOrWhiteSpace(summary, nameof(summary));
        Source = source;
        CapturedAt = capturedAt == default ? DateTimeOffset.UtcNow : capturedAt;
        Metadata = metadata ?? EmptyMetadata;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string EvidenceId { get; }

    public MemoryValidationEvidenceKind Kind { get; }

    public string Summary { get; }

    public MemorySourceRef? Source { get; }

    public DateTimeOffset CapturedAt { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// 记忆证据记录；证据可自动落地，但默认不进入 overlay。
/// Memory evidence record; evidence can be stored automatically but is not an overlay fact by default.
/// </summary>
public sealed record MemoryEvidenceRecord
{
    public MemoryEvidenceRecord(
        string evidenceId,
        MemorySpaceId memorySpaceId,
        MemorySourceRef source,
        MemoryEvidenceKind evidenceKind,
        string safeSummary,
        MemoryScopeKind? scopeKind = null,
        DateTimeOffset capturedAt = default,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<MemoryValidationEvidence>? validationEvidence = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        EvidenceId = IdentifierGuard.AgainstNullOrWhiteSpace(evidenceId, nameof(evidenceId));
        MemorySpaceId = memorySpaceId;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        EvidenceKind = evidenceKind;
        SafeSummary = IdentifierGuard.AgainstNullOrWhiteSpace(safeSummary, nameof(safeSummary));
        ScopeKind = scopeKind;
        CapturedAt = capturedAt == default ? DateTimeOffset.UtcNow : capturedAt;
        Tags = tags ?? Array.Empty<string>();
        ValidationEvidence = validationEvidence ?? Array.Empty<MemoryValidationEvidence>();
        Metadata = metadata ?? EmptyMetadata;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string EvidenceId { get; }

    public MemorySpaceId MemorySpaceId { get; }

    public MemorySourceRef Source { get; }

    public MemoryEvidenceKind EvidenceKind { get; }

    public string SafeSummary { get; }

    public MemoryScopeKind? ScopeKind { get; }

    public DateTimeOffset CapturedAt { get; }

    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<MemoryValidationEvidence> ValidationEvidence { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// 负例记忆语义。
/// Counterexample memory semantics.
/// </summary>
public sealed record MemoryCounterexample
{
    public MemoryCounterexample(
        string appliesWhen,
        string avoid,
        string because,
        string? preferredAlternative = null,
        IReadOnlyList<MemorySourceRef>? evidence = null,
        MemoryFormationPath formationPath = MemoryFormationPath.Unknown,
        IReadOnlyList<string>? tags = null)
    {
        AppliesWhen = IdentifierGuard.AgainstNullOrWhiteSpace(appliesWhen, nameof(appliesWhen));
        Avoid = IdentifierGuard.AgainstNullOrWhiteSpace(avoid, nameof(avoid));
        Because = IdentifierGuard.AgainstNullOrWhiteSpace(because, nameof(because));
        PreferredAlternative = preferredAlternative;
        Evidence = evidence ?? Array.Empty<MemorySourceRef>();
        FormationPath = formationPath;
        Tags = tags ?? Array.Empty<string>();
    }

    public string AppliesWhen { get; }

    public string Avoid { get; }

    public string Because { get; }

    public string? PreferredAlternative { get; }

    public IReadOnlyList<MemorySourceRef> Evidence { get; }

    public MemoryFormationPath FormationPath { get; }

    public IReadOnlyList<string> Tags { get; }
}

/// <summary>
/// 类比迁移链路。
/// Analogical transfer link.
/// </summary>
public sealed record MemoryTransferLink
{
    public MemoryTransferLink(
        IReadOnlyList<MemoryRecordId> sourceRecordIds,
        string similarityBasis,
        string transferHypothesis,
        IReadOnlyList<string>? validationEvidenceIds = null,
        string? applicability = null,
        MemoryRecordId? targetRecordId = null,
        DateTimeOffset createdAt = default)
    {
        SourceRecordIds = sourceRecordIds ?? throw new ArgumentNullException(nameof(sourceRecordIds));
        SimilarityBasis = IdentifierGuard.AgainstNullOrWhiteSpace(similarityBasis, nameof(similarityBasis));
        TransferHypothesis = IdentifierGuard.AgainstNullOrWhiteSpace(transferHypothesis, nameof(transferHypothesis));
        ValidationEvidenceIds = validationEvidenceIds ?? Array.Empty<string>();
        Applicability = applicability;
        TargetRecordId = targetRecordId;
        CreatedAt = createdAt == default ? DateTimeOffset.UtcNow : createdAt;
    }

    public IReadOnlyList<MemoryRecordId> SourceRecordIds { get; }

    public string SimilarityBasis { get; }

    public string TransferHypothesis { get; }

    public IReadOnlyList<string> ValidationEvidenceIds { get; }

    public string? Applicability { get; }

    public MemoryRecordId? TargetRecordId { get; }

    public DateTimeOffset CreatedAt { get; }
}

/// <summary>
/// 探索学习尝试摘要。
/// Exploratory learning attempt summary.
/// </summary>
public sealed record MemoryLearningAttemptSummary(
    string Summary,
    string Result,
    string? FailureReason = null)
{
    public string Summary { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Summary, nameof(Summary));

    public string Result { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Result, nameof(Result));
}

/// <summary>
/// 探索学习轨迹摘要。
/// Exploratory learning trace summary.
/// </summary>
public sealed record MemoryLearningTrace
{
    public MemoryLearningTrace(
        string problemSignature,
        IReadOnlyList<MemoryLearningAttemptSummary>? attemptSummaries = null,
        IReadOnlyList<string>? rejectedHypotheses = null,
        string? finalWorkingPath = null,
        IReadOnlyList<string>? validationEvidenceIds = null,
        IReadOnlyList<MemoryRecordId>? counterexampleIds = null,
        DateTimeOffset createdAt = default)
    {
        ProblemSignature = IdentifierGuard.AgainstNullOrWhiteSpace(problemSignature, nameof(problemSignature));
        AttemptSummaries = attemptSummaries ?? Array.Empty<MemoryLearningAttemptSummary>();
        RejectedHypotheses = rejectedHypotheses ?? Array.Empty<string>();
        FinalWorkingPath = finalWorkingPath;
        ValidationEvidenceIds = validationEvidenceIds ?? Array.Empty<string>();
        CounterexampleIds = counterexampleIds ?? Array.Empty<MemoryRecordId>();
        CreatedAt = createdAt == default ? DateTimeOffset.UtcNow : createdAt;
    }

    public string ProblemSignature { get; }

    public IReadOnlyList<MemoryLearningAttemptSummary> AttemptSummaries { get; }

    public IReadOnlyList<string> RejectedHypotheses { get; }

    public string? FinalWorkingPath { get; }

    public IReadOnlyList<string> ValidationEvidenceIds { get; }

    public IReadOnlyList<MemoryRecordId> CounterexampleIds { get; }

    public DateTimeOffset CreatedAt { get; }
}

/// <summary>
/// 结构化记忆上下文签名。
/// Structured memory context signature.
/// </summary>
public sealed record MemoryContextSignature
{
    public MemoryContextSignature(
        IReadOnlyList<MemorySpaceId>? memorySpaceIds = null,
        IReadOnlyList<MemoryScopeKind>? scopeKinds = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<MemorySourceRef>? sources = null,
        IReadOnlyList<MemoryLifecycleStatus>? lifecycleStatuses = null,
        IReadOnlyList<MemoryRecordId>? excludeRecordIds = null,
        decimal? minimumConfidence = null,
        DateTimeOffset? recordedAfter = null,
        DateTimeOffset? recordedBefore = null,
        DateTimeOffset? updatedAfter = null,
        DateTimeOffset? updatedBefore = null,
        DateTimeOffset? usedAfter = null,
        DateTimeOffset? usedBefore = null)
    {
        if (minimumConfidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence), "最小置信度必须位于 0 到 1 之间。");
        }

        MemorySpaceIds = memorySpaceIds ?? Array.Empty<MemorySpaceId>();
        ScopeKinds = scopeKinds ?? Array.Empty<MemoryScopeKind>();
        Tags = tags ?? Array.Empty<string>();
        Sources = sources ?? Array.Empty<MemorySourceRef>();
        LifecycleStatuses = lifecycleStatuses ?? Array.Empty<MemoryLifecycleStatus>();
        ExcludeRecordIds = excludeRecordIds ?? Array.Empty<MemoryRecordId>();
        MinimumConfidence = minimumConfidence;
        RecordedAfter = recordedAfter;
        RecordedBefore = recordedBefore;
        UpdatedAfter = updatedAfter;
        UpdatedBefore = updatedBefore;
        UsedAfter = usedAfter;
        UsedBefore = usedBefore;
    }

    public IReadOnlyList<MemorySpaceId> MemorySpaceIds { get; }

    public IReadOnlyList<MemoryScopeKind> ScopeKinds { get; }

    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<MemorySourceRef> Sources { get; }

    public IReadOnlyList<MemoryLifecycleStatus> LifecycleStatuses { get; }

    public IReadOnlyList<MemoryRecordId> ExcludeRecordIds { get; }

    public decimal? MinimumConfidence { get; }

    public DateTimeOffset? RecordedAfter { get; }

    public DateTimeOffset? RecordedBefore { get; }

    public DateTimeOffset? UpdatedAfter { get; }

    public DateTimeOffset? UpdatedBefore { get; }

    public DateTimeOffset? UsedAfter { get; }

    public DateTimeOffset? UsedBefore { get; }
}

/// <summary>
/// 记忆取代链路。
/// Memory supersede link.
/// </summary>
public sealed record MemorySupersedeLink
{
    public MemorySupersedeLink(
        MemoryRecordId oldRecordId,
        MemoryRecordId newRecordId,
        string reason,
        string actorId,
        MemorySourceRef? source = null,
        DateTimeOffset timestamp = default)
    {
        OldRecordId = oldRecordId;
        NewRecordId = newRecordId;
        Reason = IdentifierGuard.AgainstNullOrWhiteSpace(reason, nameof(reason));
        ActorId = IdentifierGuard.AgainstNullOrWhiteSpace(actorId, nameof(actorId));
        Source = source;
        Timestamp = timestamp == default ? DateTimeOffset.UtcNow : timestamp;
    }

    public MemoryRecordId OldRecordId { get; }

    public MemoryRecordId NewRecordId { get; }

    public string Reason { get; }

    public string ActorId { get; }

    public MemorySourceRef? Source { get; }

    public DateTimeOffset Timestamp { get; }
}

/// <summary>
/// 候选提升决策。
/// Memory promotion decision.
/// </summary>
public sealed record MemoryPromotionDecision
{
    public MemoryPromotionDecision(
        MemoryPromotionDecisionKind kind,
        IReadOnlyList<MemoryRiskReasonCode>? reasonCodes = null,
        MemoryLifecycleStatus? targetLifecycleStatus = null,
        string? explanation = null,
        MemorySupersedeLink? supersedeLink = null)
    {
        Kind = kind;
        ReasonCodes = reasonCodes ?? Array.Empty<MemoryRiskReasonCode>();
        TargetLifecycleStatus = targetLifecycleStatus;
        Explanation = explanation;
        SupersedeLink = supersedeLink;
    }

    public MemoryPromotionDecisionKind Kind { get; }

    public IReadOnlyList<MemoryRiskReasonCode> ReasonCodes { get; }

    public MemoryLifecycleStatus? TargetLifecycleStatus { get; }

    public string? Explanation { get; }

    public MemorySupersedeLink? SupersedeLink { get; }
}

/// <summary>
/// 录入治理决策。
/// Memory ingestion governance decision.
/// </summary>
public sealed record MemoryIngestionDecision
{
    public MemoryIngestionDecision(
        MemoryIngestionDecisionKind kind,
        IReadOnlyList<MemoryRiskReasonCode>? reasonCodes = null,
        MemoryLifecycleStatus? targetLifecycleStatus = null,
        string? explanation = null,
        MemoryPromotionDecision? promotionDecision = null)
    {
        Kind = kind;
        ReasonCodes = reasonCodes ?? Array.Empty<MemoryRiskReasonCode>();
        TargetLifecycleStatus = targetLifecycleStatus;
        Explanation = explanation;
        PromotionDecision = promotionDecision;
    }

    public MemoryIngestionDecisionKind Kind { get; }

    public IReadOnlyList<MemoryRiskReasonCode> ReasonCodes { get; }

    public MemoryLifecycleStatus? TargetLifecycleStatus { get; }

    public string? Explanation { get; }

    public MemoryPromotionDecision? PromotionDecision { get; }
}
