using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Adaptive 候选试运行模式。
/// Adaptive candidate trial mode.
/// </summary>
public enum AdaptiveCandidateTrialMode
{
    Unspecified = 0,
    ShadowRun = 1,
    BoundedPlanTrial = 2,
}

/// <summary>
/// Adaptive 候选试运行状态。
/// Adaptive candidate trial status.
/// </summary>
public enum AdaptiveCandidateTrialStatus
{
    Unspecified = 0,
    Succeeded = 1,
    Blocked = 2,
    Skipped = 3,
    Failed = 4,
}

/// <summary>
/// 候选执行计划相对基线的结构化差异摘要。
/// Structured diff summary of a candidate execution plan against the baseline.
/// </summary>
public sealed record AdaptiveCandidatePlanDiff
{
    public AdaptiveCandidatePlanDiff(
        StageGraphId baselineGraphId,
        StageGraphId candidateGraphId,
        string baselinePlanId,
        string? candidatePlanId,
        int stepCountDelta,
        int tokenBudgetDelta,
        long timeBudgetMsDelta,
        int toolCallBudgetDelta,
        int maxSideEffectLevelDelta,
        IReadOnlyList<string>? baselineStepKinds = null,
        IReadOnlyList<string>? candidateStepKinds = null,
        IReadOnlyList<string>? addedStepKinds = null,
        IReadOnlyList<string>? removedStepKinds = null)
    {
        BaselineGraphId = baselineGraphId;
        CandidateGraphId = candidateGraphId;
        BaselinePlanId = AbstractionGuard.RequiredText(baselinePlanId, nameof(baselinePlanId));
        CandidatePlanId = candidatePlanId;
        StepCountDelta = stepCountDelta;
        TokenBudgetDelta = tokenBudgetDelta;
        TimeBudgetMsDelta = timeBudgetMsDelta;
        ToolCallBudgetDelta = toolCallBudgetDelta;
        MaxSideEffectLevelDelta = maxSideEffectLevelDelta;
        BaselineStepKinds = baselineStepKinds ?? Array.Empty<string>();
        CandidateStepKinds = candidateStepKinds ?? Array.Empty<string>();
        AddedStepKinds = addedStepKinds ?? Array.Empty<string>();
        RemovedStepKinds = removedStepKinds ?? Array.Empty<string>();
    }

    public StageGraphId BaselineGraphId { get; }

    public StageGraphId CandidateGraphId { get; }

    public string BaselinePlanId { get; }

    public string? CandidatePlanId { get; }

    public int StepCountDelta { get; }

    public int TokenBudgetDelta { get; }

    public long TimeBudgetMsDelta { get; }

    public int ToolCallBudgetDelta { get; }

    public int MaxSideEffectLevelDelta { get; }

    public IReadOnlyList<string> BaselineStepKinds { get; }

    public IReadOnlyList<string> CandidateStepKinds { get; }

    public IReadOnlyList<string> AddedStepKinds { get; }

    public IReadOnlyList<string> RemovedStepKinds { get; }
}

/// <summary>
/// Adaptive 候选试运行请求。
/// Adaptive candidate trial request.
/// </summary>
public sealed record AdaptiveCandidateTrialRequest
{
    public AdaptiveCandidateTrialRequest(
        KernelProposalSet proposalSet,
        AdaptiveCandidateValidationReport validationReport,
        KernelValidationContext context,
        StageGraph baselineGraph,
        ExecutionPlan baselinePlan,
        KernelRunOptions options,
        IReadOnlyList<AdaptiveCandidateTrialMode>? modes = null,
        MetadataBag? metadata = null)
    {
        ProposalSet = proposalSet ?? throw new ArgumentNullException(nameof(proposalSet));
        ValidationReport = validationReport ?? throw new ArgumentNullException(nameof(validationReport));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        BaselineGraph = baselineGraph ?? throw new ArgumentNullException(nameof(baselineGraph));
        BaselinePlan = baselinePlan ?? throw new ArgumentNullException(nameof(baselinePlan));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Modes = modes ?? new[] { AdaptiveCandidateTrialMode.ShadowRun, AdaptiveCandidateTrialMode.BoundedPlanTrial };
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public KernelProposalSet ProposalSet { get; }

    public AdaptiveCandidateValidationReport ValidationReport { get; }

    public KernelValidationContext Context { get; }

    public StageGraph BaselineGraph { get; }

    public ExecutionPlan BaselinePlan { get; }

    public KernelRunOptions Options { get; }

    public IReadOnlyList<AdaptiveCandidateTrialMode> Modes { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 单个候选试运行记录。
/// Trial record for a single candidate.
/// </summary>
public sealed record AdaptiveCandidateTrialRecord
{
    public AdaptiveCandidateTrialRecord(
        KernelProposalId proposalId,
        StageGraphId? graphId,
        AdaptiveCandidateTrialMode mode,
        AdaptiveCandidateTrialStatus status,
        AdaptiveCandidatePlanDiff? diff = null,
        KernelValidationResult? validation = null,
        string? rationaleRef = null,
        bool executedRuntime = false,
        bool promotedStrategy = false,
        MetadataBag? metadata = null)
    {
        ProposalId = proposalId;
        GraphId = graphId;
        Mode = mode;
        Status = status;
        Diff = diff;
        Validation = validation ?? new KernelValidationResult(KernelValidationDecision.Unspecified);
        RationaleRef = rationaleRef;
        ExecutedRuntime = executedRuntime;
        PromotedStrategy = promotedStrategy;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public KernelProposalId ProposalId { get; }

    public StageGraphId? GraphId { get; }

    public AdaptiveCandidateTrialMode Mode { get; }

    public AdaptiveCandidateTrialStatus Status { get; }

    public AdaptiveCandidatePlanDiff? Diff { get; }

    public KernelValidationResult Validation { get; }

    public string? RationaleRef { get; }

    public bool ExecutedRuntime { get; }

    public bool PromotedStrategy { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Adaptive 候选试运行报告；该报告不得作为策略晋升证据的唯一来源。
/// Adaptive candidate trial report; this report must not be the sole evidence for strategy promotion.
/// </summary>
public sealed record AdaptiveCandidateTrialReport
{
    public AdaptiveCandidateTrialReport(
        IReadOnlyList<AdaptiveCandidateTrialRecord>? records = null,
        string? rationaleRef = null,
        MetadataBag? metadata = null)
    {
        Records = records ?? Array.Empty<AdaptiveCandidateTrialRecord>();
        RationaleRef = rationaleRef;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public IReadOnlyList<AdaptiveCandidateTrialRecord> Records { get; }

    public string? RationaleRef { get; }

    public MetadataBag Metadata { get; }

    public int SucceededCount => Records.Count(static record => record.Status == AdaptiveCandidateTrialStatus.Succeeded);

    public int BlockedCount => Records.Count(static record => record.Status == AdaptiveCandidateTrialStatus.Blocked);

    public bool ExecutedRuntime => Records.Any(static record => record.ExecutedRuntime);

    public bool PromotedStrategy => Records.Any(static record => record.PromotedStrategy);
}
