using System.Text.Json.Serialization;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Memory;

/// <summary>
/// 记忆作用域种类。
/// Memory scope kind.
/// </summary>
public enum MemoryScopeKind
{
    User = 0,
    Workspace = 1,
    Team = 2,
    Session = 3,
    Agent = 4,
    Collaboration = 5,
}

/// <summary>
/// 记忆合并决策。
/// Memory merge decision.
/// </summary>
public enum MemoryMergeDecision
{
    Applied = 0,
    Ignored = 1,
    NeedsReview = 2,
}

/// <summary>
/// 记忆生命周期状态。
/// Memory lifecycle status.
/// </summary>
public enum MemoryLifecycleStatus
{
    Active = 0,
    PendingReview = 1,
    Archived = 2,
    Forgotten = 3,
    Deleted = 4,
}

/// <summary>
/// 记忆变更实际产生的效果。
/// Actual effect produced by a memory mutation.
/// </summary>
public enum MemoryMutationEffect
{
    /// <summary>
    /// 未声明具体效果。
    /// No specific effect was declared.
    /// </summary>
    None = 0,

    /// <summary>
    /// 新增或覆盖了结构化事实。
    /// A structured fact was added or upserted.
    /// </summary>
    Upserted = 1,

    /// <summary>
    /// 只改变了事实生命周期。
    /// Only the fact lifecycle was changed.
    /// </summary>
    LifecycleChanged = 2,

    /// <summary>
    /// 删除请求被实现为保留 tombstone 的软删除。
    /// The delete request was implemented as a tombstone-preserving soft delete.
    /// </summary>
    SoftDeleted = 3,

    /// <summary>
    /// 删除请求执行了物理删除。
    /// The delete request performed a physical deletion.
    /// </summary>
    PhysicallyDeleted = 4,

    /// <summary>
    /// 操作被降级或能力不支持。
    /// The operation was degraded or unsupported.
    /// </summary>
    Degraded = 5,

    /// <summary>
    /// 新记录取代了旧记录。
    /// A new record superseded an old record.
    /// </summary>
    Superseded = 6,
}

/// <summary>
/// 线程或会话级记忆模式。
/// Thread/session-level memory mode.
/// </summary>
public enum ThreadMemoryMode
{
    /// <summary>
    /// 默认读写模式：读取 overlay，并允许策略批准后的持久化写入。
    /// Default read-write mode: reads overlays and allows policy-approved persistent writes.
    /// </summary>
    ReadWrite = 0,

    /// <summary>
    /// 只读模式：可读取既有记忆，但不持久化新的记忆变更。
    /// Read-only mode: reads existing memory but does not persist new memory mutations.
    /// </summary>
    ReadOnly = 1,

    /// <summary>
    /// 临时模式：允许会话内临时记忆，但不写入长期存储。
    /// Ephemeral mode: allows session-local memory without writing to long-term storage.
    /// </summary>
    Ephemeral = 2,

    /// <summary>
    /// 禁用模式：不读取记忆 overlay，也不写入记忆。
    /// Disabled mode: does not read memory overlays and does not write memory.
    /// </summary>
    Disabled = 3,
}

/// <summary>
/// 记忆检索模式。
/// Memory search mode.
/// </summary>
public enum MemorySearchMode
{
    /// <summary>
    /// 只使用结构化字段过滤。
    /// Uses structured field filtering only.
    /// </summary>
    Structured = 0,

    /// <summary>
    /// 使用本地关键词检索。
    /// Uses local keyword search.
    /// </summary>
    Keyword = 1,

    /// <summary>
    /// 请求语义检索；缺少语义 provider 时必须显式降级。
    /// Requests semantic search; must degrade explicitly when no semantic provider is available.
    /// </summary>
    Semantic = 2,
}

/// <summary>
/// 记忆来源类型。
/// Memory source kind.
/// </summary>
public enum MemorySourceKind
{
    Unknown = 0,
    Conversation = 1,
    Artifact = 2,
    File = 3,
    Url = 4,
    ToolResult = 5,
    System = 6,
    ExternalProvider = 7,
}

/// <summary>
/// 记忆 provider 能力。
/// Memory-provider capability.
/// </summary>
[Flags]
public enum MemoryProviderCapability
{
    None = 0,
    ListSpaces = 1 << 0,
    Add = 1 << 1,
    Extract = 1 << 2,
    Filter = 1 << 3,
    Forget = 1 << 4,
    Delete = 1 << 5,
    Feedback = 1 << 6,
    Citation = 1 << 7,
    Import = 1 << 8,
    Export = 1 << 9,
    Supersede = 1 << 10,
    Review = 1 << 11,
    KeywordSearch = 1 << 12,
    SemanticSearch = 1 << 13,
    EmbeddingIndexing = 1 << 14,
    LlmExtraction = 1 << 15,
    ReadOnlyAccess = 1 << 16,
    ReadWriteAccess = 1 << 17,
}

/// <summary>
/// 记忆 provider 接入模式。
/// Memory-provider binding mode.
/// </summary>
public enum MemoryProviderBindingMode
{
    ReadOnly = 0,
    ReadWrite = 1,
    Mirror = 2,
    ImportExport = 3,
}

/// <summary>
/// 记忆 provider 信任等级。
/// Memory-provider trust level.
/// </summary>
public enum MemoryProviderTrustLevel
{
    Unknown = 0,
    BuiltIn = 1,
    Workspace = 2,
    Organization = 3,
    External = 4,
}

/// <summary>
/// 记忆 provider 降级策略。
/// Memory-provider degradation strategy.
/// </summary>
public enum MemoryProviderDegradationStrategy
{
    Unknown = 0,
    UnsupportedResult = 1,
    DegradedRead = 2,
    FailClosed = 3,
}

/// <summary>
/// 记忆 provider 特性标记。
/// Memory-provider feature flags.
/// </summary>
[Flags]
public enum MemoryProviderFeature
{
    None = 0,
    SourceTracking = 1 << 0,
    CitationWriteBack = 1 << 1,
    UsageTelemetry = 1 << 2,
    SecretRedaction = 1 << 3,
    BackgroundTasks = 1 << 4,
    ExternalIndex = 1 << 5,
    ModelInvocation = 1 << 6,
}

/// <summary>
/// 记忆来源引用。
/// Memory source reference.
/// </summary>
public sealed record MemorySourceRef
{
    /// <summary>
    /// 初始化记忆来源引用。
    /// Initializes a memory source reference.
    /// </summary>
    [JsonConstructor]
    public MemorySourceRef(
        MemorySourceKind sourceKind,
        string sourceId,
        string? role = null,
        string? path = null,
        string? url = null,
        string? snippet = null,
        DateTimeOffset capturedAt = default,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        SourceKind = sourceKind;
        SourceId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceId, nameof(sourceId));
        Role = role;
        Path = path;
        Url = url;
        Snippet = snippet;
        CapturedAt = capturedAt == default ? DateTimeOffset.UtcNow : capturedAt;
        Metadata = metadata ?? EmptyMetadata;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public MemorySourceKind SourceKind { get; }

    public string SourceId { get; }

    public string? Role { get; }

    public string? Path { get; }

    public string? Url { get; }

    public string? Snippet { get; }

    public DateTimeOffset CapturedAt { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// 单条记忆引用证据。
/// Single memory citation evidence entry.
/// </summary>
public sealed record MemoryCitationEntry(
    MemoryRecordId MemoryRecordId,
    MemorySpaceId MemorySpaceId,
    string Key,
    MemorySourceRef? Source = null,
    string? Note = null)
{
    public string Key { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Key, nameof(Key));
}

/// <summary>
/// 一次回答或投影实际使用的记忆引用集合。
/// Set of memory citations used by a response or projection.
/// </summary>
public sealed record MemoryCitation(IReadOnlyList<MemoryCitationEntry>? Entries = null)
{
    public IReadOnlyList<MemoryCitationEntry> Entries { get; } = Entries ?? Array.Empty<MemoryCitationEntry>();
}

/// <summary>
/// 记忆进入 overlay 的解释。
/// Explanation for why a memory fact entered an overlay.
/// </summary>
public sealed record MemoryOverlayExplanation(
    MemoryRecordId MemoryRecordId,
    MemorySpaceId MemorySpaceId,
    string Key,
    int Rank,
    decimal Score,
    IReadOnlyList<string>? Factors = null,
    string? RetrievalMode = null)
{
    public string Key { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Key, nameof(Key));

    public IReadOnlyList<string> Factors { get; } = Factors ?? Array.Empty<string>();
}

/// <summary>
/// 记忆 provider 描述符。
/// Memory-provider descriptor.
/// </summary>
public sealed record MemoryProviderDescriptor(
    string ProviderId,
    string DisplayName,
    string Version,
    MemoryProviderCapability Capabilities,
    IReadOnlyList<MemoryScopeKind>? SupportedScopes = null,
    bool RequiresNetwork = false,
    bool RequiresCredentials = false,
    MemoryProviderTrustLevel TrustLevel = MemoryProviderTrustLevel.Unknown,
    IReadOnlyList<MemoryLifecycleStatus>? SupportedLifecycleStatuses = null,
    MemoryProviderDegradationStrategy DegradationStrategy = MemoryProviderDegradationStrategy.Unknown,
    MemoryProviderFeature Features = MemoryProviderFeature.None)
{
    public string ProviderId { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(ProviderId, nameof(ProviderId));

    public string DisplayName { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(DisplayName, nameof(DisplayName));

    public string Version { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Version, nameof(Version));

    public IReadOnlyList<MemoryScopeKind> SupportedScopes { get; } = SupportedScopes ?? Array.Empty<MemoryScopeKind>();

    public IReadOnlyList<MemoryLifecycleStatus> SupportedLifecycleStatuses { get; } = SupportedLifecycleStatuses ?? Array.Empty<MemoryLifecycleStatus>();
}

/// <summary>
/// 记忆 provider 与空间之间的绑定。
/// Binding between a memory provider and a memory space.
/// </summary>
public sealed record MemoryProviderBinding(
    string ProviderId,
    MemorySpaceId MemorySpaceId,
    MemoryProviderBindingMode Mode,
    MemoryProviderCapability AllowedCapabilities)
{
    public string ProviderId { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(ProviderId, nameof(ProviderId));
}

/// <summary>
/// 记忆操作上下文。
/// Memory operation context.
/// </summary>
public sealed record MemoryOperationContext
{
    /// <summary>
    /// 初始化记忆操作上下文。
    /// Initializes a memory operation context.
    /// </summary>
    public MemoryOperationContext(
        string actorId,
        SessionId? sessionId = null,
        string? correlationId = null,
        DateTimeOffset? timestamp = null,
        IReadOnlyDictionary<string, string>? policyOverrides = null)
    {
        ActorId = IdentifierGuard.AgainstNullOrWhiteSpace(actorId, nameof(actorId));
        SessionId = sessionId;
        CorrelationId = correlationId;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
        PolicyOverrides = policyOverrides ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public string ActorId { get; }

    public SessionId? SessionId { get; }

    public string? CorrelationId { get; }

    public DateTimeOffset Timestamp { get; }

    public IReadOnlyDictionary<string, string> PolicyOverrides { get; }
}

/// <summary>
/// 记忆抽取候选。
/// Memory extraction candidate.
/// </summary>
public sealed record MemoryCandidate
{
    /// <summary>
    /// 初始化记忆抽取候选。
    /// Initializes a memory extraction candidate.
    /// </summary>
    public MemoryCandidate(
        string key,
        StructuredValue value,
        MemorySpaceId memorySpaceId,
        decimal confidence = 1m,
        MemorySourceRef? source = null,
        string? extractionReason = null,
        string? ruleId = null,
        MemoryFormationPath formationPath = MemoryFormationPath.Unknown,
        IReadOnlyList<MemoryValidationEvidence>? validationEvidence = null,
        MemoryContextSignature? contextSignature = null,
        bool isCounterexample = false)
    {
        if (confidence < 0m || confidence > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "候选置信度必须位于 0 到 1 之间。");
        }

        Key = IdentifierGuard.AgainstNullOrWhiteSpace(key, nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        MemorySpaceId = memorySpaceId;
        Confidence = confidence;
        Source = source;
        ExtractionReason = extractionReason;
        RuleId = ruleId;
        FormationPath = formationPath;
        ValidationEvidence = validationEvidence ?? Array.Empty<MemoryValidationEvidence>();
        ContextSignature = contextSignature;
        IsCounterexample = isCounterexample;
    }

    public string Key { get; }

    public StructuredValue Value { get; }

    public MemorySpaceId MemorySpaceId { get; }

    public decimal Confidence { get; }

    public MemorySourceRef? Source { get; }

    public string? ExtractionReason { get; }

    public string? RuleId { get; }

    public MemoryFormationPath FormationPath { get; }

    public IReadOnlyList<MemoryValidationEvidence> ValidationEvidence { get; }

    public MemoryContextSignature? ContextSignature { get; }

    public bool IsCounterexample { get; }

    public MemoryLifecycleStatus LifecycleStatus => MemoryLifecycleStatus.PendingReview;
}

/// <summary>
/// 记忆抽取作业状态。
/// Memory extraction job status.
/// </summary>
public enum MemoryExtractionJobStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>
/// 记忆抽取作业，用于追踪一次从来源到候选记忆的确定性抽取。
/// Memory extraction job used to track one deterministic source-to-candidate extraction.
/// </summary>
public sealed record MemoryExtractionJob
{
    /// <summary>
    /// 初始化记忆抽取作业。
    /// Initializes a memory extraction job.
    /// </summary>
    public MemoryExtractionJob(
        JobId jobId,
        MemorySpaceId memorySpaceId,
        MemorySourceRef source,
        MemoryExtractionJobStatus status = MemoryExtractionJobStatus.Pending,
        string? extractorId = null,
        int candidateCount = 0,
        DateTimeOffset createdAt = default,
        DateTimeOffset? completedAt = null)
    {
        if (candidateCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateCount), "候选数量不能为负。");
        }

        JobId = jobId;
        MemorySpaceId = memorySpaceId;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Status = status;
        ExtractorId = extractorId;
        CandidateCount = candidateCount;
        CreatedAt = createdAt == default ? DateTimeOffset.UtcNow : createdAt;
        CompletedAt = completedAt;
    }

    public JobId JobId { get; }

    public MemorySpaceId MemorySpaceId { get; }

    public MemorySourceRef Source { get; }

    public MemoryExtractionJobStatus Status { get; }

    public string? ExtractorId { get; }

    public int CandidateCount { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public static MemoryExtractionJob Start(
        MemorySpaceId memorySpaceId,
        MemorySourceRef source,
        string extractorId,
        DateTimeOffset createdAt)
        => new(
            new JobId($"memory-extraction:{Guid.NewGuid():N}"),
            memorySpaceId,
            source,
            extractorId: IdentifierGuard.AgainstNullOrWhiteSpace(extractorId, nameof(extractorId)),
            createdAt: createdAt);

    public MemoryExtractionJob Complete(
        int candidateCount,
        DateTimeOffset completedAt)
        => new(
            JobId,
            MemorySpaceId,
            Source,
            MemoryExtractionJobStatus.Succeeded,
            ExtractorId,
            candidateCount,
            CreatedAt,
            completedAt);
}

/// <summary>
/// 记忆空间模型。
/// Memory-space model.
/// </summary>
public sealed record MemorySpace
{
    /// <summary>
    /// 初始化记忆空间模型。
    /// Initializes a memory-space model.
    /// </summary>
    public MemorySpace(
        MemorySpaceId id,
        MemoryScopeKind scopeKind,
        string scopeKey,
        string displayName,
        bool isReadOnly = false)
    {
        Id = id;
        ScopeKind = scopeKind;
        ScopeKey = IdentifierGuard.AgainstNullOrWhiteSpace(scopeKey, nameof(scopeKey));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        IsReadOnly = isReadOnly;
    }

    public MemorySpaceId Id { get; }

    public MemoryScopeKind ScopeKind { get; }

    public string ScopeKey { get; }

    public string DisplayName { get; }

    public bool IsReadOnly { get; }
}

/// <summary>
/// 事实记忆记录。
/// Fact-memory record.
/// </summary>
public sealed record FactMemoryRecord
{
    [JsonConstructor]
    public FactMemoryRecord(
        MemoryRecordId id,
        string key,
        StructuredValue value,
        MemorySpaceId memorySpaceId,
        decimal confidence,
        DateTimeOffset recordedAt,
        MemoryLifecycleStatus lifecycleStatus,
        IReadOnlyList<MemorySourceRef>? sources,
        IReadOnlyList<string>? tags,
        long usageCount,
        DateTimeOffset? lastUsedAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        MemoryFormationPath formationPath,
        MemoryContextSignature? contextSignature,
        IReadOnlyList<MemoryValidationEvidence>? validationEvidence,
        bool isCounterexample)
    {
        if (confidence < 0m || confidence > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "置信度必须位于 0 到 1 之间。");
        }

        if (usageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(usageCount), "使用次数不能为负数。");
        }

        Id = id;
        Key = IdentifierGuard.AgainstNullOrWhiteSpace(key, nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        MemorySpaceId = memorySpaceId;
        Confidence = confidence;
        RecordedAt = recordedAt;
        LifecycleStatus = lifecycleStatus;
        Sources = sources ?? Array.Empty<MemorySourceRef>();
        Tags = tags ?? Array.Empty<string>();
        UsageCount = usageCount;
        LastUsedAt = lastUsedAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        FormationPath = formationPath;
        ContextSignature = contextSignature;
        ValidationEvidence = validationEvidence ?? Array.Empty<MemoryValidationEvidence>();
        IsCounterexample = isCounterexample;
    }

    /// <summary>
    /// 初始化事实记忆记录。
    /// Initializes a fact-memory record.
    /// </summary>
    public FactMemoryRecord(
        string key,
        StructuredValue value,
        MemorySpaceId memorySpaceId,
        decimal confidence = 1m,
        DateTimeOffset? recordedAt = null,
        MemoryRecordId? id = null,
        MemoryLifecycleStatus lifecycleStatus = MemoryLifecycleStatus.Active,
        IReadOnlyList<MemorySourceRef>? sources = null,
        IReadOnlyList<string>? tags = null,
        long usageCount = 0,
        DateTimeOffset? lastUsedAt = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        MemoryFormationPath formationPath = MemoryFormationPath.Unknown,
        MemoryContextSignature? contextSignature = null,
        IReadOnlyList<MemoryValidationEvidence>? validationEvidence = null,
        bool isCounterexample = false)
    {
        if (confidence < 0m || confidence > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "置信度必须位于 0 到 1 之间。");
        }

        if (usageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(usageCount), "使用次数不能为负数。");
        }

        var normalizedKey = IdentifierGuard.AgainstNullOrWhiteSpace(key, nameof(key));
        var normalizedRecordedAt = recordedAt ?? DateTimeOffset.UtcNow;

        Id = id ?? CreateDefaultId(memorySpaceId, normalizedKey);
        Key = normalizedKey;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        MemorySpaceId = memorySpaceId;
        Confidence = confidence;
        RecordedAt = normalizedRecordedAt;
        LifecycleStatus = lifecycleStatus;
        Sources = sources ?? Array.Empty<MemorySourceRef>();
        Tags = tags ?? Array.Empty<string>();
        UsageCount = usageCount;
        LastUsedAt = lastUsedAt;
        CreatedAt = createdAt ?? normalizedRecordedAt;
        UpdatedAt = updatedAt ?? normalizedRecordedAt;
        FormationPath = formationPath;
        ContextSignature = contextSignature;
        ValidationEvidence = validationEvidence ?? Array.Empty<MemoryValidationEvidence>();
        IsCounterexample = isCounterexample;
    }

    public MemoryRecordId Id { get; }

    public string Key { get; }

    public StructuredValue Value { get; }

    public MemorySpaceId MemorySpaceId { get; }

    public decimal Confidence { get; }

    public DateTimeOffset RecordedAt { get; }

    public MemoryLifecycleStatus LifecycleStatus { get; }

    public IReadOnlyList<MemorySourceRef> Sources { get; }

    public IReadOnlyList<string> Tags { get; }

    public long UsageCount { get; }

    public DateTimeOffset? LastUsedAt { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public MemoryFormationPath FormationPath { get; }

    public MemoryContextSignature? ContextSignature { get; }

    public IReadOnlyList<MemoryValidationEvidence> ValidationEvidence { get; }

    public bool IsCounterexample { get; }

    /// <summary>
    /// 返回生命周期已更新的新事实记录。
    /// Returns a new fact record with an updated lifecycle.
    /// </summary>
    public FactMemoryRecord WithLifecycle(
        MemoryLifecycleStatus lifecycleStatus,
        DateTimeOffset updatedAt)
        => new(
            Key,
            Value,
            MemorySpaceId,
            Confidence,
            RecordedAt,
            Id,
            lifecycleStatus,
            Sources,
            Tags,
            UsageCount,
            LastUsedAt,
            CreatedAt,
            updatedAt,
            FormationPath,
            ContextSignature,
            ValidationEvidence,
            IsCounterexample);

    /// <summary>
    /// 返回使用统计已更新的新事实记录。
    /// Returns a new fact record with updated usage metadata.
    /// </summary>
    public FactMemoryRecord WithUsage(
        long usageCount,
        DateTimeOffset lastUsedAt,
        DateTimeOffset updatedAt)
        => new(
            Key,
            Value,
            MemorySpaceId,
            Confidence,
            RecordedAt,
            Id,
            LifecycleStatus,
            Sources,
            Tags,
            usageCount,
            lastUsedAt,
            CreatedAt,
            updatedAt,
            FormationPath,
            ContextSignature,
            ValidationEvidence,
            IsCounterexample);

    private static MemoryRecordId CreateDefaultId(MemorySpaceId memorySpaceId, string key)
        => new($"memory-record:{memorySpaceId.Value}:{key}");
}

/// <summary>
/// 习惯画像。
/// Habit profile.
/// </summary>
public sealed record HabitProfile(
    AccountId? AccountId = null,
    IReadOnlyList<string>? PreferredTools = null,
    string? PreferredVerbosity = null,
    LabelSet? Labels = null)
{
    public IReadOnlyList<string> PreferredTools { get; } = PreferredTools ?? Array.Empty<string>();

    public LabelSet Labels { get; } = Labels ?? LabelSet.Empty;
}

/// <summary>
/// 记忆覆盖层。
/// Memory overlay.
/// </summary>
public sealed record MemoryOverlay(
    IReadOnlyList<FactMemoryRecord>? Facts = null,
    HabitProfile? HabitProfile = null,
    MemoryMergeDecision MergeDecision = MemoryMergeDecision.Applied,
    MemoryCitation? Citation = null,
    IReadOnlyList<MemoryOverlayExplanation>? Explanations = null)
{
    public IReadOnlyList<FactMemoryRecord> Facts { get; } = Facts ?? Array.Empty<FactMemoryRecord>();

    public HabitProfile? HabitProfile { get; } = HabitProfile;

    public IReadOnlyList<MemoryOverlayExplanation> Explanations { get; } = Explanations ?? Array.Empty<MemoryOverlayExplanation>();
}

/// <summary>
/// 协作域记忆覆盖层。
/// Collaboration memory overlay.
/// </summary>
public sealed record CollaborationMemoryOverlay(CollaborationSpaceId CollaborationSpaceId, MemoryOverlay Overlay);

/// <summary>
/// 环境作用域习惯画像。
/// Environment-scoped habit profile.
/// </summary>
public sealed record EnvironmentScopedHabitProfile(string EnvironmentKey, HabitProfile HabitProfile)
{
    public string EnvironmentKey { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(EnvironmentKey, nameof(EnvironmentKey));

    public HabitProfile HabitProfile { get; } = HabitProfile ?? throw new ArgumentNullException(nameof(HabitProfile));
}
