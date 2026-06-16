using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Kernel 单次运行选项；只提供默认值和约束，不替代 Kernel 校验。
/// Options for a single Kernel run; provides defaults and constraints without replacing Kernel validation.
/// </summary>
public sealed record KernelRunOptions
{
    public KernelRunOptions(
        KernelRunId? runId = null,
        StageGraphId? preferredGraphId = null,
        StrategyId? preferredStrategyId = null,
        KernelBudget? budgetOverride = null,
        bool enableAdaptive = true,
        bool requireHumanGate = true,
        MetadataBag? metadata = null)
    {
        RunId = runId;
        PreferredGraphId = preferredGraphId;
        PreferredStrategyId = preferredStrategyId;
        BudgetOverride = budgetOverride;
        EnableAdaptive = enableAdaptive;
        RequireHumanGate = requireHumanGate;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public KernelRunId? RunId { get; }

    public StageGraphId? PreferredGraphId { get; }

    public StrategyId? PreferredStrategyId { get; }

    public KernelBudget? BudgetOverride { get; }

    public bool EnableAdaptive { get; }

    public bool RequireHumanGate { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Kernel 运行态快照，供 Core、Adaptive、Validator 和 Interpreter 共享。
/// Kernel run-state snapshot shared by Core, Adaptive, Validator, and Interpreter.
/// </summary>
public sealed record KernelRunState
{
    public KernelRunState(
        KernelRunId runId,
        CoreIntentId sourceIntentId,
        KernelRunLifecycleState lifecycleState = KernelRunLifecycleState.Created,
        StageGraphId? selectedGraphId = null,
        StageId? currentStageId = null,
        StrategyId? activeStrategyId = null,
        IReadOnlyList<KernelProposal>? pendingProposals = null,
        IReadOnlyList<KernelValidationIssue>? validationIssues = null,
        MetadataBag? metadata = null)
    {
        RunId = runId;
        SourceIntentId = sourceIntentId;
        LifecycleState = lifecycleState;
        SelectedGraphId = selectedGraphId;
        CurrentStageId = currentStageId;
        ActiveStrategyId = activeStrategyId;
        PendingProposals = pendingProposals ?? Array.Empty<KernelProposal>();
        ValidationIssues = validationIssues ?? Array.Empty<KernelValidationIssue>();
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public KernelRunId RunId { get; init; }

    public CoreIntentId SourceIntentId { get; init; }

    public KernelRunLifecycleState LifecycleState { get; init; }

    public StageGraphId? SelectedGraphId { get; init; }

    public StageId? CurrentStageId { get; init; }

    public StrategyId? ActiveStrategyId { get; init; }

    public IReadOnlyList<KernelProposal> PendingProposals { get; init; }

    public IReadOnlyList<KernelValidationIssue> ValidationIssues { get; init; }

    public MetadataBag Metadata { get; init; }
}

/// <summary>
/// Kernel 运行结果；ExecutionPlan 只有在 Stable Kernel Core 批准后才允许出现。
/// Kernel run result; ExecutionPlan is present only after Stable Kernel Core approval.
/// </summary>
public sealed record KernelRunResult
{
    public KernelRunResult(
        KernelRunId runId,
        CoreIntentId sourceIntentId,
        KernelRunLifecycleState lifecycleState,
        KernelValidationResult validation,
        ExecutionPlan? executionPlan = null,
        KernelTraceId? traceId = null,
        StageGraph? approvedStageGraph = null,
        IReadOnlyList<StageResult>? stageResults = null,
        MetadataBag? metadata = null)
    {
        RunId = runId;
        SourceIntentId = sourceIntentId;
        LifecycleState = lifecycleState;
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        ExecutionPlan = executionPlan;
        TraceId = traceId;
        ApprovedStageGraph = approvedStageGraph;
        StageResults = stageResults ?? Array.Empty<StageResult>();
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public KernelRunId RunId { get; }

    public CoreIntentId SourceIntentId { get; }

    public KernelRunLifecycleState LifecycleState { get; }

    public KernelValidationResult Validation { get; }

    public ExecutionPlan? ExecutionPlan { get; }

    public KernelTraceId? TraceId { get; }

    public StageGraph? ApprovedStageGraph { get; }

    public IReadOnlyList<StageResult> StageResults { get; }

    public MetadataBag Metadata { get; }
}
