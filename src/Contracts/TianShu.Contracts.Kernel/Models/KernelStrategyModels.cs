using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel;

/// <summary>
/// Strategy 生命周期状态；默认 Unspecified 不得参与执行。
/// Strategy lifecycle state; default Unspecified must not participate in execution.
/// </summary>
public enum StrategyLifecycleState
{
    Unspecified = 0,
    Draft = 1,
    Validated = 2,
    Trial = 3,
    Promoted = 4,
    Deprecated = 5,
    RolledBack = 6,
}

/// <summary>
/// Strategy transition evidence。
/// Strategy transition evidence.
/// </summary>
public sealed record StrategyTransitionEvidence
{
    public StrategyTransitionEvidence(
        KernelRunId runId,
        KernelTraceId traceId,
        string evidenceRef,
        IReadOnlyList<string>? metricRefs = null,
        bool humanApproved = false)
    {
        RunId = runId;
        TraceId = traceId;
        EvidenceRef = KernelContractGuard.RequiredText(evidenceRef, nameof(evidenceRef));
        MetricRefs = KernelContractGuard.ListOrEmpty(metricRefs);
        HumanApproved = humanApproved;
    }

    public KernelRunId RunId { get; }

    public KernelTraceId TraceId { get; }

    public string EvidenceRef { get; }

    public IReadOnlyList<string> MetricRefs { get; }

    public bool HumanApproved { get; }
}

/// <summary>
/// Strategy 记录。
/// Strategy record.
/// </summary>
public sealed record StrategyRecord
{
    public StrategyRecord(
        StrategyId strategyId,
        string name,
        StageGraphId graphId,
        StrategyLifecycleState lifecycleState = StrategyLifecycleState.Draft,
        IReadOnlyList<StrategyTransitionEvidence>? transitionEvidence = null,
        DateTimeOffset updatedAt = default)
    {
        StrategyId = strategyId;
        Name = KernelContractGuard.RequiredText(name, nameof(name));
        GraphId = graphId;
        LifecycleState = lifecycleState;
        TransitionEvidence = KernelContractGuard.ListOrEmpty(transitionEvidence);
        UpdatedAt = updatedAt == default ? DateTimeOffset.UtcNow : updatedAt;
    }

    public StrategyId StrategyId { get; }

    public string Name { get; }

    public StageGraphId GraphId { get; }

    public StrategyLifecycleState LifecycleState { get; }

    public IReadOnlyList<StrategyTransitionEvidence> TransitionEvidence { get; }

    public DateTimeOffset UpdatedAt { get; }
}

/// <summary>
/// Kernel 评估结果。
/// Kernel evaluation result.
/// </summary>
public sealed record KernelEvaluationResult
{
    public KernelEvaluationResult(
        string evaluationId,
        KernelRunId runId,
        KernelTraceId traceId,
        KernelReviewDecision decision,
        IReadOnlyDictionary<string, decimal>? metricScores = null,
        string? summaryRef = null,
        DateTimeOffset evaluatedAt = default)
    {
        EvaluationId = KernelContractGuard.RequiredText(evaluationId, nameof(evaluationId));
        RunId = runId;
        TraceId = traceId;
        Decision = decision;
        MetricScores = metricScores ?? new Dictionary<string, decimal>(StringComparer.Ordinal);
        SummaryRef = summaryRef;
        EvaluatedAt = evaluatedAt == default ? DateTimeOffset.UtcNow : evaluatedAt;
    }

    public string EvaluationId { get; }

    public KernelRunId RunId { get; }

    public KernelTraceId TraceId { get; }

    public KernelReviewDecision Decision { get; }

    public IReadOnlyDictionary<string, decimal> MetricScores { get; }

    public string? SummaryRef { get; }

    public DateTimeOffset EvaluatedAt { get; }
}
