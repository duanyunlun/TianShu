using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel;

/// <summary>
/// Stage 边跳转种类。
/// Stage-edge transition kind.
/// </summary>
public enum StageTransitionKind
{
    Unspecified = 0,
    Success = 1,
    Failure = 2,
    Conditional = 3,
    Recovery = 4,
    Abort = 5,
}

/// <summary>
/// StageGraph 元数据。
/// StageGraph metadata.
/// </summary>
public sealed record StageGraphMetadata
{
    public StageGraphMetadata(
        string owner,
        string source,
        DateTimeOffset createdAt = default,
        StrategyId? strategyId = null,
        MetadataBag? metadata = null)
    {
        Owner = KernelContractGuard.RequiredText(owner, nameof(owner));
        Source = KernelContractGuard.RequiredText(source, nameof(source));
        CreatedAt = createdAt == default ? DateTimeOffset.UtcNow : createdAt;
        StrategyId = strategyId;
        Metadata = KernelContractGuard.MetadataOrEmpty(metadata);
    }

    public string Owner { get; }

    public string Source { get; }

    public DateTimeOffset CreatedAt { get; }

    public StrategyId? StrategyId { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Stage 成功条件。
/// Stage success criteria.
/// </summary>
public sealed record SuccessCriteria
{
    public SuccessCriteria(IReadOnlyList<string>? requiredSignals = null, string? evaluatorRef = null)
    {
        RequiredSignals = KernelContractGuard.ListOrEmpty(requiredSignals);
        EvaluatorRef = evaluatorRef;
    }

    public IReadOnlyList<string> RequiredSignals { get; }

    public string? EvaluatorRef { get; }
}

/// <summary>
/// 失败处理引用。
/// Failure-handler reference.
/// </summary>
public sealed record FailureHandlerRef
{
    public FailureHandlerRef(string handlerRef, bool mayRecover = false)
    {
        HandlerRef = KernelContractGuard.RequiredText(handlerRef, nameof(handlerRef));
        MayRecover = mayRecover;
    }

    public string HandlerRef { get; }

    public bool MayRecover { get; }
}

/// <summary>
/// Stage 跳转条件。
/// Stage transition condition.
/// </summary>
public sealed record TransitionCondition
{
    public TransitionCondition(string conditionKind, StructuredValue? expression = null)
    {
        ConditionKind = KernelContractGuard.RequiredText(conditionKind, nameof(conditionKind));
        Expression = expression ?? StructuredValue.Null;
    }

    public string ConditionKind { get; }

    public StructuredValue Expression { get; }
}

/// <summary>
/// Stage 跳转守卫。
/// Stage transition guard.
/// </summary>
public sealed record TransitionGuard
{
    public TransitionGuard(IReadOnlyList<string>? requiredSignals = null, PermissionEnvelope? permission = null)
    {
        RequiredSignals = KernelContractGuard.ListOrEmpty(requiredSignals);
        Permission = permission ?? new PermissionEnvelope();
    }

    public IReadOnlyList<string> RequiredSignals { get; }

    public PermissionEnvelope Permission { get; }
}

/// <summary>
/// Stage 节点，是 StageGraph 的可验证执行单元。
/// Stage node, the verifiable execution unit inside a StageGraph.
/// </summary>
public sealed record StageNode
{
    public StageNode(
        StageId stageId,
        string kind,
        string objective,
        ContractRef inputContract,
        ContractRef outputContract,
        IReadOnlyList<string>? allowedKernelToolIds,
        IReadOnlyList<string>? allowedCapabilityToolIds,
        ModelRoutePolicy modelRoutePolicy,
        ContextPolicy contextPolicy,
        SideEffectLevel sideEffectLevel,
        KernelBudget budget,
        SuccessCriteria successCriteria,
        FailureHandlerRef failureHandler)
    {
        StageId = stageId;
        Kind = KernelContractGuard.RequiredText(kind, nameof(kind));
        Objective = KernelContractGuard.RequiredText(objective, nameof(objective));
        InputContract = KernelContractGuard.NotNull(inputContract, nameof(inputContract));
        OutputContract = KernelContractGuard.NotNull(outputContract, nameof(outputContract));
        AllowedKernelToolIds = KernelContractGuard.ListOrEmpty(allowedKernelToolIds);
        AllowedCapabilityToolIds = KernelContractGuard.ListOrEmpty(allowedCapabilityToolIds);
        ModelRoutePolicy = KernelContractGuard.NotNull(modelRoutePolicy, nameof(modelRoutePolicy));
        ContextPolicy = KernelContractGuard.NotNull(contextPolicy, nameof(contextPolicy));
        SideEffectLevel = sideEffectLevel;
        Budget = KernelContractGuard.NotNull(budget, nameof(budget));
        SuccessCriteria = KernelContractGuard.NotNull(successCriteria, nameof(successCriteria));
        FailureHandler = KernelContractGuard.NotNull(failureHandler, nameof(failureHandler));
    }

    public StageId StageId { get; }

    public string Kind { get; }

    public string Objective { get; }

    public ContractRef InputContract { get; }

    public ContractRef OutputContract { get; }

    public IReadOnlyList<string> AllowedKernelToolIds { get; }

    public IReadOnlyList<string> AllowedCapabilityToolIds { get; }

    public ModelRoutePolicy ModelRoutePolicy { get; }

    public ContextPolicy ContextPolicy { get; }

    public SideEffectLevel SideEffectLevel { get; }

    public KernelBudget Budget { get; }

    public SuccessCriteria SuccessCriteria { get; }

    public FailureHandlerRef FailureHandler { get; }
}

/// <summary>
/// Stage 有向边。
/// Directed Stage edge.
/// </summary>
public sealed record StageEdge
{
    public StageEdge(
        StageEdgeId edgeId,
        StageId fromStageId,
        StageId toStageId,
        TransitionCondition condition,
        TransitionGuard guard,
        StageTransitionKind transitionKind)
    {
        EdgeId = edgeId;
        FromStageId = fromStageId;
        ToStageId = toStageId;
        Condition = KernelContractGuard.NotNull(condition, nameof(condition));
        Guard = KernelContractGuard.NotNull(guard, nameof(guard));
        TransitionKind = transitionKind;
    }

    public StageEdgeId EdgeId { get; }

    public StageId FromStageId { get; }

    public StageId ToStageId { get; }

    public TransitionCondition Condition { get; }

    public TransitionGuard Guard { get; }

    public StageTransitionKind TransitionKind { get; }
}

/// <summary>
/// Kernel 可解释、可验证、可回放的编排中间表示。
/// Kernel-interpretable, verifiable, replayable orchestration intermediate representation.
/// </summary>
public sealed record StageGraph
{
    public StageGraph(
        StageGraphId graphId,
        string version,
        CoreIntentKind intentKind,
        StageId entryStageId,
        IReadOnlyList<StageNode> stages,
        IReadOnlyList<StageEdge>? edges,
        GraphPolicySet policies,
        KernelBudget budgets,
        CheckpointRules checkpointRules,
        RecoveryRules recoveryRules,
        EvaluationRules evaluationRules,
        StageGraphMetadata metadata)
    {
        GraphId = graphId;
        Version = KernelContractGuard.RequiredText(version, nameof(version));
        IntentKind = intentKind;
        EntryStageId = entryStageId;
        Stages = KernelContractGuard.ListOrEmpty(stages);
        Edges = KernelContractGuard.ListOrEmpty(edges);
        Policies = KernelContractGuard.NotNull(policies, nameof(policies));
        Budgets = KernelContractGuard.NotNull(budgets, nameof(budgets));
        CheckpointRules = KernelContractGuard.NotNull(checkpointRules, nameof(checkpointRules));
        RecoveryRules = KernelContractGuard.NotNull(recoveryRules, nameof(recoveryRules));
        EvaluationRules = KernelContractGuard.NotNull(evaluationRules, nameof(evaluationRules));
        Metadata = KernelContractGuard.NotNull(metadata, nameof(metadata));
    }

    public StageGraphId GraphId { get; }

    public string Version { get; }

    public CoreIntentKind IntentKind { get; }

    public StageId EntryStageId { get; }

    public IReadOnlyList<StageNode> Stages { get; }

    public IReadOnlyList<StageEdge> Edges { get; }

    public GraphPolicySet Policies { get; }

    public KernelBudget Budgets { get; }

    public CheckpointRules CheckpointRules { get; }

    public RecoveryRules RecoveryRules { get; }

    public EvaluationRules EvaluationRules { get; }

    public StageGraphMetadata Metadata { get; }
}
