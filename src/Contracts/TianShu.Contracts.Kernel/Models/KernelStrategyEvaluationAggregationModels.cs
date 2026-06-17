namespace TianShu.Contracts.Kernel;

/// <summary>
/// 策略级评价聚合门禁状态。
/// Strategy-level evaluation aggregation gate state.
/// </summary>
public enum KernelStrategyEvaluationGateState
{
    Unspecified = 0,
    InsufficientEvidence = 1,
    RequiresHumanGate = 2,
    BlockedByDisagreement = 3,
    PromotionReady = 4,
}

/// <summary>
/// 单个策略评价样本，绑定一次 run/evaluation 以及可选的交叉评审和客观锚点校准结果。
/// Single strategy evaluation sample binding one run/evaluation plus optional cross-review and objective-anchor calibration reports.
/// </summary>
public sealed record KernelStrategyEvaluationSample
{
    public KernelStrategyEvaluationSample(
        string sampleId,
        StrategyId strategyId,
        KernelRunId runId,
        KernelTraceId traceId,
        KernelEvaluationResult evaluation,
        string evidenceRef,
        KernelCrossReviewExperimentReport? crossReviewReport = null,
        KernelObjectiveAnchorCalibrationReport? objectiveAnchorCalibrationReport = null,
        DateTimeOffset sampledAt = default)
    {
        SampleId = KernelContractGuard.RequiredText(sampleId, nameof(sampleId));
        StrategyId = strategyId;
        RunId = runId;
        TraceId = traceId;
        Evaluation = KernelContractGuard.NotNull(evaluation, nameof(evaluation));
        EvidenceRef = KernelContractGuard.RequiredText(evidenceRef, nameof(evidenceRef));
        CrossReviewReport = crossReviewReport;
        ObjectiveAnchorCalibrationReport = objectiveAnchorCalibrationReport;
        SampledAt = sampledAt == default ? DateTimeOffset.UtcNow : sampledAt;
    }

    public string SampleId { get; }

    public StrategyId StrategyId { get; }

    public KernelRunId RunId { get; }

    public KernelTraceId TraceId { get; }

    public KernelEvaluationResult Evaluation { get; }

    public string EvidenceRef { get; }

    public KernelCrossReviewExperimentReport? CrossReviewReport { get; }

    public KernelObjectiveAnchorCalibrationReport? ObjectiveAnchorCalibrationReport { get; }

    public DateTimeOffset SampledAt { get; }
}

/// <summary>
/// 策略级单指标统计聚合。
/// Strategy-level aggregate for a single metric.
/// </summary>
public sealed record KernelStrategyMetricAggregate
{
    public KernelStrategyMetricAggregate(
        string metricId,
        int sampleCount,
        decimal meanScore,
        decimal minScore,
        decimal maxScore,
        decimal meanConfidence,
        int estimatedCount,
        IReadOnlyList<KernelEvaluationSignalKind>? signalKinds = null)
    {
        MetricId = KernelContractGuard.RequiredText(metricId, nameof(metricId));
        SampleCount = KernelContractGuard.NonNegative(sampleCount, nameof(sampleCount));
        MeanScore = ValidateRatio(meanScore, nameof(meanScore));
        MinScore = ValidateRatio(minScore, nameof(minScore));
        MaxScore = ValidateRatio(maxScore, nameof(maxScore));
        MeanConfidence = ValidateRatio(meanConfidence, nameof(meanConfidence));
        EstimatedCount = KernelContractGuard.NonNegative(estimatedCount, nameof(estimatedCount));
        SignalKinds = KernelContractGuard.ListOrEmpty(signalKinds);
    }

    public string MetricId { get; }

    public int SampleCount { get; }

    public decimal MeanScore { get; }

    public decimal MinScore { get; }

    public decimal MaxScore { get; }

    public decimal MeanConfidence { get; }

    public int EstimatedCount { get; }

    public IReadOnlyList<KernelEvaluationSignalKind> SignalKinds { get; }

    private static decimal ValidateRatio(decimal value, string paramName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, "值必须位于 0 到 1 之间。");
        }

        return value;
    }
}

/// <summary>
/// 单个策略的统计比较结果；它只是 promotion 证据，不执行 strategy transition。
/// Statistical comparison result for one strategy; it is promotion evidence only and does not execute strategy transitions.
/// </summary>
public sealed record KernelStrategyComparison
{
    public KernelStrategyComparison(
        StrategyId strategyId,
        int sampleCount,
        IReadOnlyList<KernelStrategyMetricAggregate> metricAggregates,
        decimal averageScore,
        decimal averageConfidence,
        int disagreementCount,
        int objectiveAnchorConflictCount,
        int missingEvidenceCount,
        bool modelJudgeOnly,
        KernelStrategyEvaluationGateState gateState,
        bool promotionReady,
        bool requiresHumanGate,
        IReadOnlyList<string>? blockingReasons = null)
    {
        StrategyId = strategyId;
        SampleCount = KernelContractGuard.NonNegative(sampleCount, nameof(sampleCount));
        MetricAggregates = KernelContractGuard.ListOrEmpty(metricAggregates);
        AverageScore = ValidateRatio(averageScore, nameof(averageScore));
        AverageConfidence = ValidateRatio(averageConfidence, nameof(averageConfidence));
        DisagreementCount = KernelContractGuard.NonNegative(disagreementCount, nameof(disagreementCount));
        ObjectiveAnchorConflictCount = KernelContractGuard.NonNegative(objectiveAnchorConflictCount, nameof(objectiveAnchorConflictCount));
        MissingEvidenceCount = KernelContractGuard.NonNegative(missingEvidenceCount, nameof(missingEvidenceCount));
        ModelJudgeOnly = modelJudgeOnly;
        GateState = gateState == KernelStrategyEvaluationGateState.Unspecified
            ? throw new ArgumentOutOfRangeException(nameof(gateState), "GateState 不能为 Unspecified。")
            : gateState;
        PromotionReady = promotionReady;
        RequiresHumanGate = requiresHumanGate;
        BlockingReasons = KernelContractGuard.ListOrEmpty(blockingReasons);
    }

    public StrategyId StrategyId { get; }

    public int SampleCount { get; }

    public IReadOnlyList<KernelStrategyMetricAggregate> MetricAggregates { get; }

    public decimal AverageScore { get; }

    public decimal AverageConfidence { get; }

    public int DisagreementCount { get; }

    public int ObjectiveAnchorConflictCount { get; }

    public int MissingEvidenceCount { get; }

    public bool ModelJudgeOnly { get; }

    public KernelStrategyEvaluationGateState GateState { get; }

    public bool PromotionReady { get; }

    public bool RequiresHumanGate { get; }

    public IReadOnlyList<string> BlockingReasons { get; }

    private static decimal ValidateRatio(decimal value, string paramName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, "值必须位于 0 到 1 之间。");
        }

        return value;
    }
}

/// <summary>
/// 策略级评价聚合请求。
/// Strategy-level evaluation aggregation request.
/// </summary>
public sealed record KernelStrategyEvaluationAggregationRequest
{
    public KernelStrategyEvaluationAggregationRequest(
        string aggregationId,
        IReadOnlyList<KernelStrategyEvaluationSample> samples,
        StrategyId? baselineStrategyId = null,
        int minimumSamplesPerStrategy = 2,
        DateTimeOffset requestedAt = default)
    {
        AggregationId = KernelContractGuard.RequiredText(aggregationId, nameof(aggregationId));
        Samples = KernelContractGuard.ListOrEmpty(samples);
        if (Samples.Count == 0)
        {
            throw new ArgumentException("策略级评价聚合必须包含至少一个样本。", nameof(samples));
        }

        BaselineStrategyId = baselineStrategyId;
        MinimumSamplesPerStrategy = minimumSamplesPerStrategy <= 0
            ? throw new ArgumentOutOfRangeException(nameof(minimumSamplesPerStrategy), "MinimumSamplesPerStrategy 必须大于 0。")
            : minimumSamplesPerStrategy;
        RequestedAt = requestedAt == default ? DateTimeOffset.UtcNow : requestedAt;
    }

    public string AggregationId { get; }

    public IReadOnlyList<KernelStrategyEvaluationSample> Samples { get; }

    public StrategyId? BaselineStrategyId { get; }

    public int MinimumSamplesPerStrategy { get; }

    public DateTimeOffset RequestedAt { get; }
}

/// <summary>
/// 策略级评价聚合报告。
/// Strategy-level evaluation aggregation report.
/// </summary>
public sealed record KernelStrategyEvaluationAggregationReport
{
    public KernelStrategyEvaluationAggregationReport(
        string aggregationId,
        IReadOnlyList<KernelStrategyComparison> comparisons,
        StrategyId? bestCandidateStrategyId,
        bool hasPromotionReadyCandidate,
        bool requiresHumanGate,
        DateTimeOffset aggregatedAt = default)
    {
        AggregationId = KernelContractGuard.RequiredText(aggregationId, nameof(aggregationId));
        Comparisons = KernelContractGuard.ListOrEmpty(comparisons);
        if (Comparisons.Count == 0)
        {
            throw new ArgumentException("策略级评价聚合报告必须包含至少一个 comparison。", nameof(comparisons));
        }

        BestCandidateStrategyId = bestCandidateStrategyId;
        HasPromotionReadyCandidate = hasPromotionReadyCandidate;
        RequiresHumanGate = requiresHumanGate;
        AggregatedAt = aggregatedAt == default ? DateTimeOffset.UtcNow : aggregatedAt;
    }

    public string AggregationId { get; }

    public IReadOnlyList<KernelStrategyComparison> Comparisons { get; }

    public StrategyId? BestCandidateStrategyId { get; }

    public bool HasPromotionReadyCandidate { get; }

    public bool RequiresHumanGate { get; }

    public DateTimeOffset AggregatedAt { get; }
}
