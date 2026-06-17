using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.Adaptive.Tools;

internal static class AdaptiveStageGraphFactory
{
    public static StageGraph Create(KernelToolInvocation invocation)
    {
        var intentKind = invocation.Intent?.IntentKind ?? CoreIntentKind.Turn;
        var variantId = ReadString(invocation.Input, "variantId", "direct");
        var graphId = ReadString(
            invocation.Input,
            "graphId",
            invocation.Options.PreferredGraphId?.Value ?? "graph.adaptive.direct");
        var objective = ReadString(invocation.Input, "objective", "Adaptive proposal generated a bounded core-loop stage.");
        var routeId = ReadString(invocation.Input, "routeId", "route.default");
        var capabilityToolId = ReadString(invocation.Input, "capabilityToolId", "module.core_loop");
        var evaluationMetricId = ReadString(invocation.Input, "evaluationMetricId", "proposal_validity");
        var maxInputTokens = ReadInt32(invocation.Input, "maxInputTokens", invocation.Options.BudgetOverride?.TokenBudget ?? 2_048);
        var stageId = new StageId($"stage.adaptive.{variantId}.reason");
        var budget = invocation.Options.BudgetOverride ?? new KernelBudget(tokenBudget: 2_048, timeBudgetMs: 15_000, retryBudget: 1, toolCallBudget: 1);
        var stage = new StageNode(
            stageId,
            $"adaptive_{variantId}_loop",
            objective,
            new ContractRef("kernel.intent", "1"),
            new ContractRef("kernel.execution_request", "1"),
            KernelToolsFor(variantId),
            new[] { capabilityToolId },
            new ModelRoutePolicy(new[] { routeId }, routeId),
            new ContextPolicy(maxInputTokens: maxInputTokens, priorityRefs: PriorityRefsFor(variantId)),
            SideEffectLevel.ReadOnly,
            budget,
            new SuccessCriteria(new[] { $"{variantId}.candidate_materialized" }, $"evaluation.{variantId}"),
            new FailureHandlerRef("kernel.fail_closed", mayRecover: true));
        var stages = new List<StageNode> { stage };
        var edges = new List<StageEdge>();
        if (StringComparer.Ordinal.Equals(variantId, "recovery_checked"))
        {
            var recoveryStageId = new StageId("stage.adaptive.recovery_checked.recovery");
            stages.Add(new StageNode(
                recoveryStageId,
                "adaptive_recovery_plan",
                "Prepare a bounded recovery proposal before any strategy promotion.",
                new ContractRef("kernel.error_signal", "1"),
                new ContractRef("kernel.recovery_proposal", "1"),
                new[] { "kernel.propose_recovery_plan" },
                Array.Empty<string>(),
                new ModelRoutePolicy(new[] { routeId }, routeId),
                new ContextPolicy(maxInputTokens: Math.Max(512, maxInputTokens / 2), priorityRefs: new[] { "error_signal", "trace_summary" }),
                SideEffectLevel.ReadOnly,
                new KernelBudget(tokenBudget: Math.Max(512, budget.TokenBudget / 2), timeBudgetMs: Math.Max(1_000, budget.TimeBudgetMs / 2), retryBudget: 0, toolCallBudget: 0),
                new SuccessCriteria(new[] { "recovery_plan_proposed" }, "evaluation.recovery_readiness"),
                new FailureHandlerRef("kernel.fail_closed", mayRecover: false)));
            edges.Add(new StageEdge(
                new StageEdgeId("edge.adaptive.recovery_checked.reason_to_recovery"),
                stageId,
                recoveryStageId,
                new TransitionCondition("on_failure"),
                new TransitionGuard(requiredSignals: new[] { "error_signal" }),
                StageTransitionKind.Recovery));
        }

        return new StageGraph(
            new StageGraphId(graphId),
            "1",
            intentKind,
            stageId,
            stages,
            edges,
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedKernelToolIds: KernelToolsFor(variantId),
                allowedCapabilityToolIds: new[] { capabilityToolId },
                maxSideEffectLevel: SideEffectLevel.ReadOnly,
                requiresHumanGate: invocation.Options.RequireHumanGate),
            budget,
            new CheckpointRules(enabled: true, new[] { stageId }),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1, new[] { "fail_closed" }),
            new EvaluationRules(enabled: true, new[] { evaluationMetricId }),
            new StageGraphMetadata("kernel.adaptive", $"candidate.{variantId}"));
    }

    private static IReadOnlyList<string> KernelToolsFor(string variantId)
        => StringComparer.Ordinal.Equals(variantId, "context_guarded")
            ? new[] { "kernel.update_context_policy", "kernel.request_capability_call" }
            : StringComparer.Ordinal.Equals(variantId, "recovery_checked")
                ? new[] { "kernel.propose_recovery_plan", "kernel.request_capability_call" }
                : new[] { "kernel.request_capability_call" };

    private static IReadOnlyList<string> PriorityRefsFor(string variantId)
        => StringComparer.Ordinal.Equals(variantId, "context_guarded")
            ? new[] { "current_user_input", "latest_user_correction", "tool_evidence" }
            : StringComparer.Ordinal.Equals(variantId, "recovery_checked")
                ? new[] { "current_user_input", "trace_summary", "error_signal" }
                : new[] { "current_user_input" };

    private static string ReadString(StructuredValue input, string propertyName, string fallback)
        => input.Kind == StructuredValueKind.Object
           && input.TryGetProperty(propertyName, out var value)
           && value is not null
           && value.Kind != StructuredValueKind.Null
            ? value.GetString() ?? fallback
            : fallback;

    private static int ReadInt32(StructuredValue input, string propertyName, int fallback)
        => input.Kind == StructuredValueKind.Object
           && input.TryGetProperty(propertyName, out var value)
           && value is not null
           && value.Kind != StructuredValueKind.Null
            ? value.GetInt32()
            : fallback;
}
