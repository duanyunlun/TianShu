using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Projections;

/// <summary>
/// 协作空间投影，面向消费层展示协作域当前的摘要状态。
/// Collaboration-space projection exposing the current summary state of a collaboration scope to consumers.
/// </summary>
public sealed record CollaborationSpaceProjection(
    CollaborationSpaceRef CollaborationSpace,
    int ActiveSessionCount,
    int ActiveThreadCount,
    bool IsArchived);

/// <summary>
/// 线程运行态投影，承载消费层可见的生命周期状态，不暴露 Kernel / Runtime 内部对象。
/// Thread runtime-status projection carrying consumer-visible lifecycle state without exposing Kernel / Runtime internals.
/// </summary>
public sealed record ThreadRuntimeStatusProjection(
    string Lifecycle,
    string? TurnStatus = null,
    string? BackgroundStatus = null,
    bool HasActiveRun = false,
    string? ActiveRunRef = null,
    string? NotificationCode = null);

/// <summary>
/// 线程 token usage 分段投影。
/// Thread token-usage breakdown projection.
/// </summary>
public sealed record ThreadTokenUsageBreakdownProjection(
    int TotalTokens,
    int InputTokens,
    int CachedInputTokens,
    int OutputTokens,
    int ReasoningOutputTokens);

/// <summary>
/// 线程 token usage 投影，可表示真实 provider usage 或估算 usage。
/// Thread token-usage projection for real provider usage or estimated usage.
/// </summary>
public sealed record ThreadTokenUsageProjection(
    ThreadTokenUsageBreakdownProjection Last,
    ThreadTokenUsageBreakdownProjection Total,
    int? ModelContextWindow = null,
    bool Estimated = false,
    string? Source = null,
    string? MissingReason = null);

/// <summary>
/// 上下文裁切条目投影，记录 kept / dropped segment 的可审查摘要。
/// Context-slicing segment projection that records auditable kept / dropped segment summaries.
/// </summary>
public sealed record ThreadContextSegmentProjection(
    string SegmentId,
    string SourceKind,
    string Disposition,
    string? ProjectionMode = null,
    string? DroppedReason = null,
    int? EstimatedTokens = null,
    string? EvidenceRef = null,
    string? ArtifactRef = null);

/// <summary>
/// 上下文裁切诊断投影，供 Host projection 与审计消费。
/// Context-slicing diagnostics projection for Host projection and audit consumption.
/// </summary>
public sealed record ThreadContextSlicingDiagnosticsProjection(
    string? PolicyId,
    int? BudgetTokens,
    int? EstimatedInputTokens,
    int? EstimatedIncludedTokens,
    IReadOnlyList<ThreadContextSegmentProjection>? Segments = null,
    string? SourceLayer = null)
{
    public IReadOnlyList<ThreadContextSegmentProjection> Segments { get; } = Segments ?? Array.Empty<ThreadContextSegmentProjection>();
}

/// <summary>
/// 线程诊断投影，聚合 runtime trace、diagnostics、metrics 与 context slicing 摘要。
/// Thread diagnostics projection aggregating runtime trace, diagnostics, metrics, and context-slicing summaries.
/// </summary>
public sealed record ThreadDiagnosticsProjection(
    IReadOnlyList<string>? RuntimeTraceRefs = null,
    IReadOnlyList<string>? DiagnosticsRefs = null,
    IReadOnlyList<string>? MetricsEventIds = null,
    IReadOnlyList<string>? FailureCodes = null,
    IReadOnlyList<string>? MissingReasons = null,
    ThreadContextSlicingDiagnosticsProjection? ContextSlicing = null)
{
    public IReadOnlyList<string> RuntimeTraceRefs { get; } = RuntimeTraceRefs ?? Array.Empty<string>();

    public IReadOnlyList<string> DiagnosticsRefs { get; } = DiagnosticsRefs ?? Array.Empty<string>();

    public IReadOnlyList<string> MetricsEventIds { get; } = MetricsEventIds ?? Array.Empty<string>();

    public IReadOnlyList<string> FailureCodes { get; } = FailureCodes ?? Array.Empty<string>();

    public IReadOnlyList<string> MissingReasons { get; } = MissingReasons ?? Array.Empty<string>();
}

/// <summary>
/// 线程 evidence 投影，记录 turn log、rollout 与 audit refs 的可用性。
/// Thread evidence projection recording turn-log, rollout, and audit-ref availability.
/// </summary>
public sealed record ThreadEvidenceProjection(
    string? TurnLogRef = null,
    string? RolloutRef = null,
    IReadOnlyList<string>? AuditRefs = null,
    IReadOnlyList<string>? DowngradeReasons = null)
{
    public IReadOnlyList<string> AuditRefs { get; } = AuditRefs ?? Array.Empty<string>();

    public IReadOnlyList<string> DowngradeReasons { get; } = DowngradeReasons ?? Array.Empty<string>();
}

/// <summary>
/// 线程投影，面向消费层展示线程摘要信息与正式 runtime 附属投影。
/// Thread projection exposing thread summary information and formal runtime adjunct projections to consumers.
/// </summary>
public sealed record ThreadProjection(
    ThreadId ThreadId,
    string Title,
    CollaborationSpaceRef CollaborationSpace,
    ParticipantRef? InitiatedByParticipant = null,
    TurnId? ActiveTurnId = null,
    bool HasActiveTurn = false,
    ThreadRuntimeStatusProjection? RuntimeStatus = null,
    ThreadTokenUsageProjection? TokenUsage = null,
    ThreadDiagnosticsProjection? Diagnostics = null,
    ThreadEvidenceProjection? Evidence = null)
{
    public string Title { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Title, nameof(Title));
}

/// <summary>
/// 工作流看板投影。
/// Workflow-board projection.
/// </summary>
public sealed record WorkflowBoardProjection(
    WorkflowId WorkflowId,
    string DisplayName,
    CollaborationSpaceRef CollaborationSpace,
    int PendingTaskCount,
    int RunningTaskCount,
    int CompletedTaskCount)
{
    public string DisplayName { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(DisplayName, nameof(DisplayName));
}

/// <summary>
/// 任务看板条目。
/// Task-board entry.
/// </summary>
public sealed record TaskBoardItem(
    TaskId TaskId,
    string Title,
    string State,
    ParticipantRef? Owner = null)
{
    public string Title { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Title, nameof(Title));

    public string State { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(State, nameof(State));
}

/// <summary>
/// 任务看板投影。
/// Task-board projection.
/// </summary>
public sealed record TaskBoardProjection(WorkflowId WorkflowId, IReadOnlyList<TaskBoardItem> Items)
{
    public IReadOnlyList<TaskBoardItem> Items { get; } = Items ?? Array.Empty<TaskBoardItem>();
}

/// <summary>
/// 参与者投影。
/// Participant projection.
/// </summary>
public sealed record ParticipantProjection(
    ParticipantRef Participant,
    string ScopeKind,
    string ScopeKey,
    string Role,
    bool IsActive)
{
    public string ScopeKind { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(ScopeKind, nameof(ScopeKind));

    public string ScopeKey { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(ScopeKey, nameof(ScopeKey));

    public string Role { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Role, nameof(Role));
}

/// <summary>
/// 代理花名册条目。
/// Agent-roster entry.
/// </summary>
public sealed record AgentRosterEntry(
    AgentId AgentId,
    ParticipantRef Participant,
    string Role,
    int Depth,
    bool IsBusy)
{
    public string Role { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Role, nameof(Role));
}

/// <summary>
/// 代理花名册投影。
/// Agent-roster projection.
/// </summary>
public sealed record AgentRosterProjection(IReadOnlyList<AgentRosterEntry> Items)
{
    public IReadOnlyList<AgentRosterEntry> Items { get; } = Items ?? Array.Empty<AgentRosterEntry>();
}

/// <summary>
/// 审批队列条目。
/// Approval-queue entry.
/// </summary>
public sealed record ApprovalQueueItem(
    ApprovalId ApprovalId,
    string Title,
    string Reason,
    ParticipantRef RequestedFrom,
    DateTimeOffset RequestedAt,
    string? CheckpointKind = null,
    string? RiskSource = null,
    string? RequestContent = null,
    string? UserDecision = null,
    string? ExecutionResult = null,
    MetadataBag? Metadata = null)
{
    public string Title { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Title, nameof(Title));

    public string Reason { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Reason, nameof(Reason));

    public MetadataBag Metadata { get; } = Metadata ?? MetadataBag.Empty;
}

/// <summary>
/// 审批队列投影。
/// Approval-queue projection.
/// </summary>
public sealed record ApprovalQueueProjection(IReadOnlyList<ApprovalQueueItem> Items)
{
    public IReadOnlyList<ApprovalQueueItem> Items { get; } = Items ?? Array.Empty<ApprovalQueueItem>();
}

/// <summary>
/// 产物投影。
/// Artifact projection.
/// </summary>
public sealed record ArtifactProjection(
    ArtifactId ArtifactId,
    string Name,
    ArtifactKind Kind,
    ArtifactLifecycleState State,
    CollaborationSpaceRef CollaborationSpace,
    ParticipantRef? ProducedByParticipant = null)
{
    public string Name { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Name, nameof(Name));
}

/// <summary>
/// Artifact 集合投影条目。
/// Artifact collection projection item.
/// </summary>
public sealed record ArtifactCollectionItem(
    ArtifactId ArtifactId,
    string Name,
    ArtifactKind Kind,
    ArtifactLifecycleState State,
    ParticipantRef? ProducedByParticipant = null,
    IReadOnlyList<string>? PromotionChannels = null,
    IReadOnlyList<TaskId>? AttachedTaskIds = null,
    DateTimeOffset UpdatedAt = default)
{
    public string Name { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Name, nameof(Name));

    public IReadOnlyList<string> PromotionChannels { get; } = PromotionChannels ?? Array.Empty<string>();

    public IReadOnlyList<TaskId> AttachedTaskIds { get; } = AttachedTaskIds ?? Array.Empty<TaskId>();

    public DateTimeOffset UpdatedAt { get; } = UpdatedAt == default ? DateTimeOffset.UtcNow : UpdatedAt;
}

/// <summary>
/// Artifact 集合投影，表示某个协作空间下当前可见的 artifact 列表。
/// Artifact collection projection representing the current visible artifact set for a collaboration space.
/// </summary>
public sealed record ArtifactCollectionProjection(
    CollaborationSpaceRef CollaborationSpace,
    IReadOnlyList<ArtifactCollectionItem> Items)
{
    public IReadOnlyList<ArtifactCollectionItem> Items { get; } = Items ?? Array.Empty<ArtifactCollectionItem>();
}
