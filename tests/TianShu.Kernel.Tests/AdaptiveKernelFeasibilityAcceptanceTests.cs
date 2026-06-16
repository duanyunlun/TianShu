using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Tools;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Interpretation;
using TianShu.Kernel.Validation;

namespace TianShu.Kernel.Tests;

public sealed class AdaptiveKernelFeasibilityAcceptanceTests
{
    [Fact]
    public async Task A1ManualTurnStageGraphFixture_ShouldPassKernelValidatorAndInterpreterWithoutCoreLoopShell()
    {
        var intent = CreateAcceptanceIntent();
        var graph = CreateAcceptanceGraph(intent);
        var validator = new KernelValidator();
        var interpreter = new StageGraphInterpreter();
        var context = new KernelValidationContext(intent, graph: graph);
        var interpreterContext = new KernelInterpreterContext(
            intent,
            new KernelRunState(new KernelRunId("run-a1-fixture"), intent.IntentId, selectedGraphId: graph.GraphId),
            new KernelRunOptions(runId: new KernelRunId("run-a1-fixture"), requireHumanGate: false));

        var graphResult = await validator.ValidateGraphAsync(graph, context);
        var plan = await interpreter.InterpretAsync(graph, interpreterContext);

        Assert.True(graphResult.IsApproved);
        Assert.Equal("graph.acceptance.real_turn", graph.GraphId.Value);
        Assert.All(graph.Stages, stage => Assert.NotEqual("core_loop", stage.Kind));
        Assert.Contains(graph.Edges, edge => edge.TransitionKind == StageTransitionKind.Conditional);
        Assert.Contains(graph.Edges, edge => edge.TransitionKind == StageTransitionKind.Recovery);
        Assert.True(graph.RecoveryRules.Enabled);
        Assert.Equal(graph.Stages.Count, plan.Steps.Count);
        Assert.All(plan.Steps, step =>
        {
            Assert.Equal(intent.IntentId, step.SourceIntentId);
            Assert.Equal(graph.GraphId, step.SourceGraphId);
            Assert.NotEqual(RuntimeStepKind.Unspecified, step.StepKind);
            Assert.NotEqual("core_loop", Assert.IsType<ModuleCapabilityStep>(step).CapabilityId);
        });
    }

    [Fact]
    public async Task A1ManualTurnRuntimeStepMapping_ShouldKeepTraceableSourcesForProviderToolSteerInterruptResumeAndSubagent()
    {
        var intent = CreateAcceptanceIntent();
        var graph = CreateAcceptanceGraph(intent);
        var validator = new KernelValidator();
        var plan = CreateRuntimeStepMapping(intent, graph);

        Assert.Equal(11, plan.Steps.Count);
        Assert.Contains(plan.Steps, step => step.StepKind == RuntimeStepKind.ModelInvocation);
        Assert.Contains(plan.Steps, step => step.StepKind == RuntimeStepKind.ToolInvocation);
        Assert.Contains(plan.Steps, step => step.StepKind == RuntimeStepKind.HostInteraction);
        Assert.Contains(plan.Steps, step => step.StepKind == RuntimeStepKind.ModuleCapability);
        Assert.Contains(plan.Steps, step => step.StepKind == RuntimeStepKind.StateCommit);
        Assert.Contains(plan.Steps, step => step.StepKind == RuntimeStepKind.Artifact);
        Assert.Contains(plan.Steps, step => step.StepKind == RuntimeStepKind.Diagnostic);

        var graphResult = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));
        Assert.True(graphResult.IsApproved);

        var stagesById = graph.Stages.ToDictionary(static stage => stage.StageId);
        foreach (var step in plan.Steps)
        {
            Assert.True(stagesById.TryGetValue(step.SourceStageId, out var stage));
            Assert.Equal(OperationIdFor(stage!), step.SourceKernelOperationId);

            var operation = new RequestCapabilityCallOperation(
                step.SourceKernelOperationId,
                intent.IntentId,
                step.SourceStageId,
                stage!.AllowedCapabilityToolIds.Single(),
                StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["runtimeStepKind"] = step.StepKind.ToString(),
                    ["stageKind"] = stage.Kind,
                }),
                step.Permission,
                step.SideEffect);

            var operationResult = await validator.ValidateOperationAsync(operation, new KernelValidationContext(intent, graph: graph, stage: stage));
            var stepResult = await validator.ValidateRuntimeStepAsync(step, new KernelValidationContext(intent, graph: graph, stage: stage));

            Assert.True(operationResult.IsApproved);
            Assert.True(stepResult.IsApproved);
            Assert.True(step.TracePolicy.Enabled);
            Assert.True(step.TracePolicy.RequireDiagnosticsRef);
            Assert.True(step.TracePolicy.RequireRuntimeTraceRef);
        }

        AssertStageStep<ModelInvocationStep>(plan, "stage-model-initial", step => Assert.Equal("provider.openai.responses", step.ProviderModuleId));
        AssertStageStep<ToolInvocationStep>(plan, "stage-tool-call", step => Assert.Equal("tool.workspace.read", step.CapabilityToolId));
        AssertStageStep<HostInteractionStep>(plan, "stage-steer-boundary", step => Assert.Equal("steer.apply_next_tool_boundary", step.InteractionKind));
        AssertStageStep<HostInteractionStep>(plan, "stage-interrupt-boundary", step => Assert.Equal("interrupt.cancel_tail_stream", step.InteractionKind));
        AssertStageStep<HostInteractionStep>(plan, "stage-resume-intent", step => Assert.Equal("resume.create_intent", step.InteractionKind));
        AssertStageStep<ModuleCapabilityStep>(plan, "stage-subagent-spawn", step => Assert.Equal("subagent.spawn", step.CapabilityId));
        AssertStageStep<ModuleCapabilityStep>(plan, "stage-subagent-wait", step => Assert.Equal("subagent.wait", step.CapabilityId));
    }

    private static TurnIntent CreateAcceptanceIntent()
    {
        var allowedCapabilities = AcceptanceCapabilityIds();
        return new TurnIntent(
            new CoreIntentId("intent-acceptance-real-turn"),
            new KernelSubjectRef(new SessionId("session-acceptance"), new ThreadId("thread-acceptance"), turnId: new TurnId("turn-acceptance-001")),
            new GovernanceEnvelope(
                "governance-acceptance",
                policyIds: ["policy.acceptance.kernel"],
                allowedToolIds: allowedCapabilities.Prepend("kernel.request_capability_call").ToArray(),
                allowedModuleIds:
                [
                    "provider.openai.responses",
                    "module.subagent",
                    "module.artifact",
                    "module.diagnostics",
                    "module.state",
                ],
                maxSideEffectLevel: SideEffectLevel.Privileged,
                requiresHumanGate: false),
            "final-acceptance-user-turn",
            new KernelBudget(tokenBudget: 16_384, timeBudgetMs: 120_000, retryBudget: 2, toolCallBudget: 8));
    }

    private static StageGraph CreateAcceptanceGraph(CoreIntent intent)
    {
        var stages = new[]
        {
            CreateStage("stage-model-initial", "model.invoke.initial", "Call provider for the first planning turn.", SideEffectLevel.ExternalNetwork),
            CreateStage("stage-tool-call", "tool.workspace.read", "Execute a read-only workspace tool requested by the model.", SideEffectLevel.ReadOnly),
            CreateStage("stage-model-followup", "model.invoke.followup", "Send tool result back to provider and receive follow-up action.", SideEffectLevel.ExternalNetwork),
            CreateStage("stage-steer-boundary", "host.steer.apply_next_tool_boundary", "Apply steer at the next model/tool boundary.", SideEffectLevel.HostMutation),
            CreateStage("stage-interrupt-boundary", "host.interrupt.cancel_tail_stream", "Cancel the active turn and suppress stale tail output.", SideEffectLevel.HostMutation),
            CreateStage("stage-resume-intent", "host.resume.create_intent", "Create a resume intent from the interrupt checkpoint.", SideEffectLevel.HostMutation),
            CreateStage("stage-subagent-spawn", "subagent.spawn", "Spawn a subagent for a bounded verification slice.", SideEffectLevel.ExternalNetwork),
            CreateStage("stage-subagent-wait", "subagent.wait", "Wait for subagent completion and collect result.", SideEffectLevel.ReadOnly),
            CreateStage("stage-artifact-publish", "artifact.publish", "Publish the auditable delivery artifact.", SideEffectLevel.WorkspaceWrite),
            CreateStage("stage-diagnostics", "diagnostics.emit_trace", "Emit runtime trace and projection references.", SideEffectLevel.None),
        };

        return new StageGraph(
            new StageGraphId("graph.acceptance.real_turn"),
            "1",
            intent.IntentKind,
            stages[0].StageId,
            stages,
            CreateEdges(stages),
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                requiredPolicyIds: ["policy.acceptance.kernel"],
                allowedKernelToolIds: ["kernel.request_capability_call"],
                allowedCapabilityToolIds: AcceptanceCapabilityIds(),
                allowedModuleIds:
                [
                    "provider.openai.responses",
                    "module.subagent",
                    "module.artifact",
                    "module.diagnostics",
                    "module.state",
                ],
                maxSideEffectLevel: SideEffectLevel.Privileged,
                requiresHumanGate: false),
            new KernelBudget(tokenBudget: 16_384, timeBudgetMs: 120_000, retryBudget: 2, toolCallBudget: 8),
            new CheckpointRules(enabled: true, requiredStageIds: [new StageId("stage-interrupt-boundary"), new StageId("stage-artifact-publish")]),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1),
            new EvaluationRules(enabled: true, metricIds: ["evaluator.acceptance.trace"]),
            new StageGraphMetadata("acceptance", "manual-a1-fixture"));
    }

    private static ExecutionPlan CreateRuntimeStepMapping(CoreIntent intent, StageGraph graph)
    {
        var stages = graph.Stages.ToDictionary(static stage => stage.StageId.Value, StringComparer.Ordinal);
        var steps = new RuntimeStep[]
        {
            CreateModelStep(intent, graph, stages["stage-model-initial"], "step-model-initial"),
            CreateToolStep(intent, graph, stages["stage-tool-call"], "step-tool-workspace-read"),
            CreateModelStep(intent, graph, stages["stage-model-followup"], "step-model-followup"),
            CreateHostStep(intent, graph, stages["stage-steer-boundary"], "step-steer", "steer.apply_next_tool_boundary"),
            CreateHostStep(intent, graph, stages["stage-interrupt-boundary"], "step-interrupt", "interrupt.cancel_tail_stream"),
            CreateHostStep(intent, graph, stages["stage-resume-intent"], "step-resume", "resume.create_intent"),
            CreateModuleStep(intent, graph, stages["stage-subagent-spawn"], "step-subagent-spawn", "module.subagent", "subagent.spawn"),
            CreateModuleStep(intent, graph, stages["stage-subagent-wait"], "step-subagent-wait", "module.subagent", "subagent.wait"),
            CreateStateCommitStep(intent, graph, stages["stage-artifact-publish"], "step-state-commit"),
            CreateArtifactStep(intent, graph, stages["stage-artifact-publish"], "step-artifact-publish"),
            CreateDiagnosticStep(intent, graph, stages["stage-diagnostics"], "step-diagnostics"),
        };

        return new ExecutionPlan(
            "plan-acceptance-real-turn",
            graph.GraphId,
            intent.IntentId,
            steps,
            new ExecutionPlanPolicy(sequential: true, stopOnFailure: true, maxParallelism: 1),
            CreateTracePolicy());
    }

    private static StageNode CreateStage(string stageId, string capabilityId, string objective, SideEffectLevel sideEffectLevel)
        => new(
            new StageId(stageId),
            capabilityId,
            objective,
            new ContractRef($"contract.{capabilityId}.input", "1"),
            new ContractRef($"contract.{capabilityId}.output", "1"),
            ["kernel.request_capability_call"],
            [capabilityId],
            CreateModelRoutePolicy(),
            new ContextPolicy(maxInputTokens: 8_192, allowedSourceKinds: ["CurrentUserInput", "ConversationHistory", "WorkspaceFact", "ToolEvidence"]),
            sideEffectLevel,
            new KernelBudget(tokenBudget: 4_096, timeBudgetMs: 30_000, retryBudget: 1, toolCallBudget: 2),
            new SuccessCriteria([$"{capabilityId}.completed"]),
            new FailureHandlerRef("handler.acceptance.recover", mayRecover: true));

    private static IReadOnlyList<StageEdge> CreateEdges(IReadOnlyList<StageNode> stages)
    {
        var edges = new List<StageEdge>();
        for (var index = 0; index < stages.Count - 1; index++)
        {
            edges.Add(new StageEdge(
                new StageEdgeId($"edge-{index + 1:D2}"),
                stages[index].StageId,
                stages[index + 1].StageId,
                new TransitionCondition("on_success"),
                new TransitionGuard(requiredSignals: [$"{stages[index].Kind}.completed"]),
                index == 2 ? StageTransitionKind.Conditional : StageTransitionKind.Success));
        }

        edges.Add(new StageEdge(
            new StageEdgeId("edge-recovery-tool-diagnostics"),
            stages[1].StageId,
            stages[^1].StageId,
            new TransitionCondition("on_tool_failure"),
            new TransitionGuard(requiredSignals: ["tool.workspace.read.failed"]),
            StageTransitionKind.Recovery));

        return edges;
    }

    private static ModelRoutePolicy CreateModelRoutePolicy()
        => new(
            routeCandidateIds: ["route.openai.responses"],
            preferredRouteId: "route.openai.responses",
            candidates:
            [
                new ModelRouteCandidateBinding(
                    "route.openai.responses",
                    "provider.openai.responses",
                    "openai",
                    "gpt-5"),
            ]);

    private static RuntimeStep CreateModelStep(CoreIntent intent, StageGraph graph, StageNode stage, string stepId)
        => new ModelInvocationStep(
            stepId,
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            OperationIdFor(stage),
            "provider.openai.responses",
            stage.ModelRoutePolicy,
            new ProviderInvocationRequest(
                new ExecutionId($"execution-{stepId}"),
                "openai",
                "gpt-5",
                new ProviderConversationContext(intent.Subject.ThreadId, intent.Subject.TurnId),
                [new TextProviderInputItem(stage.Objective)]),
            CreatePermission(stage),
            new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            CreateTracePolicy());

    private static RuntimeStep CreateToolStep(CoreIntent intent, StageGraph graph, StageNode stage, string stepId)
        => new ToolInvocationStep(
            stepId,
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            OperationIdFor(stage),
            stage.AllowedCapabilityToolIds.Single(),
            new ToolInvocationEnvelope(
                new CallId("call-workspace-read"),
                "tool.workspace.read",
                "read",
                StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["path"] = "src",
                    ["reason"] = stage.Objective,
                }),
                CreatePermission(stage),
                new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true)),
            CreatePermission(stage),
            new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            CreateTracePolicy());

    private static RuntimeStep CreateHostStep(CoreIntent intent, StageGraph graph, StageNode stage, string stepId, string interactionKind)
        => new HostInteractionStep(
            stepId,
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            OperationIdFor(stage),
            interactionKind,
            StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["boundary"] = stage.StageId.Value,
                ["objective"] = stage.Objective,
            }),
            CreatePermission(stage),
            new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            CreateTracePolicy());

    private static RuntimeStep CreateModuleStep(CoreIntent intent, StageGraph graph, StageNode stage, string stepId, string moduleId, string capabilityId)
        => new ModuleCapabilityStep(
            stepId,
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            OperationIdFor(stage),
            moduleId,
            capabilityId,
            StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["capability"] = capabilityId,
                ["objective"] = stage.Objective,
            }),
            CreatePermission(stage),
            new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            CreateTracePolicy());

    private static RuntimeStep CreateStateCommitStep(CoreIntent intent, StageGraph graph, StageNode stage, string stepId)
        => new StateCommitStep(
            stepId,
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            OperationIdFor(stage),
            "module.state",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["artifactStage"] = stage.StageId.Value,
                ["state"] = "artifact-ready",
            }),
            CreatePermission(stage),
            new SideEffectProfile(SideEffectLevel.WorkspaceWrite, requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            CreateTracePolicy());

    private static RuntimeStep CreateArtifactStep(CoreIntent intent, StageGraph graph, StageNode stage, string stepId)
        => new ArtifactStep(
            stepId,
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            OperationIdFor(stage),
            "publish",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["artifactId"] = "artifact-acceptance-001",
                ["sourceStage"] = stage.StageId.Value,
            }),
            CreatePermission(stage),
            new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            CreateTracePolicy());

    private static RuntimeStep CreateDiagnosticStep(CoreIntent intent, StageGraph graph, StageNode stage, string stepId)
        => new DiagnosticStep(
            stepId,
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            OperationIdFor(stage),
            "execution.trace",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["traceRef"] = "trace-acceptance-real-turn",
                ["projectionRef"] = "projection-acceptance-real-turn",
            }),
            CreatePermission(stage),
            new SideEffectProfile(stage.SideEffectLevel, requiresAudit: true),
            stage.Budget,
            stage.OutputContract,
            CreateTracePolicy());

    private static PermissionEnvelope CreatePermission(StageNode stage)
        => new(stage.AllowedCapabilityToolIds, requiresHumanGate: false, reason: stage.Objective);

    private static TracePolicy CreateTracePolicy()
        => new(enabled: true, requireDiagnosticsRef: true, requireRuntimeTraceRef: true);

    private static KernelOperationId OperationIdFor(StageNode stage)
        => new($"operation-{stage.StageId.Value}");

    private static string[] AcceptanceCapabilityIds()
        =>
        [
            "model.invoke.initial",
            "tool.workspace.read",
            "model.invoke.followup",
            "host.steer.apply_next_tool_boundary",
            "host.interrupt.cancel_tail_stream",
            "host.resume.create_intent",
            "subagent.spawn",
            "subagent.wait",
            "artifact.publish",
            "diagnostics.emit_trace",
        ];

    private static void AssertStageStep<TStep>(ExecutionPlan plan, string stageId, Action<TStep> assert)
        where TStep : RuntimeStep
    {
        var step = Assert.IsType<TStep>(Assert.Single(plan.Steps, candidate => candidate.SourceStageId == new StageId(stageId)));
        assert(step);
    }
}
