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
    Candidate = 7,
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
        StrategyLifecycleState lifecycleState = StrategyLifecycleState.Candidate,
        IReadOnlyList<StrategyTransitionEvidence>? transitionEvidence = null,
        DateTimeOffset updatedAt = default,
        IReadOnlyList<StrategyLifecycleAuditRecord>? lifecycleAuditRecords = null)
    {
        StrategyId = strategyId;
        Name = KernelContractGuard.RequiredText(name, nameof(name));
        GraphId = graphId;
        LifecycleState = lifecycleState == StrategyLifecycleState.Unspecified
            ? throw new ArgumentOutOfRangeException(nameof(lifecycleState), "LifecycleState 不能为 Unspecified。")
            : lifecycleState;
        TransitionEvidence = KernelContractGuard.ListOrEmpty(transitionEvidence);
        UpdatedAt = updatedAt == default ? DateTimeOffset.UtcNow : updatedAt;
        LifecycleAuditRecords = KernelContractGuard.ListOrEmpty(lifecycleAuditRecords);
    }

    public StrategyId StrategyId { get; }

    public string Name { get; }

    public StageGraphId GraphId { get; }

    public StrategyLifecycleState LifecycleState { get; }

    public IReadOnlyList<StrategyTransitionEvidence> TransitionEvidence { get; }

    public DateTimeOffset UpdatedAt { get; }

    public IReadOnlyList<StrategyLifecycleAuditRecord> LifecycleAuditRecords { get; }
}

/// <summary>
/// Strategy 生命周期审计记录，记录 candidate / trial / promoted / deprecated / rolled_back 的可追踪变更。
/// Strategy lifecycle audit record for traceable candidate / trial / promoted / deprecated / rolled_back changes.
/// </summary>
public sealed record StrategyLifecycleAuditRecord
{
    public StrategyLifecycleAuditRecord(
        string auditId,
        StrategyId strategyId,
        StrategyLifecycleState previousState,
        StrategyLifecycleState targetState,
        IReadOnlyList<string> evidenceRefs,
        IReadOnlyList<string>? metricRefs = null,
        bool humanApproved = false,
        string? reasonRef = null,
        DateTimeOffset occurredAt = default)
    {
        AuditId = KernelContractGuard.RequiredText(auditId, nameof(auditId));
        StrategyId = strategyId;
        PreviousState = previousState;
        TargetState = targetState == StrategyLifecycleState.Unspecified
            ? throw new ArgumentOutOfRangeException(nameof(targetState), "TargetState 不能为 Unspecified。")
            : targetState;
        EvidenceRefs = KernelContractGuard.ListOrEmpty(evidenceRefs);
        if (EvidenceRefs.Count == 0)
        {
            throw new ArgumentException("Strategy lifecycle audit record 必须至少包含一个 evidence ref。", nameof(evidenceRefs));
        }

        MetricRefs = KernelContractGuard.ListOrEmpty(metricRefs);
        HumanApproved = humanApproved;
        ReasonRef = reasonRef;
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
    }

    public string AuditId { get; }

    public StrategyId StrategyId { get; }

    public StrategyLifecycleState PreviousState { get; }

    public StrategyLifecycleState TargetState { get; }

    public IReadOnlyList<string> EvidenceRefs { get; }

    public IReadOnlyList<string> MetricRefs { get; }

    public bool HumanApproved { get; }

    public string? ReasonRef { get; }

    public DateTimeOffset OccurredAt { get; }
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
        DateTimeOffset evaluatedAt = default,
        KernelEvaluationEvidenceSet? evidence = null,
        IReadOnlyList<KernelEvaluationMetricObservation>? observations = null,
        IReadOnlyList<KernelEvaluationDisagreement>? disagreements = null,
        decimal? overallConfidence = null,
        decimal? disagreementScore = null)
    {
        EvaluationId = KernelContractGuard.RequiredText(evaluationId, nameof(evaluationId));
        RunId = runId;
        TraceId = traceId;
        Decision = decision;
        MetricScores = metricScores ?? new Dictionary<string, decimal>(StringComparer.Ordinal);
        SummaryRef = summaryRef;
        EvaluatedAt = evaluatedAt == default ? DateTimeOffset.UtcNow : evaluatedAt;
        Evidence = evidence ?? new KernelEvaluationEvidenceSet();
        Observations = KernelContractGuard.ListOrEmpty(observations);
        Disagreements = KernelContractGuard.ListOrEmpty(disagreements);
        OverallConfidence = ValidateRatio(overallConfidence, nameof(overallConfidence));
        DisagreementScore = ValidateRatio(disagreementScore, nameof(disagreementScore));
    }

    public string EvaluationId { get; }

    public KernelRunId RunId { get; }

    public KernelTraceId TraceId { get; }

    public KernelReviewDecision Decision { get; }

    public IReadOnlyDictionary<string, decimal> MetricScores { get; }

    public string? SummaryRef { get; }

    public DateTimeOffset EvaluatedAt { get; }

    public KernelEvaluationEvidenceSet Evidence { get; }

    public IReadOnlyList<KernelEvaluationMetricObservation> Observations { get; }

    public IReadOnlyList<KernelEvaluationDisagreement> Disagreements { get; }

    public decimal? OverallConfidence { get; }

    public decimal? DisagreementScore { get; }

    private static decimal? ValidateRatio(decimal? value, string paramName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, "值必须位于 0 到 1 之间。");
        }

        return value;
    }
}
