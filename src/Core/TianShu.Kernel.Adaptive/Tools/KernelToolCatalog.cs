using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.Adaptive.Tools;

/// <summary>
/// 默认 KernelTool 目录。
/// Default KernelTool catalog.
/// </summary>
public static class KernelToolCatalog
{
    public static IReadOnlyList<IKernelTool> CreateDefaultTools()
        =>
        [
            new ProposeStageKernelTool(),
            new ComposeStageGraphKernelTool(),
            new ReviseStageGraphKernelTool(),
            new SelectModelRouteKernelTool(),
            new SelectToolStrategyKernelTool(),
            new RequestCapabilityCallKernelTool(),
            new UpdateContextPolicyKernelTool(),
            new ProposeCheckpointKernelTool(),
            new ProposeRecoveryPlanKernelTool(),
            new EvaluateRunKernelTool(),
            new PromoteStrategyKernelTool(),
            new RollbackStrategyKernelTool(),
            new ProposeKernelPolicyChangeKernelTool(),
        ];
}

public sealed class ProposeStageKernelTool : KernelToolBase
{
    public ProposeStageKernelTool() : base(KernelToolNames.ProposeStage, "Propose a StageGraph patch that adds a stage.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var operation = new StageGraphPatchOperation(
            "add_stage",
            new StageId(ReadString(invocation.Input, "stageId", "stage.proposed")),
            StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["objective"] = ReadString(invocation.Input, "objective", "proposed stage"),
            }));

        return Task.FromResult(new KernelToolResult(new StageGraphPatchProposal(
            ProposalId(ToolName),
            invocation.SourceIntentId,
            invocation.State.SelectedGraphId ?? new StageGraphId("graph.proposed"),
            new[] { operation },
            LowRisk(),
            BudgetImpact("stage proposal"),
            Rollback(ToolName),
            Evaluation(ToolName))));
    }
}

public sealed class ComposeStageGraphKernelTool : KernelToolBase
{
    public ComposeStageGraphKernelTool() : base(KernelToolNames.ComposeStageGraph, "Compose a candidate StageGraph.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(new KernelToolResult(new StageGraphProposal(
            ProposalId(ToolName),
            invocation.SourceIntentId,
            AdaptiveStageGraphFactory.Create(invocation),
            LowRisk(),
            BudgetImpact("compose stage graph"),
            Rollback(ToolName),
            Evaluation(ToolName))));
}

public sealed class ReviseStageGraphKernelTool : KernelToolBase
{
    public ReviseStageGraphKernelTool() : base(KernelToolNames.ReviseStageGraph, "Revise an existing StageGraph by patch.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(new KernelToolResult(new StageGraphPatchProposal(
            ProposalId(ToolName),
            invocation.SourceIntentId,
            invocation.State.SelectedGraphId ?? new StageGraphId("graph.proposed"),
            new[] { new StageGraphPatchOperation("revise_graph", payload: StructuredValue.FromString("validation feedback")) },
            LowRisk(),
            BudgetImpact("revise stage graph"),
            Rollback(ToolName),
            Evaluation(ToolName))));
}

public sealed class SelectModelRouteKernelTool : KernelToolBase
{
    public SelectModelRouteKernelTool() : base(KernelToolNames.SelectModelRoute, "Propose model route constraints.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => this.Patch(invocation, "select_model_route", new ModelRoutePolicy(new[] { ReadString(invocation.Input, "routeId", "route.default") }, ReadString(invocation.Input, "routeId", "route.default")));
}

public sealed class SelectToolStrategyKernelTool : KernelToolBase
{
    public SelectToolStrategyKernelTool() : base(KernelToolNames.SelectToolStrategy, "Propose tool strategy constraints.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => this.Patch(invocation, "select_tool_strategy", StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["toolId"] = ReadString(invocation.Input, "toolId", "module.core_loop"),
        }));
}

public sealed class RequestCapabilityCallKernelTool : KernelToolBase
{
    public RequestCapabilityCallKernelTool() : base(KernelToolNames.RequestCapabilityCall, "Request a capability call operation.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(new KernelToolResult(operation: new RequestCapabilityCallOperation(
            OperationId(ToolName),
            invocation.SourceIntentId,
            new StageId(ReadString(invocation.Input, "stageId", invocation.State.CurrentStageId?.Value ?? "stage.proposed")),
            ReadString(invocation.Input, "capabilityToolId", "module.core_loop"),
            invocation.Input,
            new PermissionEnvelope(new[] { "kernel.capability.request" }, requiresHumanGate: true),
            new SideEffectProfile(SideEffectLevel.ReadOnly))));
}

public sealed class UpdateContextPolicyKernelTool : KernelToolBase
{
    public UpdateContextPolicyKernelTool() : base(KernelToolNames.UpdateContextPolicy, "Propose context policy changes.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => this.Patch(invocation, "update_context_policy", new ContextPolicy(maxInputTokens: 4_096));
}

public sealed class ProposeCheckpointKernelTool : KernelToolBase
{
    public ProposeCheckpointKernelTool() : base(KernelToolNames.ProposeCheckpoint, "Request a checkpoint proposal operation.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(new KernelToolResult(operation: new CheckpointProposalOperation(
            OperationId(ToolName),
            invocation.SourceIntentId,
            new StageId(ReadString(invocation.Input, "stageId", invocation.State.CurrentStageId?.Value ?? "stage.proposed")),
            ReadString(invocation.Input, "checkpointRef", "checkpoint.proposed"),
            new PermissionEnvelope(new[] { "kernel.checkpoint.propose" }, requiresHumanGate: true),
            new SideEffectProfile(SideEffectLevel.None))));
}

public sealed class ProposeRecoveryPlanKernelTool : KernelToolBase
{
    public ProposeRecoveryPlanKernelTool() : base(KernelToolNames.ProposeRecoveryPlan, "Propose a recovery plan.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(new KernelToolResult(new RecoveryProposal(
            ProposalId(ToolName),
            invocation.SourceIntentId,
            new RecoveryPlan(ReadString(invocation.Input, "recoveryKind", "retry"), new[] { "retry_stage" }, requiresHumanGate: true),
            LowRisk(requiresHumanGate: true),
            BudgetImpact("recovery plan"),
            Rollback(ToolName),
            Evaluation(ToolName))));
}

public sealed class EvaluateRunKernelTool : KernelToolBase
{
    public EvaluateRunKernelTool() : base(KernelToolNames.EvaluateRun, "Request run evaluation.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(new KernelToolResult(operation: new EvaluationRequestOperation(
            OperationId(ToolName),
            invocation.SourceIntentId,
            new StageId(ReadString(invocation.Input, "stageId", invocation.State.CurrentStageId?.Value ?? "stage.proposed")),
            invocation.State.RunId,
            new EvaluationPlan("evaluation.run", new[] { "success" }),
            new PermissionEnvelope(new[] { "kernel.evaluation.request" }, requiresHumanGate: true),
            new SideEffectProfile(SideEffectLevel.None))));
}

public sealed class PromoteStrategyKernelTool : KernelToolBase
{
    public PromoteStrategyKernelTool() : base(KernelToolNames.PromoteStrategy, "Propose strategy promotion through trial or human gate.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(new KernelToolResult(new StrategyPromotionProposal(
            ProposalId(ToolName),
            invocation.SourceIntentId,
            new StrategyId(ReadString(invocation.Input, "strategyId", "strategy.proposed")),
            StrategyLifecycleState.Trial,
            LowRisk(requiresHumanGate: true),
            BudgetImpact("strategy promotion enters trial or human gate"),
            Rollback(ToolName),
            Evaluation(ToolName))));
}

public sealed class RollbackStrategyKernelTool : KernelToolBase
{
    public RollbackStrategyKernelTool() : base(KernelToolNames.RollbackStrategy, "Request strategy rollback.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(new KernelToolResult(operation: new StrategyRollbackOperation(
            OperationId(ToolName),
            invocation.SourceIntentId,
            new StageId(ReadString(invocation.Input, "stageId", invocation.State.CurrentStageId?.Value ?? "stage.proposed")),
            new StrategyId(ReadString(invocation.Input, "strategyId", "strategy.proposed")),
            ReadString(invocation.Input, "reason", "rollback requested"),
            new PermissionEnvelope(new[] { "kernel.strategy.rollback" }, requiresHumanGate: true),
            new SideEffectProfile(SideEffectLevel.None))));
}

public sealed class ProposeKernelPolicyChangeKernelTool : KernelToolBase
{
    public ProposeKernelPolicyChangeKernelTool() : base(KernelToolNames.ProposeKernelPolicyChange, "Propose a Kernel policy change that always requires human gate.") { }

    public override Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(new KernelToolResult(new PolicyChangeProposal(
            ProposalId(ToolName),
            invocation.SourceIntentId,
            new GraphPolicySet(PolicyEnforcementMode.HumanGate, requiresHumanGate: true),
            ReadString(invocation.Input, "reason", "policy change proposal"),
            LowRisk(requiresHumanGate: true),
            BudgetImpact("kernel policy change"),
            Rollback(ToolName),
            Evaluation(ToolName))));
}

internal static class KernelToolPatch
{
    public static Task<KernelToolResult> Patch(this KernelToolBase tool, KernelToolInvocation invocation, string operationKind, object payload)
        => Task.FromResult(new KernelToolResult(new StageGraphPatchProposal(
            KernelToolBase.ProposalId(tool.ToolName),
            invocation.SourceIntentId,
            invocation.State.SelectedGraphId ?? new StageGraphId("graph.proposed"),
            new[] { new StageGraphPatchOperation(operationKind, payload: StructuredValue.FromPlainObject(payload)) },
            KernelToolBase.LowRisk(),
            KernelToolBase.BudgetImpact(operationKind),
            KernelToolBase.Rollback(tool.ToolName),
            KernelToolBase.Evaluation(tool.ToolName))));
}
