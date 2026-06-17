namespace TianShu.Contracts.Kernel;

/// <summary>
/// 客观锚点类型，用于校准模型裁判可信度。
/// Objective-anchor kind used to calibrate model-judge credibility.
/// </summary>
public enum KernelObjectiveAnchorKind
{
    Unspecified = 0,
    BuildSucceeded = 1,
    TestsPassed = 2,
    GoldenAnswer = 3,
    HumanLabel = 4,
}

/// <summary>
/// 单个客观锚点观测。
/// Single objective-anchor observation.
/// </summary>
public sealed record KernelObjectiveAnchorObservation
{
    public KernelObjectiveAnchorObservation(
        string anchorId,
        KernelObjectiveAnchorKind anchorKind,
        string metricId,
        decimal score,
        decimal confidence,
        string evidenceRef,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        AnchorId = KernelContractGuard.RequiredText(anchorId, nameof(anchorId));
        AnchorKind = anchorKind == KernelObjectiveAnchorKind.Unspecified
            ? throw new ArgumentOutOfRangeException(nameof(anchorKind), "AnchorKind 不能为 Unspecified。")
            : anchorKind;
        MetricId = KernelContractGuard.RequiredText(metricId, nameof(metricId));
        Score = ValidateRatio(score, nameof(score));
        Confidence = ValidateRatio(confidence, nameof(confidence));
        EvidenceRef = KernelContractGuard.RequiredText(evidenceRef, nameof(evidenceRef));
        Reason = KernelContractGuard.RequiredText(reason, nameof(reason));
        Metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public string AnchorId { get; }

    public KernelObjectiveAnchorKind AnchorKind { get; }

    public string MetricId { get; }

    public decimal Score { get; }

    public decimal Confidence { get; }

    public string EvidenceRef { get; }

    public string Reason { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

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
/// 客观锚点校准请求。
/// Objective-anchor calibration request.
/// </summary>
public sealed record KernelObjectiveAnchorCalibrationRequest
{
    public KernelObjectiveAnchorCalibrationRequest(
        string calibrationId,
        KernelRunId runId,
        KernelTraceId traceId,
        IReadOnlyList<KernelEvaluationMetricObservation> modelJudgeObservations,
        IReadOnlyList<KernelObjectiveAnchorObservation> objectiveAnchors,
        decimal conflictThreshold = 0.25m)
    {
        CalibrationId = KernelContractGuard.RequiredText(calibrationId, nameof(calibrationId));
        RunId = runId;
        TraceId = traceId;
        ModelJudgeObservations = KernelContractGuard.ListOrEmpty(modelJudgeObservations);
        if (ModelJudgeObservations.Count == 0)
        {
            throw new ArgumentException("客观锚点校准必须包含至少一个模型裁判观测。", nameof(modelJudgeObservations));
        }

        if (ModelJudgeObservations.Any(static item => item.MetricKind != KernelEvaluationMetricKind.ModelJudge))
        {
            throw new ArgumentException("客观锚点校准只能接收 ModelJudge 指标作为待校准对象。", nameof(modelJudgeObservations));
        }

        ObjectiveAnchors = KernelContractGuard.ListOrEmpty(objectiveAnchors);
        if (ObjectiveAnchors.Count == 0)
        {
            throw new ArgumentException("客观锚点校准必须包含至少一个客观锚点。", nameof(objectiveAnchors));
        }

        ConflictThreshold = conflictThreshold is < 0m or > 1m
            ? throw new ArgumentOutOfRangeException(nameof(conflictThreshold), "ConflictThreshold 必须位于 0 到 1 之间。")
            : conflictThreshold;
    }

    public string CalibrationId { get; }

    public KernelRunId RunId { get; }

    public KernelTraceId TraceId { get; }

    public IReadOnlyList<KernelEvaluationMetricObservation> ModelJudgeObservations { get; }

    public IReadOnlyList<KernelObjectiveAnchorObservation> ObjectiveAnchors { get; }

    public decimal ConflictThreshold { get; }
}

/// <summary>
/// 客观锚点校准报告。
/// Objective-anchor calibration report.
/// </summary>
public sealed record KernelObjectiveAnchorCalibrationReport
{
    public KernelObjectiveAnchorCalibrationReport(
        string calibrationId,
        KernelRunId runId,
        KernelTraceId traceId,
        KernelEvaluationEvidenceSet evidence,
        IReadOnlyList<KernelEvaluationMetricObservation> objectiveAnchorObservations,
        IReadOnlyList<KernelEvaluationMetricObservation> calibratedModelJudgeObservations,
        IReadOnlyList<KernelEvaluationDisagreement> disagreements,
        decimal objectiveAnchorScore,
        decimal modelJudgeScore,
        decimal originalModelJudgeConfidence,
        decimal calibratedModelJudgeConfidence,
        bool requiresHumanGate)
    {
        CalibrationId = KernelContractGuard.RequiredText(calibrationId, nameof(calibrationId));
        RunId = runId;
        TraceId = traceId;
        Evidence = KernelContractGuard.NotNull(evidence, nameof(evidence));
        ObjectiveAnchorObservations = KernelContractGuard.ListOrEmpty(objectiveAnchorObservations);
        CalibratedModelJudgeObservations = KernelContractGuard.ListOrEmpty(calibratedModelJudgeObservations);
        Disagreements = KernelContractGuard.ListOrEmpty(disagreements);
        ObjectiveAnchorScore = ValidateRatio(objectiveAnchorScore, nameof(objectiveAnchorScore));
        ModelJudgeScore = ValidateRatio(modelJudgeScore, nameof(modelJudgeScore));
        OriginalModelJudgeConfidence = ValidateRatio(originalModelJudgeConfidence, nameof(originalModelJudgeConfidence));
        CalibratedModelJudgeConfidence = ValidateRatio(calibratedModelJudgeConfidence, nameof(calibratedModelJudgeConfidence));
        RequiresHumanGate = requiresHumanGate;
    }

    public string CalibrationId { get; }

    public KernelRunId RunId { get; }

    public KernelTraceId TraceId { get; }

    public KernelEvaluationEvidenceSet Evidence { get; }

    public IReadOnlyList<KernelEvaluationMetricObservation> ObjectiveAnchorObservations { get; }

    public IReadOnlyList<KernelEvaluationMetricObservation> CalibratedModelJudgeObservations { get; }

    public IReadOnlyList<KernelEvaluationDisagreement> Disagreements { get; }

    public decimal ObjectiveAnchorScore { get; }

    public decimal ModelJudgeScore { get; }

    public decimal OriginalModelJudgeConfidence { get; }

    public decimal CalibratedModelJudgeConfidence { get; }

    public bool RequiresHumanGate { get; }

    private static decimal ValidateRatio(decimal value, string paramName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, "值必须位于 0 到 1 之间。");
        }

        return value;
    }
}
