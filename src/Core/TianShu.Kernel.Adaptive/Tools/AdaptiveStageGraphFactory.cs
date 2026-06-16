using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Adaptive.Tools;

internal static class AdaptiveStageGraphFactory
{
    public static StageGraph Create(KernelToolInvocation invocation)
    {
        var intentKind = invocation.Intent?.IntentKind ?? CoreIntentKind.Turn;
        var stageId = new StageId("stage.adaptive.proposed");
        var budget = invocation.Options.BudgetOverride ?? new KernelBudget(tokenBudget: 2_048, timeBudgetMs: 15_000, retryBudget: 1, toolCallBudget: 1);
        var stage = new StageNode(
            stageId,
            "adaptive_core_loop",
            "Adaptive proposal generated a bounded core-loop stage.",
            new ContractRef("kernel.intent", "1"),
            new ContractRef("kernel.execution_request", "1"),
            new[] { "kernel.request_capability_call" },
            new[] { "module.core_loop" },
            new ModelRoutePolicy(new[] { "route.default" }, "route.default"),
            new ContextPolicy(maxInputTokens: budget.TokenBudget),
            SideEffectLevel.ReadOnly,
            budget,
            new SuccessCriteria(new[] { "execution_plan_created" }),
            new FailureHandlerRef("kernel.fail_closed", mayRecover: true));

        return new StageGraph(
            invocation.Options.PreferredGraphId ?? new StageGraphId("graph.adaptive.proposed"),
            "1",
            intentKind,
            stageId,
            new[] { stage },
            Array.Empty<StageEdge>(),
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedKernelToolIds: new[] { "kernel.request_capability_call" },
                allowedCapabilityToolIds: new[] { "module.core_loop" },
                maxSideEffectLevel: SideEffectLevel.ReadOnly,
                requiresHumanGate: invocation.Options.RequireHumanGate),
            budget,
            new CheckpointRules(enabled: true, new[] { stageId }),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1, new[] { "fail_closed" }),
            new EvaluationRules(enabled: true, new[] { "proposal_validity" }),
            new StageGraphMetadata("kernel.adaptive", "default"));
    }
}
