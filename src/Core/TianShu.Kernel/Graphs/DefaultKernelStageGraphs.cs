using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Graphs;

/// <summary>
/// 内置默认 StageGraph 工厂，用于桥接当前固定 core loop 行为。
/// Built-in default StageGraph factory used to bridge the current fixed core-loop behavior.
/// </summary>
public static class DefaultKernelStageGraphs
{
    private const string DefaultCapabilityId = "module.core_loop";
    private const string RequestCapabilityCallToolId = "request_capability_call";
    private const string UpdateContextPolicyToolId = "update_context_policy";
    private static readonly string[] DefaultTurnToolIds =
    [
        "read_file",
        "list_dir",
        "grep",
        "glob",
        "apply_patch",
        "write",
        "memory_search",
        "artifacts",
        "spawn_agent",
    ];
    private static readonly string[] ReadOnlyTurnToolIds =
    [
        "read_file",
        "list_dir",
        "grep",
        "glob",
    ];

    private static readonly string[] DefaultTurnModuleIds =
    [
        "kernel.default",
        "provider.default",
    ];

    public static StageGraph CreateForIntent(CoreIntent intent, KernelRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        options ??= new KernelRunOptions();

        if (intent.IntentKind == CoreIntentKind.Turn)
        {
            return CreateTurnGraph(intent, options);
        }

        if (intent.IntentKind == CoreIntentKind.Interrupt)
        {
            return CreateHostInteractionGraph(
                intent,
                options,
                new StageId("stage.interrupt-host"),
                "interrupt-host",
                "Project a host interrupt into an auditable runtime boundary.",
                new ContractRef("contract.host.interrupt", "1"),
                new ContractRef("contract.host.interrupt-projection", "1"),
                "interrupt.projected",
                "graph.interrupt.default",
                SideEffectLevel.HostMutation);
        }

        if (intent.IntentKind == CoreIntentKind.Resume)
        {
            return CreateHostInteractionGraph(
                intent,
                options,
                new StageId("stage.resume-host"),
                "resume-host",
                "Project a host resume request from an approved checkpoint into an auditable runtime boundary.",
                new ContractRef("contract.host.resume", "1"),
                new ContractRef("contract.host.resume-projection", "1"),
                "resume.projected",
                "graph.resume.default",
                SideEffectLevel.ReadOnly);
        }

        var budget = options.BudgetOverride ?? NormalizeBudget(intent.Budget);
        var stageId = new StageId($"stage.default.{intent.IntentKind.ToString().ToLowerInvariant()}");
        var inputContract = new ContractRef("kernel.intent", "1");
        var outputContract = new ContractRef("kernel.execution_request", "1");
        var stage = new StageNode(
            stageId,
            "core_loop",
            $"Process {intent.IntentKind} intent through the default Kernel core loop.",
            inputContract,
            outputContract,
            new[] { "kernel.request_capability_call" },
            new[] { DefaultCapabilityId },
            new ModelRoutePolicy(new[] { "route.default" }, "route.default"),
            new ContextPolicy(maxInputTokens: budget.TokenBudget),
            SideEffectLevel.ReadOnly,
            budget,
            new SuccessCriteria(new[] { "execution_plan_created" }),
            new FailureHandlerRef("kernel.fail_closed", mayRecover: true));

        return new StageGraph(
            options.PreferredGraphId ?? new StageGraphId($"graph.default.{intent.IntentKind.ToString().ToLowerInvariant()}"),
            "1",
            intent.IntentKind,
            stageId,
            new[] { stage },
            Array.Empty<StageEdge>(),
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedKernelToolIds: new[] { "kernel.request_capability_call" },
                allowedCapabilityToolIds: new[] { DefaultCapabilityId },
                maxSideEffectLevel: SideEffectLevel.ReadOnly,
                requiresHumanGate: intent.Governance.RequiresHumanGate || options.RequireHumanGate),
            budget,
            new CheckpointRules(enabled: true, new[] { stageId }, "before_execution_runtime"),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1, new[] { "fail_closed" }),
            new EvaluationRules(enabled: true, new[] { "execution_plan_created" }),
            new StageGraphMetadata("kernel", "default-core-loop", metadata: MetadataBag.Empty));
    }

    private static StageGraph CreateHostInteractionGraph(
        CoreIntent intent,
        KernelRunOptions options,
        StageId stageId,
        string stageKind,
        string objective,
        ContractRef inputContract,
        ContractRef outputContract,
        string successSignal,
        string defaultGraphId,
        SideEffectLevel sideEffectLevel)
    {
        var budget = NormalizeBudget(options.BudgetOverride ?? intent.Budget);
        var stage = new StageNode(
            stageId,
            stageKind,
            objective,
            inputContract,
            outputContract,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new ModelRoutePolicy(failClosedWhenMissingCandidate: false),
            new ContextPolicy(maxInputTokens: Math.Max(1, budget.TokenBudget), failClosed: false),
            sideEffectLevel,
            new KernelBudget(
                tokenBudget: Math.Max(1, budget.TokenBudget / 16),
                timeBudgetMs: Math.Max(1, budget.TimeBudgetMs / 10),
                retryBudget: 1,
                toolCallBudget: 0),
            new SuccessCriteria([successSignal]),
            new FailureHandlerRef("handler.host-interaction-fail-closed"));

        return new StageGraph(
            options.PreferredGraphId ?? new StageGraphId(defaultGraphId),
            "1",
            intent.IntentKind,
            stageId,
            [stage],
            Array.Empty<StageEdge>(),
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                maxSideEffectLevel: sideEffectLevel,
                requiresHumanGate: intent.Governance.RequiresHumanGate || options.RequireHumanGate),
            budget,
            new CheckpointRules(enabled: true, requiredStageIds: [stageId], materializationPolicy: "before_host_interaction"),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1, allowedRecoveryKinds: ["fail_closed"]),
            new EvaluationRules(enabled: true, metricIds: [successSignal]),
            new StageGraphMetadata("builtin", "fixed-host-interaction", metadata: MetadataBag.Empty));
    }

    private static StageGraph CreateTurnGraph(CoreIntent intent, KernelRunOptions options)
    {
        var budget = NormalizeTurnBudget(options.BudgetOverride ?? intent.Budget);
        var route = CreateDefaultModelRoutePolicy();
        var nonModelRoute = new ModelRoutePolicy(failClosedWhenMissingCandidate: false);
        var allowedTurnToolIds = DefaultTurnToolIds
            .Where(toolId => intent.Governance.AllowedToolIds.Contains(toolId, StringComparer.Ordinal))
            .Where(toolId => !string.Equals(toolId, "spawn_agent", StringComparison.Ordinal)
                             || intent.Governance.AllowedModuleIds.Contains("module.sub_agent", StringComparer.Ordinal))
            .ToArray();
        var allowedTurnModuleIds = intent.Governance.AllowedModuleIds.Contains("module.sub_agent", StringComparer.Ordinal)
            ? DefaultTurnModuleIds.Concat(["module.sub_agent"]).ToArray()
            : DefaultTurnModuleIds;
        var toolStageSideEffect = allowedTurnToolIds.Length > 0
                                  && allowedTurnToolIds.Contains("spawn_agent", StringComparer.Ordinal)
            ? SideEffectLevel.HostMutation
            : allowedTurnToolIds.Length > 0
              && allowedTurnToolIds.All(static toolId => ReadOnlyTurnToolIds.Contains(toolId, StringComparer.Ordinal))
            ? SideEffectLevel.ReadOnly
            : SideEffectLevel.WorkspaceWrite;
        var graphMaxSideEffectLevel = allowedTurnToolIds.Contains("spawn_agent", StringComparer.Ordinal)
            ? SideEffectLevel.HostMutation
            : SideEffectLevel.ExternalNetwork;
        var context = new ContextPolicy(
            maxInputTokens: Math.Max(1, budget.TokenBudget),
            allowedSourceKinds:
            [
                ContextSourceKind.CurrentUserInput.ToString(),
                ContextSourceKind.ConversationHistory.ToString(),
                ContextSourceKind.WorkspaceFact.ToString(),
                ContextSourceKind.ToolEvidence.ToString(),
            ]);

        var prepare = new StageNode(
            new StageId("stage.prepare-context"),
            "prepare-context",
            "Collect and trim the provider-neutral context for the current turn.",
            new ContractRef("contract.turn.user-input", "1"),
            new ContractRef("contract.context.prepared", "1"),
            [UpdateContextPolicyToolId],
            Array.Empty<string>(),
            nonModelRoute,
            context,
            SideEffectLevel.ReadOnly,
            new KernelBudget(tokenBudget: Math.Max(1, budget.TokenBudget / 8), timeBudgetMs: Math.Max(1, budget.TimeBudgetMs / 10), retryBudget: 1),
            new SuccessCriteria(["context.prepared"]),
            new FailureHandlerRef("handler.finalize-abort"));

        var model = new StageNode(
            new StageId("stage.model-reason"),
            "model-reason",
            "Invoke the approved model route to produce an assistant response or tool requests.",
            new ContractRef("contract.context.prepared", "1"),
            new ContractRef("contract.model.turn-output", "1"),
            [RequestCapabilityCallToolId],
            Array.Empty<string>(),
            route,
            context,
            SideEffectLevel.ExternalNetwork,
            new KernelBudget(tokenBudget: Math.Max(1, budget.TokenBudget), timeBudgetMs: Math.Max(1, Math.Min(budget.TimeBudgetMs, 30_000)), retryBudget: 5, toolCallBudget: budget.ToolCallBudget),
            new SuccessCriteria(["model.responded"]),
            new FailureHandlerRef("handler.model-retry", mayRecover: true));

        var tool = new StageNode(
            new StageId("stage.tool-exec"),
            "tool-exec",
            "Execute approved capability tool requests and materialize tool evidence.",
            new ContractRef("contract.tool.approved-requests", "1"),
            new ContractRef("contract.tool.results", "1"),
            Array.Empty<string>(),
            allowedTurnToolIds,
            nonModelRoute,
            context,
            toolStageSideEffect,
            new KernelBudget(tokenBudget: Math.Max(1, budget.TokenBudget / 4), timeBudgetMs: Math.Max(1, budget.TimeBudgetMs / 3), retryBudget: 1, toolCallBudget: Math.Max(1, budget.ToolCallBudget)),
            new SuccessCriteria(["tool.results.materialized"]),
            new FailureHandlerRef("handler.tool-failure"));

        var finalize = new StageNode(
            new StageId("stage.finalize"),
            "finalize",
            "Commit turn state and emit diagnostics for the host projection.",
            new ContractRef("contract.model.turn-output", "1"),
            new ContractRef("contract.turn.final-projection", "1"),
            Array.Empty<string>(),
            Array.Empty<string>(),
            nonModelRoute,
            new ContextPolicy(maxInputTokens: Math.Max(1, budget.TokenBudget), failClosed: false),
            SideEffectLevel.WorkspaceWrite,
            new KernelBudget(tokenBudget: Math.Max(1, budget.TokenBudget / 16), timeBudgetMs: Math.Max(1, budget.TimeBudgetMs / 10), retryBudget: 1),
            new SuccessCriteria(["turn.finalized"]),
            new FailureHandlerRef("handler.hard-fail"));

        var stages = new[] { prepare, model, tool, finalize };
        var edges = new[]
        {
            new StageEdge(new StageEdgeId("edge.prepare-to-reason"), prepare.StageId, model.StageId, new TransitionCondition("context.prepared"), new TransitionGuard(requiredSignals: ["context.prepared"]), StageTransitionKind.Success),
            new StageEdge(new StageEdgeId("edge.prepare-fail"), prepare.StageId, finalize.StageId, new TransitionCondition("context.failed"), new TransitionGuard(), StageTransitionKind.Failure),
            new StageEdge(new StageEdgeId("edge.reason-to-tool"), model.StageId, tool.StageId, new TransitionCondition("tool.requests.available"), new TransitionGuard(requiredSignals: ["model.tool_requests"], permission: new PermissionEnvelope(allowedTurnToolIds, requiresHumanGate: options.RequireHumanGate)), StageTransitionKind.Conditional),
            new StageEdge(new StageEdgeId("edge.tool-to-reason"), tool.StageId, model.StageId, new TransitionCondition("tool.results.materialized"), new TransitionGuard(requiredSignals: ["tool.results.materialized"]), StageTransitionKind.Success),
            new StageEdge(new StageEdgeId("edge.reason-to-final"), model.StageId, finalize.StageId, new TransitionCondition("model.final_response"), new TransitionGuard(requiredSignals: ["model.responded"]), StageTransitionKind.Success),
            new StageEdge(new StageEdgeId("edge.reason-budget-exhausted"), model.StageId, finalize.StageId, new TransitionCondition("budget.exhausted"), new TransitionGuard(), StageTransitionKind.Failure),
            new StageEdge(new StageEdgeId("edge.tool-fail"), tool.StageId, finalize.StageId, new TransitionCondition("tool.failed"), new TransitionGuard(), StageTransitionKind.Failure),
        };

        return new StageGraph(
            options.PreferredGraphId ?? new StageGraphId("graph.turn.default"),
            "1",
            CoreIntentKind.Turn,
            prepare.StageId,
            stages,
            edges,
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedKernelToolIds: [UpdateContextPolicyToolId, RequestCapabilityCallToolId],
                allowedCapabilityToolIds: allowedTurnToolIds,
                allowedModuleIds: allowedTurnModuleIds,
                maxSideEffectLevel: graphMaxSideEffectLevel,
                requiresHumanGate: intent.Governance.RequiresHumanGate || options.RequireHumanGate),
            budget,
            new CheckpointRules(enabled: true, requiredStageIds: [prepare.StageId, model.StageId, tool.StageId], materializationPolicy: "before_stage_transition"),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: Math.Max(1, budget.RetryBudget), allowedRecoveryKinds: ["fail_closed"]),
            new EvaluationRules(enabled: true, metricIds: ["turn.finalized", "runtime.step.completed"]),
            new StageGraphMetadata("builtin", "fixed", metadata: MetadataBag.Empty));
    }

    private static KernelBudget NormalizeBudget(KernelBudget budget)
        => budget.TokenBudget > 0 || budget.TimeBudgetMs > 0 || budget.ToolCallBudget > 0
            ? budget
            : new KernelBudget(tokenBudget: 4_096, timeBudgetMs: 30_000, retryBudget: 1, toolCallBudget: 1);

    private static KernelBudget NormalizeTurnBudget(KernelBudget budget)
        => budget.TokenBudget > 0 || budget.TimeBudgetMs > 0 || budget.ToolCallBudget > 0
            ? new KernelBudget(
                tokenBudget: budget.TokenBudget <= 0 ? 4_096 : budget.TokenBudget,
                timeBudgetMs: budget.TimeBudgetMs <= 0 ? 30_000 : budget.TimeBudgetMs,
                costBudget: budget.CostBudget,
                retryBudget: budget.RetryBudget <= 0 ? 5 : budget.RetryBudget,
                toolCallBudget: budget.ToolCallBudget <= 0 ? 25 : budget.ToolCallBudget)
            : new KernelBudget(tokenBudget: 4_096, timeBudgetMs: 30_000, retryBudget: 5, toolCallBudget: 25);

    private static ModelRoutePolicy CreateDefaultModelRoutePolicy()
        => new(
            routeCandidateIds: ["route.default"],
            preferredRouteId: "route.default",
            candidates:
            [
                new ModelRouteCandidateBinding(
                    "route.default",
                    "provider.default",
                    "default",
                    "default"),
            ]);
}
