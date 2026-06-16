using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel;

/// <summary>
/// Kernel trace event 种类。
/// Kernel trace-event kind.
/// </summary>
public enum KernelTraceEventKind
{
    Unspecified = 0,
    IntentAccepted = 1,
    ProposalCreated = 2,
    ProposalReviewed = 3,
    GraphValidated = 4,
    OperationReviewed = 5,
    ExecutionPlanCreated = 6,
    CheckpointCreated = 7,
    EvaluationRecorded = 8,
    Rejected = 9,
}

/// <summary>
/// Kernel trace event。
/// Kernel trace event.
/// </summary>
public sealed record KernelTraceEvent
{
    public KernelTraceEvent(
        KernelTraceEventKind kind,
        string message,
        DateTimeOffset occurredAt = default,
        CoreIntentId? sourceIntentId = null,
        StageGraphId? sourceGraphId = null,
        StageId? sourceStageId = null,
        KernelOperationId? sourceOperationId = null,
        StructuredValue? data = null)
    {
        Kind = kind;
        Message = KernelContractGuard.RequiredText(message, nameof(message));
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
        SourceIntentId = sourceIntentId;
        SourceGraphId = sourceGraphId;
        SourceStageId = sourceStageId;
        SourceOperationId = sourceOperationId;
        Data = data ?? StructuredValue.Null;
    }

    public KernelTraceEventKind Kind { get; }

    public string Message { get; }

    public DateTimeOffset OccurredAt { get; }

    public CoreIntentId? SourceIntentId { get; }

    public StageGraphId? SourceGraphId { get; }

    public StageId? SourceStageId { get; }

    public KernelOperationId? SourceOperationId { get; }

    public StructuredValue Data { get; }
}

/// <summary>
/// 单次 Kernel run 的 trace。
/// Trace for a single Kernel run.
/// </summary>
public sealed record KernelRunTrace
{
    public KernelRunTrace(KernelRunId runId, IReadOnlyList<KernelTraceEvent>? events = null)
    {
        RunId = runId;
        Events = KernelContractGuard.ListOrEmpty(events);
    }

    public KernelRunId RunId { get; }

    public IReadOnlyList<KernelTraceEvent> Events { get; }
}

/// <summary>
/// Kernel trace 聚合根。
/// Kernel trace aggregate root.
/// </summary>
public sealed record KernelTrace
{
    public KernelTrace(KernelTraceId traceId, KernelRunId runId, KernelRunTrace runTrace, MetadataBag? metadata = null)
    {
        TraceId = traceId;
        RunId = runId;
        RunTrace = KernelContractGuard.NotNull(runTrace, nameof(runTrace));
        Metadata = KernelContractGuard.MetadataOrEmpty(metadata);
    }

    public KernelTraceId TraceId { get; }

    public KernelRunId RunId { get; }

    public KernelRunTrace RunTrace { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Kernel trace 摘要，供 Adaptive Orchestration 与评估读取。
/// Kernel trace summary consumed by Adaptive Orchestration and evaluation.
/// </summary>
public sealed record KernelTraceSummary
{
    public KernelTraceSummary(
        KernelRunId runId,
        int eventCount = 0,
        IReadOnlyList<string>? rejectionReasons = null,
        IReadOnlyList<string>? checkpointRefs = null)
    {
        RunId = runId;
        EventCount = KernelContractGuard.NonNegative(eventCount, nameof(eventCount));
        RejectionReasons = KernelContractGuard.ListOrEmpty(rejectionReasons);
        CheckpointRefs = KernelContractGuard.ListOrEmpty(checkpointRefs);
    }

    public KernelRunId RunId { get; }

    public int EventCount { get; }

    public IReadOnlyList<string> RejectionReasons { get; }

    public IReadOnlyList<string> CheckpointRefs { get; }
}
