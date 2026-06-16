using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel;

/// <summary>
/// Kernel operation 种类。
/// Kernel operation kind.
/// </summary>
public enum KernelOperationKind
{
    Unspecified = 0,
    RequestCapabilityCall = 1,
    CheckpointProposal = 2,
    EvaluationRequest = 3,
    StrategyRollback = 4,
}

/// <summary>
/// Kernel operation 基类，表示运行中请求 Stable Kernel Core 判定的操作。
/// Base Kernel operation representing an in-run request that Stable Kernel Core must judge.
/// </summary>
public abstract record KernelOperation
{
    protected KernelOperation(
        KernelOperationId operationId,
        CoreIntentId sourceIntentId,
        StageId sourceStageId,
        KernelOperationKind operationKind,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        MetadataBag? metadata = null)
    {
        OperationId = operationId;
        SourceIntentId = sourceIntentId;
        SourceStageId = sourceStageId;
        OperationKind = operationKind;
        Permission = KernelContractGuard.NotNull(permission, nameof(permission));
        SideEffect = KernelContractGuard.NotNull(sideEffect, nameof(sideEffect));
        Metadata = KernelContractGuard.MetadataOrEmpty(metadata);
    }

    public KernelOperationId OperationId { get; }

    public CoreIntentId SourceIntentId { get; }

    public StageId SourceStageId { get; }

    public KernelOperationKind OperationKind { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffect { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 请求将能力调用物化为 RuntimeStep 的 Kernel operation。
/// Kernel operation requesting a capability call to be materialized as a RuntimeStep.
/// </summary>
public sealed record RequestCapabilityCallOperation : KernelOperation
{
    public RequestCapabilityCallOperation(
        KernelOperationId operationId,
        CoreIntentId sourceIntentId,
        StageId sourceStageId,
        string capabilityToolId,
        StructuredValue inputEnvelope,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        MetadataBag? metadata = null)
        : base(operationId, sourceIntentId, sourceStageId, KernelOperationKind.RequestCapabilityCall, permission, sideEffect, metadata)
    {
        CapabilityToolId = KernelContractGuard.RequiredText(capabilityToolId, nameof(capabilityToolId));
        InputEnvelope = KernelContractGuard.NotNull(inputEnvelope, nameof(inputEnvelope));
    }

    public string CapabilityToolId { get; }

    public StructuredValue InputEnvelope { get; }
}

/// <summary>
/// Checkpoint proposal operation。
/// Checkpoint-proposal operation.
/// </summary>
public sealed record CheckpointProposalOperation : KernelOperation
{
    public CheckpointProposalOperation(
        KernelOperationId operationId,
        CoreIntentId sourceIntentId,
        StageId sourceStageId,
        string checkpointRef,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        MetadataBag? metadata = null)
        : base(operationId, sourceIntentId, sourceStageId, KernelOperationKind.CheckpointProposal, permission, sideEffect, metadata)
    {
        CheckpointRef = KernelContractGuard.RequiredText(checkpointRef, nameof(checkpointRef));
    }

    public string CheckpointRef { get; }
}

/// <summary>
/// 评估请求 operation。
/// Evaluation-request operation.
/// </summary>
public sealed record EvaluationRequestOperation : KernelOperation
{
    public EvaluationRequestOperation(
        KernelOperationId operationId,
        CoreIntentId sourceIntentId,
        StageId sourceStageId,
        KernelRunId targetRunId,
        EvaluationPlan evaluationPlan,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        MetadataBag? metadata = null)
        : base(operationId, sourceIntentId, sourceStageId, KernelOperationKind.EvaluationRequest, permission, sideEffect, metadata)
    {
        TargetRunId = targetRunId;
        EvaluationPlan = KernelContractGuard.NotNull(evaluationPlan, nameof(evaluationPlan));
    }

    public KernelRunId TargetRunId { get; }

    public EvaluationPlan EvaluationPlan { get; }
}

/// <summary>
/// 策略回滚 operation。
/// Strategy-rollback operation.
/// </summary>
public sealed record StrategyRollbackOperation : KernelOperation
{
    public StrategyRollbackOperation(
        KernelOperationId operationId,
        CoreIntentId sourceIntentId,
        StageId sourceStageId,
        StrategyId strategyId,
        string rollbackReason,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        MetadataBag? metadata = null)
        : base(operationId, sourceIntentId, sourceStageId, KernelOperationKind.StrategyRollback, permission, sideEffect, metadata)
    {
        StrategyId = strategyId;
        RollbackReason = KernelContractGuard.RequiredText(rollbackReason, nameof(rollbackReason));
    }

    public StrategyId StrategyId { get; }

    public string RollbackReason { get; }
}
