using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Memory;

/// <summary>
/// 添加一条结构化记忆事实。
/// Adds a structured memory fact.
/// </summary>
public sealed record AddMemory(
    MemorySpaceId MemorySpaceId,
    string Key,
    StructuredValue Value,
    decimal Confidence = 1m,
    MemorySourceRef? Source = null)
{
    public string Key { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Key, nameof(Key));

    public StructuredValue Value { get; } = Value ?? throw new ArgumentNullException(nameof(Value));
}

/// <summary>
/// 从来源中抽取候选记忆。
/// Extracts candidate memories from a source.
/// </summary>
public sealed record ExtractMemory(
    MemorySpaceId MemorySpaceId,
    MemorySourceRef Source,
    StructuredValue? Content = null)
{
    public MemorySourceRef Source { get; } = Source ?? throw new ArgumentNullException(nameof(Source));
}

/// <summary>
/// 从外部来源导入记忆事实；Phase A 默认实现只承诺可测降级边界。
/// Imports memory facts from an external source; the Phase A default only guarantees a testable degraded boundary.
/// </summary>
public sealed record ImportMemory(
    MemorySpaceId MemorySpaceId,
    MemorySourceRef Source,
    IReadOnlyList<FactMemoryRecord>? Records = null)
{
    public MemorySourceRef Source { get; } = Source ?? throw new ArgumentNullException(nameof(Source));

    public IReadOnlyList<FactMemoryRecord> Records { get; } = Records ?? Array.Empty<FactMemoryRecord>();
}

/// <summary>
/// 导出记忆事实给外部目标；Phase A 默认实现只承诺可测降级边界。
/// Exports memory facts to an external destination; the Phase A default only guarantees a testable degraded boundary.
/// </summary>
public sealed record ExportMemory(
    MemorySpaceId MemorySpaceId,
    MemorySourceRef? Destination = null,
    FilterMemory? Filter = null);

/// <summary>
/// 将已注册的记忆 provider 绑定到指定记忆空间。
/// Binds an already registered memory provider to a memory space.
/// </summary>
public sealed record BindMemoryProvider(
    string ProviderId,
    MemorySpaceId MemorySpaceId,
    MemoryProviderBindingMode Mode,
    MemoryProviderCapability AllowedCapabilities)
{
    public string ProviderId { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(ProviderId, nameof(ProviderId));
}

/// <summary>
/// 触发一轮记忆整理；该命令只允许生成审计记录和待审 proposal，不允许静默改写 Active fact。
/// Runs one memory consolidation pass; the command may only emit audit records and review proposals.
/// </summary>
public sealed record RunMemoryConsolidation(
    MemorySpaceId? MemorySpaceId = null,
    bool EnableLease = true,
    bool IncludeArchiveProposals = false,
    bool IncludeForgetProposals = false,
    bool IncludeOverlayCacheRebuildProposals = false,
    bool EmitOverlayCacheSnapshot = true,
    bool RecordFailureDiagnostics = true,
    int? LeaseDurationSeconds = null,
    int? CooldownWindowSeconds = null,
    int? MaxRetryAttempts = null,
    int? RetryDelaySeconds = null,
    int? ArchiveUnusedFactsOlderThanSeconds = null,
    long? ArchiveUnusedFactsWithUsageCountAtMost = null);

/// <summary>
/// 遗忘一条记忆，使其退出默认读取路径。
/// Forgets a memory so it leaves default read paths.
/// </summary>
public sealed record ForgetMemory(
    MemoryRecordId? MemoryRecordId = null,
    MemorySpaceId? MemorySpaceId = null,
    string? Key = null);

/// <summary>
/// 删除一条记忆，用于显式物理删除或合规删除。
/// Deletes a memory for explicit physical or compliance deletion.
/// </summary>
public sealed record DeleteMemory(
    MemoryRecordId? MemoryRecordId = null,
    MemorySpaceId? MemorySpaceId = null,
    string? Key = null,
    string? Reason = null);

/// <summary>
/// 用一条新事实取代旧记忆；调用方不得用普通 upsert 表达正式纠错。
/// Supersedes an old memory with a new fact; callers must not model formal correction as a plain upsert.
/// </summary>
public sealed record SupersedeMemory(
    MemoryRecordId OldRecordId,
    MemorySpaceId MemorySpaceId,
    string NewKey,
    StructuredValue NewValue,
    string Reason,
    decimal Confidence = 1m,
    MemorySourceRef? Source = null)
{
    public string NewKey { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(NewKey, nameof(NewKey));

    public StructuredValue NewValue { get; } = NewValue ?? throw new ArgumentNullException(nameof(NewValue));

    public string Reason { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Reason, nameof(Reason));

    public decimal Confidence { get; } = Confidence is < 0m or > 1m
        ? throw new ArgumentOutOfRangeException(nameof(Confidence), "置信度必须位于 0 到 1 之间。")
        : Confidence;
}

/// <summary>
/// 批准一条待审记忆，使 PendingReview fact 或候选记忆受控提升为 Active fact。
/// Approves a pending-review fact or candidate memory and promotes it to an Active fact.
/// </summary>
public sealed record ApproveMemoryReview(
    MemoryRecordId? MemoryRecordId = null,
    MemorySpaceId? MemorySpaceId = null,
    string? Key = null,
    string? Reason = null,
    MemorySourceRef? Source = null);

/// <summary>
/// 降权一条待审记忆，使其退出默认待审队列但保留审计。
/// Demotes a pending-review memory so it leaves the default review queue while preserving audit.
/// </summary>
public sealed record DemoteMemoryReview(
    MemoryRecordId? MemoryRecordId = null,
    MemorySpaceId? MemorySpaceId = null,
    string? Key = null,
    string? Reason = null,
    MemorySourceRef? Source = null);

/// <summary>
/// 将一条待审记忆合并到既有事实，并保留取代链与审核审计。
/// Merges a pending-review memory into an existing fact while preserving supersede links and review audit.
/// </summary>
public sealed record MergeMemoryReview(
    MemoryRecordId ReviewRecordId,
    MemoryRecordId TargetRecordId,
    MemorySpaceId MemorySpaceId,
    string Reason,
    string? MergedKey = null,
    StructuredValue? MergedValue = null,
    decimal? Confidence = null,
    MemorySourceRef? Source = null)
{
    public string Reason { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Reason, nameof(Reason));
}

/// <summary>
/// 恢复一条已降权、拒绝、归档或遗忘的记忆到待审状态。
/// Restores a demoted, rejected, archived, or forgotten memory to pending review.
/// </summary>
public sealed record RestoreMemoryReview(
    MemoryRecordId? MemoryRecordId = null,
    MemorySpaceId? MemorySpaceId = null,
    string? Key = null,
    string? Reason = null,
    MemorySourceRef? Source = null);

/// <summary>
/// 记录针对记忆的反馈或修正建议。
/// Records feedback or a correction suggestion for a memory.
/// </summary>
public sealed record RecordMemoryFeedback(
    MemoryRecordId MemoryRecordId,
    MemoryMergeDecision Decision,
    string Feedback,
    MemorySourceRef? Source = null)
{
    public string Feedback { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Feedback, nameof(Feedback));
}

/// <summary>
/// 记录一次回答或投影实际使用的记忆引用。
/// Records memory citations actually used by a response or projection.
/// </summary>
public sealed record RecordMemoryCitation(MemoryCitation Citation)
{
    public MemoryCitation Citation { get; } = Citation ?? throw new ArgumentNullException(nameof(Citation));
}
