namespace TianShu.Contracts.Kernel;

/// <summary>
/// Kernel 异质交叉评审中的模型评审者声明。
/// Model reviewer declaration for heterogeneous Kernel cross-review.
/// </summary>
public sealed record KernelCrossReviewReviewerSpec
{
    public KernelCrossReviewReviewerSpec(
        string reviewerId,
        string modelRouteRef,
        string providerKey,
        string model,
        IReadOnlyList<string>? metricIds = null)
    {
        ReviewerId = KernelContractGuard.RequiredText(reviewerId, nameof(reviewerId));
        ModelRouteRef = KernelContractGuard.RequiredText(modelRouteRef, nameof(modelRouteRef));
        ProviderKey = KernelContractGuard.RequiredText(providerKey, nameof(providerKey));
        Model = KernelContractGuard.RequiredText(model, nameof(model));
        MetricIds = KernelContractGuard.ListOrEmpty(metricIds);
    }

    public string ReviewerId { get; }

    public string ModelRouteRef { get; }

    public string ProviderKey { get; }

    public string Model { get; }

    public IReadOnlyList<string> MetricIds { get; }
}

/// <summary>
/// 单个评审者对单个指标给出的结构化评分。
/// Structured score from one reviewer for one metric.
/// </summary>
public sealed record KernelCrossReviewMetricScore
{
    public KernelCrossReviewMetricScore(
        string metricId,
        decimal score,
        decimal confidence,
        decimal uncertainty,
        string reason,
        string evidenceRef)
    {
        MetricId = KernelContractGuard.RequiredText(metricId, nameof(metricId));
        Score = ValidateRatio(score, nameof(score));
        Confidence = ValidateRatio(confidence, nameof(confidence));
        Uncertainty = ValidateRatio(uncertainty, nameof(uncertainty));
        Reason = KernelContractGuard.RequiredText(reason, nameof(reason));
        EvidenceRef = KernelContractGuard.RequiredText(evidenceRef, nameof(evidenceRef));
    }

    public string MetricId { get; }

    public decimal Score { get; }

    public decimal Confidence { get; }

    public decimal Uncertainty { get; }

    public string Reason { get; }

    public string EvidenceRef { get; }

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
/// 单个评审者提交的交叉评审结果。
/// Cross-review submission from one reviewer.
/// </summary>
public sealed record KernelCrossReviewSubmission
{
    public KernelCrossReviewSubmission(
        string reviewId,
        string reviewerId,
        string modelRouteRef,
        IReadOnlyList<KernelCrossReviewMetricScore> scores,
        string summary,
        string evidenceRef,
        DateTimeOffset submittedAt = default)
    {
        ReviewId = KernelContractGuard.RequiredText(reviewId, nameof(reviewId));
        ReviewerId = KernelContractGuard.RequiredText(reviewerId, nameof(reviewerId));
        ModelRouteRef = KernelContractGuard.RequiredText(modelRouteRef, nameof(modelRouteRef));
        Scores = KernelContractGuard.ListOrEmpty(scores);
        if (Scores.Count == 0)
        {
            throw new ArgumentException("交叉评审提交必须包含至少一个评分。", nameof(scores));
        }

        Summary = KernelContractGuard.RequiredText(summary, nameof(summary));
        EvidenceRef = KernelContractGuard.RequiredText(evidenceRef, nameof(evidenceRef));
        SubmittedAt = submittedAt == default ? DateTimeOffset.UtcNow : submittedAt;
    }

    public string ReviewId { get; }

    public string ReviewerId { get; }

    public string ModelRouteRef { get; }

    public IReadOnlyList<KernelCrossReviewMetricScore> Scores { get; }

    public string Summary { get; }

    public string EvidenceRef { get; }

    public DateTimeOffset SubmittedAt { get; }
}

/// <summary>
/// 异质交叉评审实验请求；A 执行，B/C 等评审者提交结构化结果。
/// Heterogeneous cross-review experiment request where A executes and reviewers such as B/C submit structured results.
/// </summary>
public sealed record KernelCrossReviewExperimentRequest
{
    public KernelCrossReviewExperimentRequest(
        string experimentId,
        KernelRunId runId,
        KernelTraceId traceId,
        string executorRef,
        KernelEvaluationResult baselineEvaluation,
        IReadOnlyList<KernelCrossReviewReviewerSpec> reviewers,
        IReadOnlyList<KernelCrossReviewSubmission> submissions,
        decimal disagreementThreshold = 0.25m)
    {
        ExperimentId = KernelContractGuard.RequiredText(experimentId, nameof(experimentId));
        RunId = runId;
        TraceId = traceId;
        ExecutorRef = KernelContractGuard.RequiredText(executorRef, nameof(executorRef));
        BaselineEvaluation = KernelContractGuard.NotNull(baselineEvaluation, nameof(baselineEvaluation));
        Reviewers = KernelContractGuard.ListOrEmpty(reviewers);
        if (Reviewers.Select(static item => item.ReviewerId).Distinct(StringComparer.Ordinal).Count() < 2)
        {
            throw new ArgumentException("异质交叉评审至少需要两个不同评审者。", nameof(reviewers));
        }

        if (Reviewers.Select(static item => $"{item.ProviderKey}/{item.Model}").Distinct(StringComparer.Ordinal).Count() < 2)
        {
            throw new ArgumentException("异质交叉评审至少需要两个不同 provider/model 组合。", nameof(reviewers));
        }

        Submissions = KernelContractGuard.ListOrEmpty(submissions);
        if (Submissions.Select(static item => item.ReviewerId).Distinct(StringComparer.Ordinal).Count() < 2)
        {
            throw new ArgumentException("异质交叉评审至少需要两个不同评审者的提交。", nameof(submissions));
        }

        DisagreementThreshold = disagreementThreshold is < 0m or > 1m
            ? throw new ArgumentOutOfRangeException(nameof(disagreementThreshold), "DisagreementThreshold 必须位于 0 到 1 之间。")
            : disagreementThreshold;
    }

    public string ExperimentId { get; }

    public KernelRunId RunId { get; }

    public KernelTraceId TraceId { get; }

    public string ExecutorRef { get; }

    public KernelEvaluationResult BaselineEvaluation { get; }

    public IReadOnlyList<KernelCrossReviewReviewerSpec> Reviewers { get; }

    public IReadOnlyList<KernelCrossReviewSubmission> Submissions { get; }

    public decimal DisagreementThreshold { get; }
}

/// <summary>
/// 单个评审者的交叉评审报告。
/// Cross-review report for a single reviewer.
/// </summary>
public sealed record KernelCrossReviewReviewerReport
{
    public KernelCrossReviewReviewerReport(
        string reviewerId,
        string modelRouteRef,
        IReadOnlyList<KernelEvaluationMetricObservation> observations,
        string summaryRef,
        decimal averageScore,
        decimal averageConfidence,
        decimal averageUncertainty)
    {
        ReviewerId = KernelContractGuard.RequiredText(reviewerId, nameof(reviewerId));
        ModelRouteRef = KernelContractGuard.RequiredText(modelRouteRef, nameof(modelRouteRef));
        Observations = KernelContractGuard.ListOrEmpty(observations);
        SummaryRef = KernelContractGuard.RequiredText(summaryRef, nameof(summaryRef));
        AverageScore = ValidateRatio(averageScore, nameof(averageScore));
        AverageConfidence = ValidateRatio(averageConfidence, nameof(averageConfidence));
        AverageUncertainty = ValidateRatio(averageUncertainty, nameof(averageUncertainty));
    }

    public string ReviewerId { get; }

    public string ModelRouteRef { get; }

    public IReadOnlyList<KernelEvaluationMetricObservation> Observations { get; }

    public string SummaryRef { get; }

    public decimal AverageScore { get; }

    public decimal AverageConfidence { get; }

    public decimal AverageUncertainty { get; }

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
/// 异质交叉评审实验报告。
/// Heterogeneous cross-review experiment report.
/// </summary>
public sealed record KernelCrossReviewExperimentReport
{
    public KernelCrossReviewExperimentReport(
        string experimentId,
        KernelRunId runId,
        KernelTraceId traceId,
        string executorRef,
        KernelEvaluationEvidenceSet evidence,
        IReadOnlyList<KernelCrossReviewReviewerReport> reviewerReports,
        IReadOnlyList<KernelEvaluationMetricObservation> observations,
        IReadOnlyList<KernelEvaluationDisagreement> disagreements,
        decimal averageScore,
        decimal averageConfidence,
        decimal averageUncertainty,
        bool requiresHumanGate)
    {
        ExperimentId = KernelContractGuard.RequiredText(experimentId, nameof(experimentId));
        RunId = runId;
        TraceId = traceId;
        ExecutorRef = KernelContractGuard.RequiredText(executorRef, nameof(executorRef));
        Evidence = KernelContractGuard.NotNull(evidence, nameof(evidence));
        ReviewerReports = KernelContractGuard.ListOrEmpty(reviewerReports);
        Observations = KernelContractGuard.ListOrEmpty(observations);
        Disagreements = KernelContractGuard.ListOrEmpty(disagreements);
        AverageScore = ValidateRatio(averageScore, nameof(averageScore));
        AverageConfidence = ValidateRatio(averageConfidence, nameof(averageConfidence));
        AverageUncertainty = ValidateRatio(averageUncertainty, nameof(averageUncertainty));
        RequiresHumanGate = requiresHumanGate;
    }

    public string ExperimentId { get; }

    public KernelRunId RunId { get; }

    public KernelTraceId TraceId { get; }

    public string ExecutorRef { get; }

    public KernelEvaluationEvidenceSet Evidence { get; }

    public IReadOnlyList<KernelCrossReviewReviewerReport> ReviewerReports { get; }

    public IReadOnlyList<KernelEvaluationMetricObservation> Observations { get; }

    public IReadOnlyList<KernelEvaluationDisagreement> Disagreements { get; }

    public decimal AverageScore { get; }

    public decimal AverageConfidence { get; }

    public decimal AverageUncertainty { get; }

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
