namespace TianShu.Contracts.Kernel;

/// <summary>
/// Kernel 评价指标类型，区分可观测事实、客观锚点、模型裁判、置信度和分歧。
/// Kernel evaluation metric kind, separating observable facts, objective anchors, model judges, confidence, and disagreement.
/// </summary>
public enum KernelEvaluationMetricKind
{
    Unspecified = 0,
    Observable = 1,
    ObjectiveAnchor = 2,
    ModelJudge = 3,
    Confidence = 4,
    Disagreement = 5,
}

/// <summary>
/// Kernel 评价信号来源。
/// Kernel evaluation signal source.
/// </summary>
public enum KernelEvaluationSignalKind
{
    Unspecified = 0,
    RuntimeTrace = 1,
    RuntimeMetrics = 2,
    CandidateValidation = 3,
    CandidateTrial = 4,
    ObjectiveAnchor = 5,
    ModelJudge = 6,
    HumanFeedback = 7,
    Diagnostics = 8,
}

/// <summary>
/// Kernel 评价分歧类型。
/// Kernel evaluation disagreement kind.
/// </summary>
public enum KernelEvaluationDisagreementKind
{
    Unspecified = 0,
    ObjectiveAnchorConflict = 1,
    ModelJudgeDisagreement = 2,
    MetricVariance = 3,
    MissingEvidence = 4,
}

/// <summary>
/// Kernel 评价证据集合，保存指标可追溯的引用，不保存敏感原文。
/// Kernel evaluation evidence set that stores traceable references without sensitive raw payloads.
/// </summary>
public sealed record KernelEvaluationEvidenceSet
{
    public KernelEvaluationEvidenceSet(
        IReadOnlyList<string>? traceRefs = null,
        IReadOnlyList<string>? runtimeMetricRefs = null,
        IReadOnlyList<string>? diagnosticRefs = null,
        IReadOnlyList<string>? objectiveAnchorRefs = null,
        IReadOnlyList<string>? modelJudgeRefs = null,
        IReadOnlyList<string>? humanFeedbackRefs = null)
    {
        TraceRefs = KernelContractGuard.ListOrEmpty(traceRefs);
        RuntimeMetricRefs = KernelContractGuard.ListOrEmpty(runtimeMetricRefs);
        DiagnosticRefs = KernelContractGuard.ListOrEmpty(diagnosticRefs);
        ObjectiveAnchorRefs = KernelContractGuard.ListOrEmpty(objectiveAnchorRefs);
        ModelJudgeRefs = KernelContractGuard.ListOrEmpty(modelJudgeRefs);
        HumanFeedbackRefs = KernelContractGuard.ListOrEmpty(humanFeedbackRefs);
    }

    public IReadOnlyList<string> TraceRefs { get; }

    public IReadOnlyList<string> RuntimeMetricRefs { get; }

    public IReadOnlyList<string> DiagnosticRefs { get; }

    public IReadOnlyList<string> ObjectiveAnchorRefs { get; }

    public IReadOnlyList<string> ModelJudgeRefs { get; }

    public IReadOnlyList<string> HumanFeedbackRefs { get; }
}

/// <summary>
/// 单个 Kernel 评价指标观测值。
/// Single Kernel evaluation metric observation.
/// </summary>
public sealed record KernelEvaluationMetricObservation
{
    public KernelEvaluationMetricObservation(
        string metricId,
        KernelEvaluationMetricKind metricKind,
        KernelEvaluationSignalKind signalKind,
        string evidenceRef,
        decimal? score = null,
        decimal? observedValue = null,
        string? unit = null,
        decimal weight = 1m,
        decimal confidence = 1m,
        bool estimated = false,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        MetricId = KernelContractGuard.RequiredText(metricId, nameof(metricId));
        MetricKind = metricKind == KernelEvaluationMetricKind.Unspecified
            ? throw new ArgumentOutOfRangeException(nameof(metricKind), "MetricKind 不能为 Unspecified。")
            : metricKind;
        SignalKind = signalKind == KernelEvaluationSignalKind.Unspecified
            ? throw new ArgumentOutOfRangeException(nameof(signalKind), "SignalKind 不能为 Unspecified。")
            : signalKind;
        EvidenceRef = KernelContractGuard.RequiredText(evidenceRef, nameof(evidenceRef));
        Score = ValidateRatio(score, nameof(score));
        ObservedValue = observedValue;
        Unit = unit;
        Weight = weight < 0m ? throw new ArgumentOutOfRangeException(nameof(weight), "Weight 不能为负数。") : weight;
        Confidence = ValidateRatio(confidence, nameof(confidence))!.Value;
        Estimated = estimated;
        Metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public string MetricId { get; }

    public KernelEvaluationMetricKind MetricKind { get; }

    public KernelEvaluationSignalKind SignalKind { get; }

    public string EvidenceRef { get; }

    public decimal? Score { get; }

    public decimal? ObservedValue { get; }

    public string? Unit { get; }

    public decimal Weight { get; }

    public decimal Confidence { get; }

    public bool Estimated { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static decimal? ValidateRatio(decimal? value, string paramName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, "值必须位于 0 到 1 之间。");
        }

        return value;
    }
}

/// <summary>
/// Kernel 评价分歧记录，用于表达客观锚点、模型裁判或指标之间的冲突。
/// Kernel evaluation disagreement record for conflicts between objective anchors, model judges, or metrics.
/// </summary>
public sealed record KernelEvaluationDisagreement
{
    public KernelEvaluationDisagreement(
        string disagreementId,
        KernelEvaluationDisagreementKind disagreementKind,
        IReadOnlyList<string> metricIds,
        string reason,
        string evidenceRef,
        decimal severity = 1m,
        bool requiresHumanGate = true)
    {
        DisagreementId = KernelContractGuard.RequiredText(disagreementId, nameof(disagreementId));
        DisagreementKind = disagreementKind == KernelEvaluationDisagreementKind.Unspecified
            ? throw new ArgumentOutOfRangeException(nameof(disagreementKind), "DisagreementKind 不能为 Unspecified。")
            : disagreementKind;
        MetricIds = KernelContractGuard.ListOrEmpty(metricIds);
        if (MetricIds.Count == 0)
        {
            throw new ArgumentException("分歧必须至少引用一个指标。", nameof(metricIds));
        }

        Reason = KernelContractGuard.RequiredText(reason, nameof(reason));
        EvidenceRef = KernelContractGuard.RequiredText(evidenceRef, nameof(evidenceRef));
        Severity = severity is < 0m or > 1m
            ? throw new ArgumentOutOfRangeException(nameof(severity), "Severity 必须位于 0 到 1 之间。")
            : severity;
        RequiresHumanGate = requiresHumanGate;
    }

    public string DisagreementId { get; }

    public KernelEvaluationDisagreementKind DisagreementKind { get; }

    public IReadOnlyList<string> MetricIds { get; }

    public string Reason { get; }

    public string EvidenceRef { get; }

    public decimal Severity { get; }

    public bool RequiresHumanGate { get; }
}
