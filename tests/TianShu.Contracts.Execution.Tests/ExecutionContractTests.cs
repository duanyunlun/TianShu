using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Tools;

namespace TianShu.Contracts.Execution.Tests;

public sealed class ExecutionContractTests
{
    [Fact]
    public void ExecutionAttempt_RejectsNonPositiveAttemptNumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionAttempt(
            0,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExecutionRequest_PreservesTypedContext()
    {
        var context = CreateContext();
        var request = new ExecutionRequest(
            new ExecutionId("execution-001"),
            ExecutionKind.ProviderTurn,
            "execute",
            context,
            StructuredValue.FromString("payload"));

        Assert.Equal(new ExecutionId("execution-001"), request.ExecutionId);
        Assert.Equal(context, request.Context);
        Assert.Equal(StructuredValueKind.String, request.Input?.Kind);
    }

    [Fact]
    public void ExecutionBlocked_PreservesResumeHint()
    {
        var hint = new ResumeHint(new CallId("call-001"), "等待审批");
        var blocked = new ExecutionBlocked(
            new ExecutionId("execution-002"),
            new CallId("call-001"),
            "approval-required",
            resumeHint: hint);

        Assert.Equal(hint, blocked.ResumeHint);
        Assert.Equal("approval-required", blocked.Reason);
    }

    [Fact]
    public void ExecutionStarted_ExposesTypedEventSurface()
    {
        var startedAt = DateTimeOffset.Parse("2026-04-08T10:00:00+00:00");
        var payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["status"] = "running",
        });

        IExecutionEvent @event = new ExecutionStarted(
            new ExecutionId("execution-003"),
            1,
            startedAt,
            "execution started",
            payload);

        Assert.Equal(ExecutionEventKind.Started, @event.Kind);
        Assert.Equal(startedAt, @event.Timestamp);
        Assert.Equal("execution started", @event.Message);
        Assert.Equal("running", Assert.IsType<Dictionary<string, object?>>(@event.Data!.ToPlainObject())["status"]);
    }

    [Fact]
    public void ExecutionFailed_ProjectsFailureDetailsToEventSurface()
    {
        var occurredAt = DateTimeOffset.Parse("2026-04-08T10:01:00+00:00");
        var details = StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["reason"] = "timeout",
        });
        var failure = new ExecutionFailure("timeout", "执行超时", details: details);

        IExecutionEvent @event = new ExecutionFailed(
            new ExecutionId("execution-004"),
            failure,
            1,
            occurredAt);

        Assert.Equal(ExecutionEventKind.Failed, @event.Kind);
        Assert.Equal(occurredAt, @event.Timestamp);
        Assert.Equal("执行超时", @event.Message);
        Assert.Equal("timeout", Assert.IsType<Dictionary<string, object?>>(@event.Data!.ToPlainObject())["reason"]);
    }

    [Fact]
    public void ControlPlaneCommandExecutionContracts_PreserveTypedPayload()
    {
        var start = new ControlPlaneCommandExecutionStartCommand
        {
            WorkingDirectory = "D:/workspace",
            CommandArgs = new[] { "pwsh", "-NoLogo" },
            Tty = true,
            Size = new ControlPlaneCommandExecutionTerminalSize
            {
                Rows = 40,
                Cols = 120,
            },
            ThreadId = new ThreadId("thread-exec-001"),
            TurnId = new TurnId("turn-exec-001"),
            ItemId = "item-exec-001",
            ApprovalPolicy = "on-request",
            EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["FOO"] = "bar",
            },
            Sandbox = StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mode"] = "workspace-write",
            }),
        };

        var write = new ControlPlaneCommandExecutionWriteCommand
        {
            ProcessId = "proc-exec-001",
            DeltaBase64 = "AQID",
            CloseStdin = true,
        };
        var terminate = new ControlPlaneCommandExecutionTerminateCommand
        {
            ProcessId = "proc-exec-001",
        };
        var resize = new ControlPlaneCommandExecutionResizeCommand
        {
            ProcessId = "proc-exec-001",
            Size = new ControlPlaneCommandExecutionTerminalSize
            {
                Rows = 50,
                Cols = 160,
            },
        };
        var result = new ControlPlaneCommandExecutionResult
        {
            Started = true,
            ProcessId = "proc-exec-001",
            Pid = 1001,
            Stdout = "hello",
        };
        var accepted = new ControlPlaneCommandExecutionCommandAcceptedResult();

        Assert.Equal(new ThreadId("thread-exec-001"), start.ThreadId);
        Assert.Equal(new TurnId("turn-exec-001"), start.TurnId);
        Assert.Equal("workspace-write", Assert.IsType<Dictionary<string, object?>>(start.Sandbox!.ToPlainObject())["mode"]);
        Assert.Equal("AQID", write.DeltaBase64);
        Assert.True(write.CloseStdin);
        Assert.Equal("proc-exec-001", terminate.ProcessId);
        Assert.Equal((ushort)160, resize.Size.Cols);
        Assert.True(result.Started);
        Assert.Equal(1001, result.Pid);
        Assert.NotNull(accepted);
    }

    [Fact]
    public void ControlPlaneCodeModeContracts_PreserveTypedPayload()
    {
        var exec = new ControlPlaneCodeModeExecCommand
        {
            ThreadId = new ThreadId("thread-code-001"),
            Input = "print('hi')",
            YieldTimeMs = 250,
            MaxOutputTokens = 64,
        };
        var wait = new ControlPlaneCodeModeWaitCommand
        {
            ThreadId = new ThreadId("thread-code-001"),
            CellId = "cell-code-001",
            YieldTimeMs = 400,
            MaxTokens = 32,
            Terminate = true,
        };
        var result = new ControlPlaneCodeModeResult
        {
            Success = true,
            Status = "running",
            ThreadId = new ThreadId("thread-code-001"),
            TurnId = new TurnId("turn-code-001"),
            CellId = "cell-code-001",
            Output = "partial output",
            ContentItems = new[]
            {
                new ControlPlaneCodeModeOutputItem
                {
                    Type = "input_text",
                    Text = "partial output",
                },
            },
        };

        Assert.Equal(new ThreadId("thread-code-001"), exec.ThreadId);
        Assert.Equal("print('hi')", exec.Input);
        Assert.Equal("cell-code-001", wait.CellId);
        Assert.True(wait.Terminate);
        Assert.True(result.Success);
        Assert.Equal(new TurnId("turn-code-001"), result.TurnId);
        Assert.Equal("partial output", Assert.Single(result.ContentItems).Text);
    }

    [Fact]
    public void RuntimeStep_RequiresKernelSourceIdsAndPolicyEnvelope()
    {
        var common = CreateRuntimeStepInputs();

        Assert.Throws<ArgumentException>(() => new ToolInvocationStep(
            "step-tool-001",
            common.IntentId,
            default,
            common.StageId,
            common.OperationId,
            "tool.shell",
            common.ToolEnvelope,
            common.Permission,
            common.SideEffect,
            common.Budget,
            common.Contract,
            common.TracePolicy));

        var step = new ToolInvocationStep(
            "step-tool-001",
            common.IntentId,
            common.GraphId,
            common.StageId,
            common.OperationId,
            "tool.shell",
            common.ToolEnvelope,
            common.Permission,
            common.SideEffect,
            common.Budget,
            common.Contract,
            common.TracePolicy);

        Assert.Equal(RuntimeStepKind.ToolInvocation, step.StepKind);
        Assert.Equal(common.IntentId, step.SourceIntentId);
        Assert.Equal(common.GraphId, step.SourceGraphId);
        Assert.Equal(common.StageId, step.SourceStageId);
        Assert.Equal(common.OperationId, step.SourceKernelOperationId);
        Assert.Equal(SideEffectLevel.ReadOnly, step.SideEffect.Level);
    }

    [Fact]
    public void ExecutionPlan_PreservesApprovedRuntimeSteps()
    {
        var common = CreateRuntimeStepInputs();
        var step = new ModelInvocationStep(
            "step-model-001",
            common.IntentId,
            common.GraphId,
            common.StageId,
            common.OperationId,
            "provider.openai",
            new ModelRoutePolicy(["route.default"], "route.default"),
            common.ProviderRequest,
            common.Permission,
            common.SideEffect,
            common.Budget,
            common.Contract,
            common.TracePolicy);

        var plan = new ExecutionPlan(
            "plan-001",
            common.GraphId,
            common.IntentId,
            [step],
            new ExecutionPlanPolicy(),
            common.TracePolicy);
        var result = new ExecutionRunResult(
            "plan-001",
            new ExecutionId("execution-plan-001"),
            RuntimeStepResultStatus.Succeeded,
            [new RuntimeStepResult(step.StepId, step.StepKind, RuntimeStepResultStatus.Succeeded, traceRef: "trace-001")],
            traceRef: "trace-run-001");

        Assert.Equal("plan-001", plan.PlanId);
        Assert.Single(plan.Steps);
        Assert.True(plan.Policy.StopOnFailure);
        Assert.Equal("trace-run-001", result.TraceRef);
        Assert.Equal(RuntimeStepResultStatus.Succeeded, Assert.Single(result.StepResults).Status);
    }

    [Fact]
    public void RuntimeStepConcreteTypes_ExposeAllRequiredKinds()
    {
        var common = CreateRuntimeStepInputs();
        RuntimeStep[] steps =
        [
            new StateCommitStep("step-state", common.IntentId, common.GraphId, common.StageId, common.OperationId, "state.thread", StructuredValue.FromString("commit"), common.Permission, common.SideEffect, common.Budget, common.Contract, common.TracePolicy),
            new ArtifactStep("step-artifact", common.IntentId, common.GraphId, common.StageId, common.OperationId, "publish", StructuredValue.FromString("artifact"), common.Permission, common.SideEffect, common.Budget, common.Contract, common.TracePolicy),
            new DiagnosticStep("step-diagnostic", common.IntentId, common.GraphId, common.StageId, common.OperationId, "runtime", StructuredValue.FromString("diagnostic"), common.Permission, common.SideEffect, common.Budget, common.Contract, common.TracePolicy),
            new HostInteractionStep("step-host", common.IntentId, common.GraphId, common.StageId, common.OperationId, "input", StructuredValue.FromString("question"), common.Permission, common.SideEffect, common.Budget, common.Contract, common.TracePolicy),
            new ModuleCapabilityStep("step-module", common.IntentId, common.GraphId, common.StageId, common.OperationId, "memory", "query", StructuredValue.FromString("query"), common.Permission, common.SideEffect, common.Budget, common.Contract, common.TracePolicy),
        ];

        Assert.Equal(
            [
                RuntimeStepKind.StateCommit,
                RuntimeStepKind.Artifact,
                RuntimeStepKind.Diagnostic,
                RuntimeStepKind.HostInteraction,
                RuntimeStepKind.ModuleCapability,
            ],
            steps.Select(static step => step.StepKind).ToArray());
    }

    private static ExecutionContext CreateContext()
    {
        var space = new CollaborationSpace(
            new CollaborationSpaceId("space-020"),
            "contracts",
            "Contracts",
            new CollaborationSpaceProfile("实现"),
            CollaborationDefaultSet.Empty);
        var participant = new ServiceParticipant(
            new ParticipantId("participant-020"),
            "Control Plane",
            "coordinator");
        var envelope = new InteractionEnvelope(
            new InteractionEnvelopeId("interaction-020"),
            new InteractionSource(InteractionSourceKind.Host, "cli"),
            new InteractionItem[]
            {
                new TextInteractionItem("继续"),
            });

        return new ExecutionContext(
            CollaborationSpaceRef.From(space),
            InteractionEnvelopeRef.From(envelope),
            ParticipantRef.From(participant),
            new ThreadId("thread-020"),
            new TurnId("turn-020"));
    }

    private static RuntimeStepInputs CreateRuntimeStepInputs()
    {
        var permission = new PermissionEnvelope(["runtime.execute"], requiresHumanGate: false);
        var sideEffect = new SideEffectProfile(SideEffectLevel.ReadOnly, ["workspace"], reversible: true);
        var providerContext = new ProviderInvocationContext(
            "step-model-001",
            "intent-rt-001",
            "graph-rt-001",
            "stage-rt-001",
            "operation-rt-001",
            permission,
            sideEffect);
        var providerRequest = new ProviderInvocationRequest(
            new ExecutionId("execution-provider-rt"),
            "openai",
            "gpt-5",
            new ProviderConversationContext(),
            [new TextProviderInputItem("hello")],
            invocationContext: providerContext);
        var toolEnvelope = new ToolInvocationEnvelope(
            new CallId("call-tool-rt"),
            "tool.shell",
            "run",
            StructuredValue.FromString("echo hi"),
            permission,
            sideEffect);

        return new RuntimeStepInputs(
            new CoreIntentId("intent-rt-001"),
            new StageGraphId("graph-rt-001"),
            new StageId("stage-rt-001"),
            new KernelOperationId("operation-rt-001"),
            permission,
            sideEffect,
            new KernelBudget(tokenBudget: 100, timeBudgetMs: 1_000, toolCallBudget: 1),
            new ContractRef("contract.output", "1"),
            new TracePolicy(),
            providerRequest,
            toolEnvelope);
    }

    private sealed record RuntimeStepInputs(
        CoreIntentId IntentId,
        StageGraphId GraphId,
        StageId StageId,
        KernelOperationId OperationId,
        PermissionEnvelope Permission,
        SideEffectProfile SideEffect,
        KernelBudget Budget,
        ContractRef Contract,
        TracePolicy TracePolicy,
        ProviderInvocationRequest ProviderRequest,
        ToolInvocationEnvelope ToolEnvelope);
}
