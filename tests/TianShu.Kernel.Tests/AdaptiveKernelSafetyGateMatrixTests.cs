using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Validation;

namespace TianShu.Kernel.Tests;

public sealed class AdaptiveKernelSafetyGateMatrixTests
{
    [Fact]
    public async Task KernelValidator_ShouldFailClosedForBStageNegativeMatrix()
    {
        var validator = new KernelValidator();
        var cases = new (string Id, Func<Task<GateOutcome>> Run)[]
        {
            ("intent-null", async () => await Validate(() => validator.ValidateIntentAsync(null!, Context()))),
            ("intent-missing-governance", () => ContractGuard(() => new MatrixIntent(CoreIntentKind.Turn, null!))),
            ("intent-unspecified-kind", async () => await Validate(() => validator.ValidateIntentAsync(new MatrixIntent(CoreIntentKind.Unspecified, Governance()), Context()))),
            ("graph-empty-stage-list", async () => await ValidateGraph(validator, Graph(stages: Array.Empty<StageNode>()))),
            ("graph-entry-stage-missing", async () => await ValidateGraph(validator, Graph(entryStageId: new StageId("missing-stage")))),
            ("graph-duplicate-stage-id", async () => await ValidateGraph(validator, Graph(stages: new[] { Stage("stage-1"), Stage("stage-1") }))),
            ("graph-edge-to-missing-stage", async () => await ValidateGraph(validator, Graph(edges: new[] { Edge("stage-1", "missing-stage") }))),
            ("graph-unreachable-stage", async () => await ValidateGraph(validator, Graph(stages: new[] { Stage("stage-1"), Stage("stage-2") }))),
            ("graph-no-terminal-stage", async () => await ValidateGraph(validator, Graph(edges: new[] { Edge("stage-1", "stage-2"), Edge("stage-2", "stage-1") }, recoveryRules: new RecoveryRules(enabled: true, maxRecoveryAttempts: 1)))),
            ("graph-unbounded-cycle", async () => await ValidateGraph(validator, Graph(edges: new[] { Edge("stage-1", "stage-2"), Edge("stage-2", "stage-1") }, recoveryRules: new RecoveryRules(enabled: false)))),
            ("graph-zero-budget", async () => await ValidateGraph(validator, Graph(budget: KernelBudget.Zero))),
            ("graph-side-effect-exceeds-governance", async () => await ValidateGraph(validator, Graph(policies: Policies(maxSideEffectLevel: SideEffectLevel.ExternalMutation)), Intent(maxSideEffectLevel: SideEffectLevel.ReadOnly))),
            ("graph-kernel-tool-outside-governance", async () => await ValidateGraph(validator, Graph(policies: Policies(allowedKernelToolIds: new[] { "kernel.request_capability_call" })), Intent(allowedToolIds: new[] { "module.core_loop" }))),
            ("graph-module-outside-governance", async () => await ValidateGraph(validator, Graph(policies: Policies(allowedModuleIds: new[] { "module.not-governed" })))),
            ("stage-unspecified-side-effect", async () => await ValidateGraph(validator, Graph(stages: new[] { Stage("stage-1", sideEffectLevel: SideEffectLevel.Unspecified) }))),
            ("stage-unbounded-budget", async () => await ValidateGraph(validator, Graph(stages: new[] { Stage("stage-1", budget: KernelBudget.Zero) }))),
            ("stage-fail-closed-model-route-without-candidate", async () => await ValidateGraph(validator, Graph(stages: new[] { Stage("stage-1", modelRoutePolicy: new ModelRoutePolicy()) }))),
            ("stage-context-policy-missing-budget", async () => await ValidateGraph(validator, Graph(stages: new[] { Stage("stage-1", contextPolicy: new ContextPolicy(maxInputTokens: 0, failClosed: true)) }))),
            ("operation-stage-mismatch", async () => await ValidateOperation(validator, Operation(sourceStageId: new StageId("stage-other")))),
            ("operation-unspecified-side-effect", async () => await ValidateOperation(validator, Operation(sideEffect: new SideEffectProfile(SideEffectLevel.Unspecified)))),
            ("operation-side-effect-exceeds-stage", async () => await ValidateOperation(validator, Operation(sideEffect: new SideEffectProfile(SideEffectLevel.WorkspaceWrite)))),
            ("operation-capability-not-allowed", async () => await ValidateOperation(validator, Operation(capabilityToolId: "tool.not-allowed"))),
        };

        foreach (var (id, run) in cases)
        {
            var outcome = await run();

            Assert.False(outcome.Approved);
            Assert.False(string.IsNullOrWhiteSpace(outcome.Code), id);
        }
    }

    [Fact]
    public async Task KernelValidator_ShouldApproveBStagePositiveControls()
    {
        var validator = new KernelValidator();
        var intent = Intent();
        var graph = Graph();
        var stage = Assert.Single(graph.Stages);
        var context = new KernelValidationContext(intent, graph: graph, stage: stage);
        var operation = Operation();
        var step = new ModuleCapabilityStep(
            "step-positive",
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            operation.OperationId,
            "module.core_loop",
            "module.core_loop",
            StructuredValue.Null,
            Permission,
            new SideEffectProfile(SideEffectLevel.ReadOnly),
            Budget,
            OutputContract,
            TracePolicy);

        Assert.True((await validator.ValidateIntentAsync(intent, context)).IsApproved);
        Assert.True((await validator.ValidateGraphAsync(graph, context)).IsApproved);
        Assert.True((await validator.ValidateOperationAsync(operation, context)).IsApproved);
        Assert.True((await validator.ValidateRuntimeStepAsync(step, context)).IsApproved);
    }

    private static async Task<GateOutcome> Validate(Func<Task<KernelValidationResult>> action)
    {
        try
        {
            var result = await action();
            return new GateOutcome(result.IsApproved, result.Issues.FirstOrDefault()?.Code ?? "approved");
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentNullException)
        {
            return new GateOutcome(false, ex.GetType().Name);
        }
    }

    private static Task<GateOutcome> ContractGuard(Action action)
    {
        try
        {
            action();
            return Task.FromResult(new GateOutcome(true, "constructed"));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentNullException)
        {
            return Task.FromResult(new GateOutcome(false, ex.GetType().Name));
        }
    }

    private static Task<GateOutcome> ValidateGraph(KernelValidator validator, StageGraph graph, CoreIntent? intent = null)
        => Validate(() => validator.ValidateGraphAsync(graph, new KernelValidationContext(intent ?? Intent(), graph: graph)));

    private static KernelValidationContext Context()
        => new(Intent());

    private static Task<GateOutcome> ValidateOperation(KernelValidator validator, KernelOperation operation)
    {
        var intent = Intent();
        var graph = Graph();
        var stage = Assert.Single(graph.Stages);
        return Validate(() => validator.ValidateOperationAsync(operation, new KernelValidationContext(intent, graph: graph, stage: stage)));
    }

    private static CoreIntent Intent(
        IReadOnlyList<string>? allowedToolIds = null,
        SideEffectLevel maxSideEffectLevel = SideEffectLevel.ReadOnly)
        => new MatrixIntent(
            CoreIntentKind.Turn,
            Governance(
                allowedToolIds: allowedToolIds ?? new[] { "kernel.request_capability_call", "module.core_loop" },
                allowedModuleIds: new[] { "module.core_loop" },
                maxSideEffectLevel: maxSideEffectLevel));

    private static GovernanceEnvelope Governance(
        IReadOnlyList<string>? allowedToolIds = null,
        IReadOnlyList<string>? allowedModuleIds = null,
        SideEffectLevel maxSideEffectLevel = SideEffectLevel.ReadOnly)
        => new(
            "governance-matrix",
            allowedToolIds: allowedToolIds ?? new[] { "kernel.request_capability_call", "module.core_loop" },
            allowedModuleIds: allowedModuleIds ?? new[] { "module.core_loop" },
            maxSideEffectLevel: maxSideEffectLevel,
            requiresHumanGate: false);

    private static StageGraph Graph(
        StageId? entryStageId = null,
        IReadOnlyList<StageNode>? stages = null,
        IReadOnlyList<StageEdge>? edges = null,
        GraphPolicySet? policies = null,
        KernelBudget? budget = null,
        RecoveryRules? recoveryRules = null)
        => new(
            new StageGraphId("graph-matrix"),
            "1",
            CoreIntentKind.Turn,
            entryStageId ?? new StageId("stage-1"),
            stages ?? new[] { Stage("stage-1") },
            edges ?? Array.Empty<StageEdge>(),
            policies ?? Policies(),
            budget ?? Budget,
            new CheckpointRules(),
            recoveryRules ?? new RecoveryRules(enabled: true, maxRecoveryAttempts: 1),
            new EvaluationRules(),
            new StageGraphMetadata("test", "b-stage-matrix"));

    private static GraphPolicySet Policies(
        IReadOnlyList<string>? allowedKernelToolIds = null,
        IReadOnlyList<string>? allowedCapabilityToolIds = null,
        IReadOnlyList<string>? allowedModuleIds = null,
        SideEffectLevel maxSideEffectLevel = SideEffectLevel.ReadOnly)
        => new(
            PolicyEnforcementMode.AllowListed,
            allowedKernelToolIds: allowedKernelToolIds ?? new[] { "kernel.request_capability_call" },
            allowedCapabilityToolIds: allowedCapabilityToolIds ?? new[] { "module.core_loop" },
            allowedModuleIds: allowedModuleIds ?? new[] { "module.core_loop" },
            maxSideEffectLevel: maxSideEffectLevel,
            requiresHumanGate: false);

    private static StageNode Stage(
        string stageId,
        SideEffectLevel sideEffectLevel = SideEffectLevel.ReadOnly,
        KernelBudget? budget = null,
        ModelRoutePolicy? modelRoutePolicy = null,
        ContextPolicy? contextPolicy = null)
        => new(
            new StageId(stageId),
            "core_loop",
            "test stage",
            new ContractRef("input", "1"),
            new ContractRef("output", "1"),
            new[] { "kernel.request_capability_call" },
            new[] { "module.core_loop" },
            modelRoutePolicy ?? ModelRoute(),
            contextPolicy ?? new ContextPolicy(maxInputTokens: 128),
            sideEffectLevel,
            budget ?? Budget,
            new SuccessCriteria(new[] { "ok" }),
            new FailureHandlerRef("fail", mayRecover: true));

    private static StageEdge Edge(string from, string to)
        => new(
            new StageEdgeId($"edge-{from}-{to}"),
            new StageId(from),
            new StageId(to),
            new TransitionCondition("always"),
            new TransitionGuard(),
            StageTransitionKind.Success);

    private static ModelRoutePolicy ModelRoute()
        => new(
            routeCandidateIds: new[] { "route.default" },
            preferredRouteId: "route.default",
            candidates: new[] { new ModelRouteCandidateBinding("route.default", "provider.module", "provider", "model") });

    private static RequestCapabilityCallOperation Operation(
        StageId? sourceStageId = null,
        string capabilityToolId = "module.core_loop",
        SideEffectProfile? sideEffect = null)
        => new(
            new KernelOperationId("operation-matrix"),
            new CoreIntentId("intent-matrix"),
            sourceStageId ?? new StageId("stage-1"),
            capabilityToolId,
            StructuredValue.Null,
            Permission,
            sideEffect ?? new SideEffectProfile(SideEffectLevel.ReadOnly));

    private sealed record MatrixIntent(CoreIntentKind Kind, GovernanceEnvelope GovernanceEnvelope)
        : CoreIntent(
            new CoreIntentId("intent-matrix"),
            Kind,
            new KernelSubjectRef(new SessionId("session-matrix"), new ThreadId("thread-matrix")),
            GovernanceEnvelope,
            AdaptiveKernelSafetyGateMatrixTests.Budget);

    private sealed record GateOutcome(bool Approved, string Code);

    private static readonly PermissionEnvelope Permission = new(scopes: new[] { "runtime.execute" }, requiresHumanGate: false);
    private static readonly KernelBudget Budget = new(tokenBudget: 1_000, timeBudgetMs: 1_000, costBudget: 1, retryBudget: 1, toolCallBudget: 1);
    private static readonly ContractRef OutputContract = new("runtime.output", "v1");
    private static readonly TracePolicy TracePolicy = new();
}
