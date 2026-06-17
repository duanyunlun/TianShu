using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Graphs;
using TianShu.Kernel.Interpretation;
using TianShu.Kernel.Tracing;
using TianShu.Kernel.Validation;

namespace TianShu.Kernel.Tests;

public sealed class StableKernelCoreTests
{
    [Fact]
    public async Task RunAsync_DefaultIntentProducesApprovedExecutionPlanAndTrace()
    {
        var traceStore = new InMemoryKernelTraceStore();
        var core = new StableKernelCore(traceStore: traceStore);
        var intent = CreateIntent();

        var result = await core.RunAsync(intent, new KernelRunOptions(runId: new KernelRunId("run-001"), requireHumanGate: false));
        var trace = await traceStore.ReadRunTraceAsync(result.RunId);

        Assert.Equal(KernelRunLifecycleState.Executing, result.LifecycleState);
        Assert.True(result.Validation.IsApproved);
        Assert.NotNull(result.ExecutionPlan);
        Assert.Equal(new StageGraphId("graph.turn.default"), result.ExecutionPlan!.SourceGraphId);
        Assert.All(result.ExecutionPlan.Steps, step =>
        {
            Assert.Equal(intent.IntentId, step.SourceIntentId);
            Assert.Equal(new StageGraphId("graph.turn.default"), step.SourceGraphId);
            Assert.NotEqual(RuntimeStepKind.Unspecified, step.StepKind);
        });
        Assert.Contains(result.ExecutionPlan.Steps, step => step.StepKind == RuntimeStepKind.ModuleCapability && step.SourceStageId == new StageId("stage.prepare-context"));
        Assert.Contains(result.ExecutionPlan.Steps, step => step.StepKind == RuntimeStepKind.ModelInvocation && step.SourceStageId == new StageId("stage.model-reason"));
        Assert.Contains(result.ExecutionPlan.Steps, step => step.StepKind == RuntimeStepKind.ToolInvocation && step.SourceStageId == new StageId("stage.tool-exec"));
        Assert.Contains(result.ExecutionPlan.Steps, step => step.StepKind == RuntimeStepKind.StateCommit && step.SourceStageId == new StageId("stage.finalize"));
        Assert.Contains(result.ExecutionPlan.Steps, step => step.StepKind == RuntimeStepKind.Diagnostic && step.SourceStageId == new StageId("stage.finalize"));
        Assert.NotNull(trace);
        Assert.Contains(trace!.Events, item => item.Kind == KernelTraceEventKind.IntentAccepted);
        Assert.Contains(trace.Events, item => item.Kind == KernelTraceEventKind.GraphValidated);
        Assert.Contains(trace.Events, item => item.Kind == KernelTraceEventKind.ExecutionPlanCreated);
        Assert.Contains(trace.Events, item => item.Kind == KernelTraceEventKind.CheckpointCreated);
        Assert.Contains(trace.Events, item => item.Kind == KernelTraceEventKind.EvaluationRecorded);
    }

    [Fact]
    public void TurnIntentRejectsMissingGovernanceEnvelopeBeforeKernelAdmission()
    {
        Assert.Throws<ArgumentNullException>(() => new TurnIntent(
            new CoreIntentId("intent-no-governance"),
            new KernelSubjectRef(new SessionId("session-001"), new ThreadId("thread-001")),
            governance: null!,
            userInputRef: "input-001"));
    }

    [Fact]
    public async Task ValidatorApprovesDefaultGraphOperationAndRuntimeStepPositiveControls()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();
        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));
        var stage = Assert.Single(graph.Stages, candidate => candidate.StageId == new StageId("stage.tool-exec"));
        const string capabilityToolId = "read_file";
        Assert.Contains(capabilityToolId, stage.AllowedCapabilityToolIds);
        var context = new KernelValidationContext(intent, graph: graph, stage: stage);
        var operation = new RequestCapabilityCallOperation(
            new KernelOperationId("operation-positive"),
            intent.IntentId,
            stage.StageId,
            capabilityToolId,
            StructuredValue.Null,
            new PermissionEnvelope(requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.WorkspaceWrite));
        var step = new ToolInvocationStep(
            "step-positive",
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            operation.OperationId,
            capabilityToolId,
            new ToolInvocationEnvelope(
                new CallId("call-positive"),
                capabilityToolId,
                "execute_approved_requests",
                StructuredValue.Null,
                new PermissionEnvelope(requiresHumanGate: false),
                new SideEffectProfile(SideEffectLevel.WorkspaceWrite)),
            new PermissionEnvelope(requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.WorkspaceWrite),
            stage.Budget,
            stage.OutputContract,
            new TracePolicy());

        var graphResult = await validator.ValidateGraphAsync(graph, context);
        var operationResult = await validator.ValidateOperationAsync(operation, context);
        var stepResult = await validator.ValidateRuntimeStepAsync(step, context);

        Assert.True(graphResult.IsApproved);
        Assert.True(operationResult.IsApproved);
        Assert.True(stepResult.IsApproved);
    }

    [Fact]
    public async Task ValidatorRejectsIllegalStrategyTransitionAndPromotionWithoutMetricEvidence()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent(requiresHumanGate: true, approvalIds: new[] { new ApprovalId("approval-001") });
        var context = new KernelValidationContext(intent);
        var candidate = new StrategyRecord(
            new StrategyId("strategy-001"),
            "Candidate strategy",
            new StageGraphId("graph-001"));

        var illegal = await validator.ValidateStrategyTransitionAsync(
            candidate,
            StrategyLifecycleState.Promoted,
            CreateStrategyEvidence(humanApproved: true),
            context);
        var trial = new StrategyRecord(candidate.StrategyId, candidate.Name, candidate.GraphId, StrategyLifecycleState.Trial);
        var missingMetrics = await validator.ValidateStrategyTransitionAsync(
            trial,
            StrategyLifecycleState.Promoted,
            new[]
            {
                new StrategyTransitionEvidence(
                    new KernelRunId("run-001"),
                    new KernelTraceId("trace-001"),
                    "evidence://promotion/no-metrics",
                    humanApproved: true),
            },
            context);
        var missingHumanGate = await validator.ValidateStrategyTransitionAsync(
            trial,
            StrategyLifecycleState.Promoted,
            CreateStrategyEvidence(humanApproved: false),
            context);

        Assert.Equal(KernelValidationDecision.Rejected, illegal.Decision);
        Assert.Contains(illegal.Issues, issue => issue.Code == "kernel.strategy.illegal_transition");
        Assert.Equal(KernelValidationDecision.Rejected, missingMetrics.Decision);
        Assert.Contains(missingMetrics.Issues, issue => issue.Code == "kernel.strategy.missing_evaluation");
        Assert.Equal(KernelValidationDecision.Rejected, missingHumanGate.Decision);
        Assert.Contains(missingHumanGate.Issues, issue => issue.Code == "kernel.strategy.missing_human_gate");
    }

    [Fact]
    public async Task DefaultTurnGraphUsesBuiltinFourStageGraphWithoutCoreLoopShell()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();
        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));

        var result = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.True(result.IsApproved);
        Assert.Equal("graph.turn.default", graph.GraphId.Value);
        Assert.Equal("builtin", graph.Metadata.Owner);
        Assert.Equal("fixed", graph.Metadata.Source);
        Assert.Equal(new StageId("stage.prepare-context"), graph.EntryStageId);
        Assert.Equal(
            ["prepare-context", "model-reason", "tool-exec", "finalize"],
            graph.Stages.Select(static stage => stage.Kind).ToArray());
        Assert.Contains(graph.Edges, edge => edge.FromStageId == new StageId("stage.tool-exec") && edge.ToStageId == new StageId("stage.model-reason"));
        Assert.DoesNotContain("shell", graph.Policies.AllowedCapabilityToolIds);
        Assert.DoesNotContain(graph.Stages, stage => string.Equals(stage.Kind, "core_loop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DefaultTurnGraphNarrowsCapabilityToolsToGovernanceReadOnlyBoundary()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent(
            allowedToolIds:
            [
                "update_context_policy",
                "request_capability_call",
                "module.core_loop",
                "read_file",
                "list_dir",
                "grep",
                "glob",
            ]);
        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));

        var validation = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));
        var toolStage = Assert.Single(graph.Stages, stage => stage.StageId == new StageId("stage.tool-exec"));
        var reasonToTool = Assert.Single(graph.Edges, edge => edge.EdgeId == new StageEdgeId("edge.reason-to-tool"));

        Assert.True(validation.IsApproved);
        Assert.Equal(SideEffectLevel.ReadOnly, toolStage.SideEffectLevel);
        Assert.Equal(["read_file", "list_dir", "grep", "glob"], toolStage.AllowedCapabilityToolIds);
        Assert.Equal(["read_file", "list_dir", "grep", "glob"], graph.Policies.AllowedCapabilityToolIds);
        Assert.Equal(["read_file", "list_dir", "grep", "glob"], reasonToTool.Guard.Permission!.Scopes);
        Assert.DoesNotContain("apply_patch", graph.Policies.AllowedCapabilityToolIds);
        Assert.DoesNotContain("write", graph.Policies.AllowedCapabilityToolIds);
    }

    [Fact]
    public async Task DefaultTurnGraphExposesSpawnAgentOnlyWhenSubAgentModuleIsGoverned()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent(
            allowedToolIds:
            [
                "update_context_policy",
                "request_capability_call",
                "read_file",
                "spawn_agent",
            ],
            allowedModuleIds: ["kernel.default", "provider.default", "module.sub_agent"],
            maxSideEffectLevel: SideEffectLevel.HostMutation);

        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));
        var validation = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));
        var toolStage = Assert.Single(graph.Stages, stage => stage.StageId == new StageId("stage.tool-exec"));

        Assert.True(validation.IsApproved);
        Assert.Contains("spawn_agent", toolStage.AllowedCapabilityToolIds);
        Assert.Contains("spawn_agent", graph.Policies.AllowedCapabilityToolIds);
        Assert.Contains("module.sub_agent", graph.Policies.AllowedModuleIds);
        Assert.Equal(SideEffectLevel.HostMutation, toolStage.SideEffectLevel);
        Assert.Equal(SideEffectLevel.HostMutation, graph.Policies.MaxSideEffectLevel);
    }

    [Fact]
    public async Task DefaultTurnGraphKeepsSpawnAgentFailClosedWhenModuleIsNotGoverned()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent(
            allowedToolIds:
            [
                "update_context_policy",
                "request_capability_call",
                "read_file",
                "spawn_agent",
            ],
            allowedModuleIds: ["kernel.default", "provider.default"],
            maxSideEffectLevel: SideEffectLevel.HostMutation);

        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));
        var validation = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));
        var toolStage = Assert.Single(graph.Stages, stage => stage.StageId == new StageId("stage.tool-exec"));

        Assert.True(validation.IsApproved);
        Assert.DoesNotContain("spawn_agent", toolStage.AllowedCapabilityToolIds);
        Assert.DoesNotContain("spawn_agent", graph.Policies.AllowedCapabilityToolIds);
        Assert.DoesNotContain("module.sub_agent", graph.Policies.AllowedModuleIds);
    }

    [Fact]
    public async Task InterpreterMapsBuiltinTurnGraphToTypedRuntimeSteps()
    {
        var intent = CreateIntent();
        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));
        var interpreter = new StageGraphInterpreter();

        var plan = await interpreter.InterpretAsync(
            graph,
            new KernelInterpreterContext(
                intent,
                new KernelRunState(new KernelRunId("run-interpreter"), intent.IntentId, selectedGraphId: graph.GraphId),
                new KernelRunOptions(runId: new KernelRunId("run-interpreter"), requireHumanGate: false)));

        Assert.Equal(5, plan.Steps.Count);
        Assert.Contains(plan.Steps, step => step is ModuleCapabilityStep module && module.SourceStageId == new StageId("stage.prepare-context") && module.CapabilityId == "prepare_context");
        Assert.Contains(plan.Steps, step => step is ModelInvocationStep model && model.SourceStageId == new StageId("stage.model-reason") && model.ProviderModuleId == "provider.default");
        Assert.Contains(plan.Steps, step => step is ToolInvocationStep tool && tool.SourceStageId == new StageId("stage.tool-exec") && tool.CapabilityToolId == "read_file");
        Assert.Contains(plan.Steps, step => step is StateCommitStep && step.SourceStageId == new StageId("stage.finalize"));
        Assert.Contains(plan.Steps, step => step is DiagnosticStep && step.SourceStageId == new StageId("stage.finalize"));
        Assert.DoesNotContain(plan.Steps.OfType<ModuleCapabilityStep>(), step => step.CapabilityId == "core_loop");
        var toolStep = Assert.Single(plan.Steps.OfType<ToolInvocationStep>());
        Assert.Equal("static_stage_skeleton", toolStep.InputEnvelope.Input.GetProperty("toolDispatchMode").StringValue);
        Assert.Equal("stage.allow_list.first", toolStep.InputEnvelope.Input.GetProperty("selectedCapabilityToolIdSource").StringValue);
        Assert.True(toolStep.InputEnvelope.Input.GetProperty("requiresModelToolRequests").BooleanValue);
    }

    [Fact]
    public async Task InterpreterMapsMemoryStagesToOfficialMemoryModuleCapabilities()
    {
        var intent = CreateIntent(
            allowedModuleIds: ["kernel.default", "provider.default", "memory.identity"],
            maxSideEffectLevel: SideEffectLevel.ExternalMutation);
        var retrieve = CreateMemoryStage("stage-memory-retrieve", "memory-retrieve", SideEffectLevel.ReadOnly);
        var form = CreateMemoryStage("stage-memory-form", "memory-form", SideEffectLevel.ExternalMutation);
        var supersede = CreateMemoryStage("stage-memory-supersede", "memory-supersede", SideEffectLevel.ExternalMutation);
        var graph = new StageGraph(
            new StageGraphId("graph-memory"),
            "1",
            intent.IntentKind,
            retrieve.StageId,
            new[] { retrieve, form, supersede },
            new[]
            {
                new StageEdge(new StageEdgeId("edge-memory-retrieve-form"), retrieve.StageId, form.StageId, new TransitionCondition("memory retrieved"), new TransitionGuard(), StageTransitionKind.Success),
                new StageEdge(new StageEdgeId("edge-memory-form-supersede"), form.StageId, supersede.StageId, new TransitionCondition("memory formed"), new TransitionGuard(), StageTransitionKind.Success),
            },
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedModuleIds: ["memory.identity"],
                maxSideEffectLevel: SideEffectLevel.ExternalMutation,
                requiresHumanGate: false),
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, toolCallBudget: 3),
            new CheckpointRules(),
            new RecoveryRules(enabled: false),
            new EvaluationRules(),
            new StageGraphMetadata("test", "memory-capability-surface"));
        var interpreter = new StageGraphInterpreter();

        var plan = await interpreter.InterpretAsync(
            graph,
            new KernelInterpreterContext(
                intent,
                new KernelRunState(new KernelRunId("run-memory"), intent.IntentId, selectedGraphId: graph.GraphId),
                new KernelRunOptions(runId: new KernelRunId("run-memory"), requireHumanGate: false)));

        Assert.Equal(3, plan.Steps.Count);
        AssertMemoryStep(plan.Steps[0], retrieve.StageId, "memory.retrieve", SideEffectLevel.ReadOnly, requiresHumanGate: false, reversible: true);
        AssertMemoryStep(plan.Steps[1], form.StageId, "memory.form", SideEffectLevel.ExternalMutation, requiresHumanGate: true, reversible: false);
        AssertMemoryStep(plan.Steps[2], supersede.StageId, "memory.supersede", SideEffectLevel.ExternalMutation, requiresHumanGate: true, reversible: false);
        var retrieveStep = Assert.IsType<ModuleCapabilityStep>(plan.Steps[0]);
        Assert.Equal("retrieve memory objective", retrieveStep.InputEnvelope.GetProperty("queryText").StringValue);
    }

    [Fact]
    public async Task DefaultInterruptGraphUsesHostInteractionStepWithoutCoreLoopShell()
    {
        var intent = CreateInterruptIntent();
        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));
        var validator = new KernelValidator();
        var interpreter = new StageGraphInterpreter();

        var validation = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));
        var plan = await interpreter.InterpretAsync(
            graph,
            new KernelInterpreterContext(
                intent,
                new KernelRunState(new KernelRunId("run-interrupt"), intent.IntentId, selectedGraphId: graph.GraphId),
                new KernelRunOptions(runId: new KernelRunId("run-interrupt"), requireHumanGate: false)));

        Assert.True(validation.IsApproved);
        Assert.Equal("graph.interrupt.default", graph.GraphId.Value);
        Assert.Equal(["interrupt-host"], graph.Stages.Select(static stage => stage.Kind).ToArray());
        Assert.DoesNotContain(graph.Stages, stage => string.Equals(stage.Kind, "core_loop", StringComparison.Ordinal));
        Assert.Equal(SideEffectLevel.HostMutation, graph.Policies.MaxSideEffectLevel);
        var stage = Assert.Single(graph.Stages);
        Assert.Equal(SideEffectLevel.HostMutation, stage.SideEffectLevel);
        var step = Assert.Single(plan.Steps);
        var hostStep = Assert.IsType<HostInteractionStep>(step);
        Assert.Equal(RuntimeStepKind.HostInteraction, hostStep.StepKind);
        Assert.Equal("interrupt.cancel_tail_stream", hostStep.InteractionKind);
        Assert.Equal("user.cancel", hostStep.InteractionEnvelope.GetProperty("interruptReason").GetString());
        Assert.Equal(SideEffectLevel.HostMutation, hostStep.SideEffect.Level);
        Assert.True(hostStep.SideEffect.RequiresAudit);
        Assert.False(hostStep.Permission.RequiresHumanGate);
    }

    [Fact]
    public async Task DefaultResumeGraphUsesHostInteractionStepWithoutCoreLoopShell()
    {
        var intent = CreateResumeIntent();
        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));
        var validator = new KernelValidator();
        var interpreter = new StageGraphInterpreter();

        var validation = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));
        var plan = await interpreter.InterpretAsync(
            graph,
            new KernelInterpreterContext(
                intent,
                new KernelRunState(new KernelRunId("run-resume"), intent.IntentId, selectedGraphId: graph.GraphId),
                new KernelRunOptions(runId: new KernelRunId("run-resume"), requireHumanGate: false)));

        Assert.True(validation.IsApproved);
        Assert.Equal("graph.resume.default", graph.GraphId.Value);
        Assert.Equal(["resume-host"], graph.Stages.Select(static stage => stage.Kind).ToArray());
        Assert.DoesNotContain(graph.Stages, stage => string.Equals(stage.Kind, "core_loop", StringComparison.Ordinal));
        Assert.Equal(SideEffectLevel.ReadOnly, graph.Policies.MaxSideEffectLevel);
        var stage = Assert.Single(graph.Stages);
        Assert.Equal(SideEffectLevel.ReadOnly, stage.SideEffectLevel);
        var step = Assert.Single(plan.Steps);
        var hostStep = Assert.IsType<HostInteractionStep>(step);
        Assert.Equal(RuntimeStepKind.HostInteraction, hostStep.StepKind);
        Assert.Equal("resume.from_checkpoint", hostStep.InteractionKind);
        Assert.Equal("resume-token-001", hostStep.InteractionEnvelope.GetProperty("resumeToken").GetString());
        Assert.Equal("checkpoint://run/001/stage/model-reason", hostStep.InteractionEnvelope.GetProperty("checkpointRef").GetString());
        Assert.Equal(SideEffectLevel.ReadOnly, hostStep.SideEffect.Level);
        Assert.True(hostStep.SideEffect.RequiresAudit);
        Assert.False(hostStep.Permission.RequiresHumanGate);
    }

    [Fact]
    public void ResumeIntentRejectsMissingCheckpointBeforeGraphCreation()
    {
        Assert.Throws<ArgumentException>(() => new ResumeIntent(
            new CoreIntentId("intent-resume-invalid"),
            new KernelSubjectRef(new SessionId("session-001"), new ThreadId("thread-001")),
            CreateHostGovernance(),
            "resume-token-001",
            " "));
    }

    [Fact]
    public async Task ValidatorRejectsGraphWithoutTerminalStage()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();
        var graph = CreateCyclicGraph(intent);

        var result = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code is "kernel.graph.missing_terminal" or "kernel.graph.unbounded_cycle");
    }

    [Fact]
    public async Task ValidatorRejectsCyclicGraphWithoutToolCallBudgetEvenWhenRecoveryIsEnabled()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();
        var graph = CreateCyclicGraphWithTerminalButNoToolBudget(intent);

        var result = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.graph.unbounded_cycle");
    }

    [Fact]
    public async Task ValidatorRejectsUnauthorizedCapabilityTool()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();
        var graph = CreateGraphWithUnauthorizedTool(intent);

        var result = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.stage.capability_tool_not_allowed");
    }

    [Fact]
    public async Task ValidatorRejectsGraphPolicyOutsideGovernanceEnvelope()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent(maxSideEffectLevel: SideEffectLevel.ReadOnly);
        var graph = CreateGraphWithPolicies(
            intent,
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedKernelToolIds: new[] { "kernel.request_capability_call" },
                allowedCapabilityToolIds: new[] { "module.core_loop" },
                maxSideEffectLevel: SideEffectLevel.ExternalMutation,
                requiresHumanGate: false));

        var result = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.graph.side_effect_exceeds_governance");
    }

    [Fact]
    public async Task ValidatorRejectsGraphToolOutsideGovernanceEnvelope()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent(allowedToolIds: ["module.core_loop"]);
        var graph = CreateGraphWithPolicies(
            intent,
            new GraphPolicySet(
                PolicyEnforcementMode.AllowListed,
                allowedKernelToolIds: new[] { "kernel.request_capability_call" },
                allowedCapabilityToolIds: new[] { "module.core_loop" },
                maxSideEffectLevel: SideEffectLevel.ReadOnly,
                requiresHumanGate: false));

        var result = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.graph.kernel_tool_not_in_governance");
    }

    [Fact]
    public async Task ValidatorRejectsFailClosedModelRoutePolicyWithoutCandidate()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();
        var graph = CreateGraphWithModelRoutePolicy(intent, new ModelRoutePolicy());

        var result = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.stage.model_route_missing_candidate");
    }

    [Fact]
    public async Task ValidatorRejectsModelRoutePolicyWithMissingPreferredCandidate()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();
        var graph = CreateGraphWithModelRoutePolicy(intent, new ModelRoutePolicy(
            preferredRouteId: "candidate-missing",
            candidates: new[]
            {
                new ModelRouteCandidateBinding("candidate-present", "provider.module", "provider-key", "model-name"),
            }));

        var result = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.stage.model_route_preferred_candidate_missing");
    }

    [Fact]
    public async Task ValidatorRejectsFailClosedContextPolicyWithoutBudget()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();
        var graph = CreateGraphWithContextPolicy(intent, new ContextPolicy(maxInputTokens: 0, failClosed: true));

        var result = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.stage.context_policy_missing_budget");
    }

    [Fact]
    public async Task ValidatorRejectsContextPolicyWithUnspecifiedSourceKind()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();
        var graph = CreateGraphWithContextPolicy(intent, new ContextPolicy(
            maxInputTokens: 128,
            allowedSourceKinds: new[] { ContextSourceKind.Unspecified.ToString() }));

        var result = await validator.ValidateGraphAsync(graph, new KernelValidationContext(intent, graph: graph));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.stage.context_policy_unspecified_source");
    }

    [Fact]
    public async Task ValidatorRejectsOperationOutsideStageAllowList()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();
        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));
        var stage = Assert.Single(graph.Stages, candidate => candidate.StageId == new StageId("stage.prepare-context"));
        var operation = new RequestCapabilityCallOperation(
            new KernelOperationId("operation-unauthorized"),
            intent.IntentId,
            stage.StageId,
            "tool.shell",
            StructuredValue.Null,
            new PermissionEnvelope(new[] { "workspace.write" }),
            new SideEffectProfile(SideEffectLevel.WorkspaceWrite));

        var result = await validator.ValidateOperationAsync(operation, new KernelValidationContext(intent, graph: graph, stage: stage));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.operation.side_effect_exceeds_stage" || issue.Code == "kernel.operation.capability_not_allowed");
    }

    [Fact]
    public async Task ValidatorRejectsRuntimeStepOutsideGovernanceEnvelope()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent(maxSideEffectLevel: SideEffectLevel.ReadOnly);
        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));
        var stage = Assert.Single(graph.Stages, candidate => candidate.StageId == new StageId("stage.prepare-context"));
        var step = new ModuleCapabilityStep(
            "step-outside-governance",
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            new KernelOperationId("operation-001"),
            "kernel.default",
            "core_loop",
            StructuredValue.Null,
            new PermissionEnvelope(requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.ExternalMutation),
            stage.Budget,
            stage.OutputContract,
            new TracePolicy());

        var result = await validator.ValidateRuntimeStepAsync(step, new KernelValidationContext(intent, graph: graph, stage: stage));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.runtime_step.side_effect_exceeds_graph" || issue.Code == "kernel.runtime_step.side_effect_exceeds_governance");
    }

    [Fact]
    public async Task ValidatorRejectsRuntimeStepThatRequiresHumanGateWithoutApproval()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent(requiresHumanGate: true, approvalIds: Array.Empty<ApprovalId>());
        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: true));
        var stage = Assert.Single(graph.Stages, candidate => candidate.StageId == new StageId("stage.prepare-context"));
        var step = new ModuleCapabilityStep(
            "step-human-gate-missing-approval",
            intent.IntentId,
            graph.GraphId,
            stage.StageId,
            new KernelOperationId("operation-human-gate"),
            "kernel.default",
            "prepare_context",
            StructuredValue.Null,
            new PermissionEnvelope(requiresHumanGate: true),
            new SideEffectProfile(SideEffectLevel.ReadOnly),
            stage.Budget,
            stage.OutputContract,
            new TracePolicy());

        var result = await validator.ValidateRuntimeStepAsync(step, new KernelValidationContext(intent, graph: graph, stage: stage));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.runtime_step.missing_approval");
    }

    [Fact]
    public async Task ValidatorRejectsMissingRuntimeStep()
    {
        var validator = new KernelValidator();
        var intent = CreateIntent();

        var result = await validator.ValidateRuntimeStepAsync(null!, new KernelValidationContext(intent));

        Assert.Equal(KernelValidationDecision.Rejected, result.Decision);
        Assert.Contains(result.Issues, issue => issue.Code == "kernel.runtime_step.missing");
    }

    [Fact]
    public void RuntimeStepConstructorRejectsMissingSourceIds()
    {
        var intent = CreateIntent();
        var graph = DefaultKernelStageGraphs.CreateForIntent(intent, new KernelRunOptions(requireHumanGate: false));
        var stage = Assert.Single(graph.Stages, candidate => candidate.StageId == new StageId("stage.prepare-context"));

        Assert.Throws<ArgumentException>(() => new ModuleCapabilityStep(
            "step-invalid",
            intent.IntentId,
            new StageGraphId(" "),
            stage.StageId,
            new KernelOperationId("operation-001"),
            "kernel.default",
            "core_loop",
            StructuredValue.Null,
            new PermissionEnvelope(),
            new SideEffectProfile(SideEffectLevel.ReadOnly),
            stage.Budget,
            stage.OutputContract,
            new TracePolicy()));
    }

    private static TurnIntent CreateIntent(
        IReadOnlyList<string>? allowedToolIds = null,
        IReadOnlyList<string>? allowedModuleIds = null,
        SideEffectLevel maxSideEffectLevel = SideEffectLevel.ExternalNetwork,
        bool requiresHumanGate = false,
        IReadOnlyList<ApprovalId>? approvalIds = null)
        => new(
            new CoreIntentId("intent-001"),
            new KernelSubjectRef(new SessionId("session-001"), new ThreadId("thread-001")),
            new GovernanceEnvelope(
                "governance-001",
                allowedToolIds: allowedToolIds ?? DefaultTurnAllowedToolIds(),
                allowedModuleIds: allowedModuleIds ?? ["kernel.default", "provider.default"],
                maxSideEffectLevel: maxSideEffectLevel,
                requiresHumanGate: requiresHumanGate,
                approvalIds: approvalIds),
            "input-001",
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, retryBudget: 1, toolCallBudget: 1));

    private static string[] DefaultTurnAllowedToolIds()
        =>
        [
            "update_context_policy",
            "request_capability_call",
            "module.core_loop",
            "read_file",
            "list_dir",
            "grep",
            "glob",
            "apply_patch",
            "write",
            "memory_search",
            "artifacts",
        ];

    private static InterruptIntent CreateInterruptIntent()
        => new(
            new CoreIntentId("intent-interrupt-001"),
            new KernelSubjectRef(new SessionId("session-001"), new ThreadId("thread-001"), turnId: new TurnId("turn-001")),
            CreateHostGovernance(),
            "user.cancel",
            new KernelBudget(tokenBudget: 128, timeBudgetMs: 1_000, retryBudget: 1));

    private static ResumeIntent CreateResumeIntent()
        => new(
            new CoreIntentId("intent-resume-001"),
            new KernelSubjectRef(new SessionId("session-001"), new ThreadId("thread-001"), turnId: new TurnId("turn-001")),
            CreateHostGovernance(),
            "resume-token-001",
            "checkpoint://run/001/stage/model-reason",
            new KernelBudget(tokenBudget: 128, timeBudgetMs: 1_000, retryBudget: 1));

    private static GovernanceEnvelope CreateHostGovernance()
        => new(
            "governance-host-001",
            allowedToolIds: Array.Empty<string>(),
            allowedModuleIds: Array.Empty<string>(),
            maxSideEffectLevel: SideEffectLevel.HostMutation,
            requiresHumanGate: false);

    private static StageGraph CreateCyclicGraph(CoreIntent intent)
    {
        var first = CreateStage("stage-001", "module.core_loop");
        var second = CreateStage("stage-002", "module.core_loop");
        return new StageGraph(
            new StageGraphId("graph-cyclic"),
            "1",
            intent.IntentKind,
            first.StageId,
            new[] { first, second },
            new[]
            {
                new StageEdge(new StageEdgeId("edge-001"), first.StageId, second.StageId, new TransitionCondition("always"), new TransitionGuard(), StageTransitionKind.Success),
                new StageEdge(new StageEdgeId("edge-002"), second.StageId, first.StageId, new TransitionCondition("always"), new TransitionGuard(), StageTransitionKind.Success),
            },
            new GraphPolicySet(PolicyEnforcementMode.AllowListed, allowedCapabilityToolIds: new[] { "module.core_loop" }, maxSideEffectLevel: SideEffectLevel.ReadOnly, requiresHumanGate: false),
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, toolCallBudget: 1),
            new CheckpointRules(),
            new RecoveryRules(enabled: false),
            new EvaluationRules(),
            new StageGraphMetadata("test", "cyclic"));
    }

    private static StageGraph CreateCyclicGraphWithTerminalButNoToolBudget(CoreIntent intent)
    {
        var first = CreateStage("stage-001", "module.core_loop");
        var second = CreateStage("stage-002", "module.core_loop");
        var terminal = CreateStage("stage-003", "module.core_loop");
        return new StageGraph(
            new StageGraphId("graph-cyclic-no-tool-budget"),
            "1",
            intent.IntentKind,
            first.StageId,
            new[] { first, second, terminal },
            new[]
            {
                new StageEdge(new StageEdgeId("edge-001"), first.StageId, second.StageId, new TransitionCondition("always"), new TransitionGuard(), StageTransitionKind.Success),
                new StageEdge(new StageEdgeId("edge-002"), second.StageId, first.StageId, new TransitionCondition("loop"), new TransitionGuard(), StageTransitionKind.Conditional),
                new StageEdge(new StageEdgeId("edge-003"), second.StageId, terminal.StageId, new TransitionCondition("done"), new TransitionGuard(), StageTransitionKind.Success),
            },
            new GraphPolicySet(PolicyEnforcementMode.AllowListed, allowedCapabilityToolIds: new[] { "module.core_loop" }, maxSideEffectLevel: SideEffectLevel.ReadOnly, requiresHumanGate: false),
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, toolCallBudget: 0),
            new CheckpointRules(),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1),
            new EvaluationRules(),
            new StageGraphMetadata("test", "cyclic-no-tool-budget"));
    }

    private static StageGraph CreateGraphWithUnauthorizedTool(CoreIntent intent)
    {
        var stage = CreateStage("stage-001", "tool.shell");
        return new StageGraph(
            new StageGraphId("graph-unauthorized"),
            "1",
            intent.IntentKind,
            stage.StageId,
            new[] { stage },
            Array.Empty<StageEdge>(),
            new GraphPolicySet(PolicyEnforcementMode.AllowListed, allowedCapabilityToolIds: new[] { "module.core_loop" }, maxSideEffectLevel: SideEffectLevel.ReadOnly, requiresHumanGate: false),
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, toolCallBudget: 1),
            new CheckpointRules(),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1),
            new EvaluationRules(),
            new StageGraphMetadata("test", "unauthorized"));
    }

    private static StageGraph CreateGraphWithModelRoutePolicy(CoreIntent intent, ModelRoutePolicy modelRoutePolicy)
    {
        var stage = CreateStage("stage-001", "module.core_loop", modelRoutePolicy: modelRoutePolicy);
        return new StageGraph(
            new StageGraphId("graph-model-route"),
            "1",
            intent.IntentKind,
            stage.StageId,
            new[] { stage },
            Array.Empty<StageEdge>(),
            new GraphPolicySet(PolicyEnforcementMode.AllowListed, allowedCapabilityToolIds: new[] { "module.core_loop" }, maxSideEffectLevel: SideEffectLevel.ReadOnly, requiresHumanGate: false),
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, toolCallBudget: 1),
            new CheckpointRules(),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1),
            new EvaluationRules(),
            new StageGraphMetadata("test", "model-route"));
    }

    private static StageGraph CreateGraphWithContextPolicy(CoreIntent intent, ContextPolicy contextPolicy)
    {
        var stage = CreateStage("stage-001", "module.core_loop", contextPolicy);
        return new StageGraph(
            new StageGraphId("graph-context-policy"),
            "1",
            intent.IntentKind,
            stage.StageId,
            new[] { stage },
            Array.Empty<StageEdge>(),
            new GraphPolicySet(PolicyEnforcementMode.AllowListed, allowedCapabilityToolIds: new[] { "module.core_loop" }, maxSideEffectLevel: SideEffectLevel.ReadOnly, requiresHumanGate: false),
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, toolCallBudget: 1),
            new CheckpointRules(),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1),
            new EvaluationRules(),
            new StageGraphMetadata("test", "context-policy"));
    }

    private static StageGraph CreateGraphWithPolicies(CoreIntent intent, GraphPolicySet policies)
    {
        var stage = CreateStage("stage-001", "module.core_loop");
        return new StageGraph(
            new StageGraphId("graph-policies"),
            "1",
            intent.IntentKind,
            stage.StageId,
            new[] { stage },
            Array.Empty<StageEdge>(),
            policies,
            new KernelBudget(tokenBudget: 1_024, timeBudgetMs: 10_000, toolCallBudget: 1),
            new CheckpointRules(),
            new RecoveryRules(enabled: true, maxRecoveryAttempts: 1),
            new EvaluationRules(),
            new StageGraphMetadata("test", "policies"));
    }

    private static StageNode CreateStage(string stageId, string capabilityToolId, ContextPolicy? contextPolicy = null, ModelRoutePolicy? modelRoutePolicy = null)
        => new(
            new StageId(stageId),
            "core_loop",
            "test stage",
            new ContractRef("input", "1"),
            new ContractRef("output", "1"),
            Array.Empty<string>(),
            new[] { capabilityToolId },
            modelRoutePolicy ?? new ModelRoutePolicy(new[] { "route.default" }, "route.default"),
            contextPolicy ?? new ContextPolicy(maxInputTokens: 128),
            SideEffectLevel.ReadOnly,
            new KernelBudget(tokenBudget: 128, timeBudgetMs: 1_000, toolCallBudget: 1),
            new SuccessCriteria(new[] { "ok" }),
            new FailureHandlerRef("fail"));

    private static StageNode CreateMemoryStage(string stageId, string kind, SideEffectLevel sideEffectLevel)
        => new(
            new StageId(stageId),
            kind,
            kind switch
            {
                "memory-retrieve" => "retrieve memory objective",
                "memory-form" => "form memory objective",
                _ => "supersede memory objective",
            },
            new ContractRef("memory.input", "1"),
            new ContractRef("memory.output", "1"),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new ModelRoutePolicy(new[] { "route.default" }, "route.default"),
            new ContextPolicy(maxInputTokens: 128),
            sideEffectLevel,
            new KernelBudget(tokenBudget: 128, timeBudgetMs: 1_000, toolCallBudget: 1),
            new SuccessCriteria(new[] { "ok" }),
            new FailureHandlerRef("fail"));

    private static IReadOnlyList<StrategyTransitionEvidence> CreateStrategyEvidence(bool humanApproved)
        =>
        [
            new StrategyTransitionEvidence(
                new KernelRunId("run-001"),
                new KernelTraceId("trace-001"),
                "evidence://promotion/001",
                new[] { "success", "replay_compatible" },
                humanApproved),
        ];

    private static void AssertMemoryStep(
        RuntimeStep step,
        StageId stageId,
        string capabilityId,
        SideEffectLevel sideEffectLevel,
        bool requiresHumanGate,
        bool reversible)
    {
        var moduleStep = Assert.IsType<ModuleCapabilityStep>(step);
        Assert.Equal(stageId, moduleStep.SourceStageId);
        Assert.Equal("memory.identity", moduleStep.ModuleId);
        Assert.Equal(capabilityId, moduleStep.CapabilityId);
        Assert.Equal([capabilityId], moduleStep.Permission.Scopes);
        Assert.Equal(requiresHumanGate, moduleStep.Permission.RequiresHumanGate);
        Assert.Equal(sideEffectLevel, moduleStep.SideEffect.Level);
        Assert.Equal(["memory"], moduleStep.SideEffect.AffectedResources);
        Assert.Equal(reversible, moduleStep.SideEffect.Reversible);
        Assert.True(moduleStep.SideEffect.RequiresAudit);
        Assert.Equal(capabilityId, moduleStep.InputEnvelope.GetProperty("memoryCapabilityId").StringValue);
    }
}
