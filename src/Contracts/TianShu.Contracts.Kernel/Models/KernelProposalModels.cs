using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel;

/// <summary>
/// Kernel proposal 种类。
/// Kernel proposal kind.
/// </summary>
public enum KernelProposalKind
{
    Unspecified = 0,
    StageGraph = 1,
    StageGraphPatch = 2,
    Recovery = 3,
    StrategyPromotion = 4,
    PolicyChange = 5,
}

/// <summary>
/// StageGraph patch 操作。
/// StageGraph patch operation.
/// </summary>
public sealed record StageGraphPatchOperation
{
    public StageGraphPatchOperation(string operationKind, StageId? targetStageId = null, StructuredValue? payload = null)
    {
        OperationKind = KernelContractGuard.RequiredText(operationKind, nameof(operationKind));
        TargetStageId = targetStageId;
        Payload = payload ?? StructuredValue.Null;
    }

    public string OperationKind { get; }

    public StageId? TargetStageId { get; }

    public StructuredValue Payload { get; }
}

/// <summary>
/// 恢复计划。
/// Recovery plan.
/// </summary>
public sealed record RecoveryPlan
{
    public RecoveryPlan(string recoveryKind, IReadOnlyList<string>? actionRefs = null, bool requiresHumanGate = true)
    {
        RecoveryKind = KernelContractGuard.RequiredText(recoveryKind, nameof(recoveryKind));
        ActionRefs = KernelContractGuard.ListOrEmpty(actionRefs);
        RequiresHumanGate = requiresHumanGate;
    }

    public string RecoveryKind { get; }

    public IReadOnlyList<string> ActionRefs { get; }

    public bool RequiresHumanGate { get; }
}

/// <summary>
/// Kernel proposal 基类，表示 AI 或策略层提出但尚未生效的结构化建议。
/// Base Kernel proposal representing a structured suggestion that is not effective until approved.
/// </summary>
public abstract record KernelProposal
{
    protected KernelProposal(
        KernelProposalId proposalId,
        CoreIntentId sourceIntentId,
        KernelProposalKind proposalKind,
        RiskProfile riskProfile,
        KernelBudgetImpact budgetImpact,
        RollbackPlan rollbackPlan,
        EvaluationPlan evaluationPlan,
        MetadataBag? metadata = null)
    {
        ProposalId = proposalId;
        SourceIntentId = sourceIntentId;
        ProposalKind = proposalKind;
        RiskProfile = KernelContractGuard.NotNull(riskProfile, nameof(riskProfile));
        BudgetImpact = KernelContractGuard.NotNull(budgetImpact, nameof(budgetImpact));
        RollbackPlan = KernelContractGuard.NotNull(rollbackPlan, nameof(rollbackPlan));
        EvaluationPlan = KernelContractGuard.NotNull(evaluationPlan, nameof(evaluationPlan));
        Metadata = KernelContractGuard.MetadataOrEmpty(metadata);
    }

    public KernelProposalId ProposalId { get; }

    public CoreIntentId SourceIntentId { get; }

    public KernelProposalKind ProposalKind { get; }

    public RiskProfile RiskProfile { get; }

    public KernelBudgetImpact BudgetImpact { get; }

    public RollbackPlan RollbackPlan { get; }

    public EvaluationPlan EvaluationPlan { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// StageGraph proposal。
/// StageGraph proposal.
/// </summary>
public sealed record StageGraphProposal : KernelProposal
{
    public StageGraphProposal(
        KernelProposalId proposalId,
        CoreIntentId sourceIntentId,
        StageGraph graph,
        RiskProfile riskProfile,
        KernelBudgetImpact budgetImpact,
        RollbackPlan rollbackPlan,
        EvaluationPlan evaluationPlan,
        MetadataBag? metadata = null)
        : base(proposalId, sourceIntentId, KernelProposalKind.StageGraph, riskProfile, budgetImpact, rollbackPlan, evaluationPlan, metadata)
    {
        Graph = KernelContractGuard.NotNull(graph, nameof(graph));
    }

    public StageGraph Graph { get; }
}

/// <summary>
/// StageGraph patch proposal。
/// StageGraph patch proposal.
/// </summary>
public sealed record StageGraphPatchProposal : KernelProposal
{
    public StageGraphPatchProposal(
        KernelProposalId proposalId,
        CoreIntentId sourceIntentId,
        StageGraphId targetGraphId,
        IReadOnlyList<StageGraphPatchOperation> operations,
        RiskProfile riskProfile,
        KernelBudgetImpact budgetImpact,
        RollbackPlan rollbackPlan,
        EvaluationPlan evaluationPlan,
        MetadataBag? metadata = null)
        : base(proposalId, sourceIntentId, KernelProposalKind.StageGraphPatch, riskProfile, budgetImpact, rollbackPlan, evaluationPlan, metadata)
    {
        TargetGraphId = targetGraphId;
        Operations = KernelContractGuard.ListOrEmpty(operations);
    }

    public StageGraphId TargetGraphId { get; }

    public IReadOnlyList<StageGraphPatchOperation> Operations { get; }
}

/// <summary>
/// 恢复 proposal。
/// Recovery proposal.
/// </summary>
public sealed record RecoveryProposal : KernelProposal
{
    public RecoveryProposal(
        KernelProposalId proposalId,
        CoreIntentId sourceIntentId,
        RecoveryPlan recoveryPlan,
        RiskProfile riskProfile,
        KernelBudgetImpact budgetImpact,
        RollbackPlan rollbackPlan,
        EvaluationPlan evaluationPlan,
        MetadataBag? metadata = null)
        : base(proposalId, sourceIntentId, KernelProposalKind.Recovery, riskProfile, budgetImpact, rollbackPlan, evaluationPlan, metadata)
    {
        RecoveryPlan = KernelContractGuard.NotNull(recoveryPlan, nameof(recoveryPlan));
    }

    public RecoveryPlan RecoveryPlan { get; }
}

/// <summary>
/// 策略晋升 proposal。
/// Strategy-promotion proposal.
/// </summary>
public sealed record StrategyPromotionProposal : KernelProposal
{
    public StrategyPromotionProposal(
        KernelProposalId proposalId,
        CoreIntentId sourceIntentId,
        StrategyId strategyId,
        StrategyLifecycleState targetState,
        RiskProfile riskProfile,
        KernelBudgetImpact budgetImpact,
        RollbackPlan rollbackPlan,
        EvaluationPlan evaluationPlan,
        MetadataBag? metadata = null)
        : base(proposalId, sourceIntentId, KernelProposalKind.StrategyPromotion, riskProfile, budgetImpact, rollbackPlan, evaluationPlan, metadata)
    {
        StrategyId = strategyId;
        TargetState = targetState;
    }

    public StrategyId StrategyId { get; }

    public StrategyLifecycleState TargetState { get; }
}

/// <summary>
/// Kernel policy change proposal。
/// Kernel policy-change proposal.
/// </summary>
public sealed record PolicyChangeProposal : KernelProposal
{
    public PolicyChangeProposal(
        KernelProposalId proposalId,
        CoreIntentId sourceIntentId,
        GraphPolicySet proposedPolicySet,
        string changeReason,
        RiskProfile riskProfile,
        KernelBudgetImpact budgetImpact,
        RollbackPlan rollbackPlan,
        EvaluationPlan evaluationPlan,
        MetadataBag? metadata = null)
        : base(proposalId, sourceIntentId, KernelProposalKind.PolicyChange, riskProfile, budgetImpact, rollbackPlan, evaluationPlan, metadata)
    {
        ProposedPolicySet = KernelContractGuard.NotNull(proposedPolicySet, nameof(proposedPolicySet));
        ChangeReason = KernelContractGuard.RequiredText(changeReason, nameof(changeReason));
    }

    public GraphPolicySet ProposedPolicySet { get; }

    public string ChangeReason { get; }
}

/// <summary>
/// Adaptive Orchestration Layer 的 proposal 集合。
/// Proposal set returned by the Adaptive Orchestration Layer.
/// </summary>
public sealed record KernelProposalSet
{
    public KernelProposalSet(IReadOnlyList<KernelProposal>? proposals = null, string? rationaleRef = null)
    {
        Proposals = KernelContractGuard.ListOrEmpty(proposals);
        RationaleRef = rationaleRef;
    }

    public IReadOnlyList<KernelProposal> Proposals { get; }

    public string? RationaleRef { get; }
}
