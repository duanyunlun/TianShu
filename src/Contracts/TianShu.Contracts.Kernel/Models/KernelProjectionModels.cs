using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel;

/// <summary>
/// Kernel 运行状态投影枚举。
/// Kernel run-state projection enum.
/// </summary>
public enum KernelRunLifecycleState
{
    Unspecified = 0,
    Created = 1,
    IntentAccepted = 2,
    GraphSelected = 3,
    ProposalPending = 4,
    GraphValidated = 5,
    Executing = 6,
    Paused = 7,
    Recovering = 8,
    Completed = 9,
    Failed = 10,
    RolledBack = 11,
}

/// <summary>
/// Kernel projection 基类，供 Host Gateway 输出只读视图。
/// Base Kernel projection used by Host Gateway to expose read-only views.
/// </summary>
public abstract record KernelProjection
{
    protected KernelProjection(string projectionId, KernelRunId runId, VersionStamp version, DateTimeOffset generatedAt = default)
    {
        ProjectionId = KernelContractGuard.RequiredText(projectionId, nameof(projectionId));
        RunId = runId;
        Version = version;
        GeneratedAt = generatedAt == default ? DateTimeOffset.UtcNow : generatedAt;
    }

    public string ProjectionId { get; }

    public KernelRunId RunId { get; }

    public VersionStamp Version { get; }

    public DateTimeOffset GeneratedAt { get; }
}

/// <summary>
/// Kernel 运行状态投影。
/// Kernel run-state projection.
/// </summary>
public sealed record KernelRunStateProjection : KernelProjection
{
    public KernelRunStateProjection(
        string projectionId,
        KernelRunId runId,
        VersionStamp version,
        KernelRunLifecycleState state,
        string? rejectionReason = null,
        StageId? currentStageId = null,
        DateTimeOffset generatedAt = default)
        : base(projectionId, runId, version, generatedAt)
    {
        State = state;
        RejectionReason = rejectionReason;
        CurrentStageId = currentStageId;
    }

    public KernelRunLifecycleState State { get; }

    public string? RejectionReason { get; }

    public StageId? CurrentStageId { get; }
}

/// <summary>
/// Kernel Graph 投影。
/// Kernel graph projection.
/// </summary>
public sealed record KernelGraphProjection : KernelProjection
{
    public KernelGraphProjection(
        string projectionId,
        KernelRunId runId,
        VersionStamp version,
        StageGraphId graphId,
        IReadOnlyList<StageId>? stageIds = null,
        DateTimeOffset generatedAt = default)
        : base(projectionId, runId, version, generatedAt)
    {
        GraphId = graphId;
        StageIds = KernelContractGuard.ListOrEmpty(stageIds);
    }

    public StageGraphId GraphId { get; }

    public IReadOnlyList<StageId> StageIds { get; }
}

/// <summary>
/// Kernel strategy 投影。
/// Kernel strategy projection.
/// </summary>
public sealed record KernelStrategyProjection : KernelProjection
{
    public KernelStrategyProjection(
        string projectionId,
        KernelRunId runId,
        VersionStamp version,
        StrategyId strategyId,
        StrategyLifecycleState lifecycleState,
        DateTimeOffset generatedAt = default)
        : base(projectionId, runId, version, generatedAt)
    {
        StrategyId = strategyId;
        LifecycleState = lifecycleState;
    }

    public StrategyId StrategyId { get; }

    public StrategyLifecycleState LifecycleState { get; }
}
