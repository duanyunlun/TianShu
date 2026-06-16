using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TianShu.Configuration;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Tools;
using TianShu.Execution.Runtime;
using TianShu.Kernel;
using TianShu.Kernel.Abstractions;
using TianShu.Provider.Abstractions;
using TianShu.RuntimeComposition;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelRuntimeExecutionLoopTests
{
    [Fact]
    public async Task RunAsync_ShouldExecuteFixedStageGraphThroughRealKernelAndRuntime()
    {
        var runId = new KernelRunId("run-fixed-live");
        var core = new StableKernelCore();
        var runtime = new TianShuExecutionRuntime();
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime);

        var result = await loop.RunAsync(
            CreateIntent(),
            new KernelRuntimeExecutionOptions(
                new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                new ExecutionRuntimeContext(
                    new ExecutionId("execution-fixed-live"),
                    runId,
                    CreateGovernance()),
                ExecuteRuntimePlan: true),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.True(result.KernelResult.Validation.IsApproved);
        Assert.NotNull(result.KernelResult.ExecutionPlan);
        Assert.NotNull(result.RuntimeResult);
        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.RuntimeResult!.Status);
        Assert.Equal(5, result.RuntimeResult.StepResults.Count);
        Assert.All(result.RuntimeResult.StepResults, stepResult => Assert.Equal(RuntimeStepResultStatus.Succeeded, stepResult.Status));
        Assert.Contains(result.RuntimeResult.StepResults, stepResult => stepResult.StepKind == RuntimeStepKind.ModuleCapability);
        Assert.Contains(result.RuntimeResult.StepResults, stepResult => stepResult.StepKind == RuntimeStepKind.ModelInvocation);
        Assert.Contains(result.RuntimeResult.StepResults, stepResult => stepResult.StepKind == RuntimeStepKind.ToolInvocation);
        Assert.Contains(result.RuntimeResult.StepResults, stepResult => stepResult.StepKind == RuntimeStepKind.StateCommit);
        Assert.Contains(result.RuntimeResult.StepResults, stepResult => stepResult.StepKind == RuntimeStepKind.Diagnostic);
        Assert.Equal(result.KernelResult.ExecutionPlan!.PlanId, result.RuntimeResult.PlanId);
        Assert.Equal(runId, result.KernelResult.RunId);
        Assert.StartsWith("trace://execution/execution-fixed-live/", result.RuntimeTraceRef, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldSkipToolStageWhenFakeModelReturnsFinalResponse()
    {
        var runId = new KernelRunId("run-reactive-final");
        var core = new StableKernelCore();
        var runtime = RecordingExecutionRuntime.Create(plan =>
            CreateStageRuntimeResult(
                plan,
                ResolveSignalsForStage(plan, modelSignals: ["model_final_response"])));
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime.Instance);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.Equal(
            ["stage.prepare-context", "stage.model-reason", "stage.finalize"],
            result.KernelResult.StageResults.Select(stage => stage.StageId.Value).ToArray());
        Assert.DoesNotContain(result.KernelResult.StageResults, stage => stage.StageId == new StageId("stage.tool-exec"));
        Assert.Equal(3, runtime.ExecuteCallCount);
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldDriveOneFakeToolRoundFromModelSignal()
    {
        var runId = new KernelRunId("run-reactive-tool");
        var core = new StableKernelCore();
        var modelCallCount = 0;
        var runtime = RecordingExecutionRuntime.Create(plan =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCallCount++;
                return CreateStageRuntimeResult(
                    plan,
                    modelCallCount == 1
                        ? CreateToolRequestOutput("call-read-file", "read_file", "read")
                        : StructuredValue.FromPlainObject(new Dictionary<string, object?>
                        {
                            ["signals"] = new[] { "model_final_response" },
                        }));
            }

            return stageId == "stage.tool-exec"
                ? CreateToolResultRuntimeResult(plan)
                : CreateStageRuntimeResult(plan, ResolveSignalsForStage(plan));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime.Instance);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.Equal(
            ["stage.prepare-context", "stage.model-reason", "stage.tool-exec", "stage.model-reason", "stage.finalize"],
            result.KernelResult.StageResults.Select(stage => stage.StageId.Value).ToArray());
        Assert.Equal(2, modelCallCount);
        Assert.Equal(5, runtime.ExecuteCallCount);
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldInjectTurnMessageAndSteerInputsIntoModelProviderInput()
    {
        var runId = new KernelRunId("run-reactive-steer-input");
        var runtime = RecordingExecutionRuntime.Create(plan =>
            CreateStageRuntimeResult(plan, ResolveSignalsForStage(plan, modelSignals: ["model_final_response"])));
        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime.Instance);

        var result = await loop.RunReactiveAsync(
            CreateIntent(
                metadata: new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["message"] = StructuredValue.FromString("用户原始输入"),
                    ["steerInputs"] = StructuredValue.FromPlainObject(new[] { "第一条 steer", "第二条 steer" }),
                })),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        var modelPlan = runtime.Plans.Single(plan => plan.Steps.Any(static step => step.SourceStageId == new StageId("stage.model-reason")));
        var modelStep = Assert.IsType<ModelInvocationStep>(modelPlan.Steps.Single());
        var textInputs = modelStep.InputEnvelope.Inputs.OfType<TextProviderInputItem>().Select(static item => item.Text).ToArray();
        Assert.Contains("用户原始输入", textInputs);
        Assert.Contains("第一条 steer", textInputs);
        Assert.Contains("第二条 steer", textInputs);
        Assert.Equal("3", modelStep.InputEnvelope.Metadata.Entries["productInput.count"].NumberValue);
        Assert.Equal("turn.message_and_steer", modelStep.InputEnvelope.Metadata.Entries["productInput.source"].StringValue);
    }

    [Fact]
    public async Task KernelRuntimeTurnLoopBridge_ShouldPersistProductEvidenceWhenWorkingDirectoryAvailable()
    {
        using var workspace = new TempWorkspace();
        var result = await KernelRuntimeTurnLoopBridge.RunAsync(
            new KernelRuntimeTurnLoopRequest(
                "验证产品终态投影",
                WorkingDirectory: workspace.Path,
                ResumeThreadId: null,
                TurnTimeoutSeconds: 5,
                Config: CreateConfiguredProviderConfig($"TIANSHU_TEST_MISSING_{Guid.NewGuid():N}")),
            CancellationToken.None);

        Assert.Equal("failed", result.TurnStatus);
        Assert.Equal("failed", result.TerminalProjection.TurnStatus);
        Assert.True(result.TerminalProjection.ThreadProjection.StableIdsAvailable);
        Assert.Equal(result.ThreadId, result.TerminalProjection.ThreadProjection.ThreadId);
        Assert.Equal(result.TurnId, result.TerminalProjection.ThreadProjection.TurnId);
        Assert.True(result.TerminalProjection.TurnLog.Available);
        Assert.Null(result.TerminalProjection.TurnLog.Reason);
        Assert.True(File.Exists(result.TerminalProjection.TurnLog.Reference));
        Assert.True(result.TerminalProjection.RolloutRecord.Available);
        Assert.Null(result.TerminalProjection.RolloutRecord.Reason);
        Assert.True(File.Exists(result.TerminalProjection.RolloutRecord.Reference));
        Assert.DoesNotContain("turn_log_not_migrated_23_6", result.TerminalProjection.DowngradeReasons);
        Assert.DoesNotContain("rollout_record_not_migrated_23_6", result.TerminalProjection.DowngradeReasons);
        Assert.Equal(result.ReplaySummary.Completeness, result.TerminalProjection.ReplayCompleteness);
        Assert.NotEmpty(result.TerminalProjection.RuntimeTraceRefs);
        Assert.NotEmpty(result.TerminalProjection.DiagnosticsRefs);
    }

    [Fact]
    public async Task KernelRuntimeTurnLoopBridge_RunInterruptAsync_ShouldUseHostInteractionAndProjectInterruptedDowngrade()
    {
        using var workspace = new TempWorkspace();
        var result = await KernelRuntimeTurnLoopBridge.RunInterruptAsync(
            new KernelRuntimeInterruptRequest(
                "thread-interrupt-001",
                "turn-interrupt-001",
                "user.cancel",
                WorkingDirectory: workspace.Path,
                Config: CreateConfiguredProviderConfig($"TIANSHU_TEST_MISSING_{Guid.NewGuid():N}")),
            CancellationToken.None);

        Assert.Equal("interrupt", result.Operation);
        Assert.Equal("interrupted", result.Status);
        Assert.Null(result.FailureCode);
        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.ExecutionResult!.Disposition);
        Assert.Equal(["stage.interrupt-host"], result.ReplaySummary!.StagePath);
        Assert.Contains(result.ReplaySummary.Steps, static step => step.StepKind == RuntimeStepKind.HostInteraction);
        var hostStep = Assert.IsType<HostInteractionStep>(Assert.Single(result.ExecutionResult.KernelResult.ExecutionPlan!.Steps));
        Assert.Equal(SideEffectLevel.HostMutation, hostStep.SideEffect.Level);
        Assert.True(hostStep.SideEffect.RequiresAudit);
        Assert.False(hostStep.Permission.RequiresHumanGate);
        Assert.Equal("interrupted", result.TerminalProjection.TurnStatus);
        Assert.True(result.TerminalProjection.TurnLog.Available);
        Assert.True(File.Exists(result.TerminalProjection.TurnLog.Reference));
        Assert.True(result.TerminalProjection.RolloutRecord.Available);
        Assert.True(File.Exists(result.TerminalProjection.RolloutRecord.Reference));
        Assert.False(result.TerminalProjection.ActiveRunCancellation.Available);
        Assert.Equal("active_run_not_found", result.TerminalProjection.ActiveRunCancellation.Reason);
        Assert.Contains("active_run_not_found", result.TerminalProjection.DowngradeReasons);
        Assert.DoesNotContain("active_run_registry_not_migrated_23_7", result.TerminalProjection.DowngradeReasons);
    }

    [Fact]
    public async Task KernelRuntimeTurnLoopBridge_RunInterruptAsync_ShouldCancelMatchingActiveRun()
    {
        using var workspace = new TempWorkspace();
        using var provider = new DelayedProviderServer();
        const string threadId = "thread-active-cancel-001";
        var envKey = $"TIANSHU_TEST_ACTIVE_CANCEL_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envKey, "test-api-key");
        try
        {
            var runTask = KernelRuntimeTurnLoopBridge.RunAsync(
                new KernelRuntimeTurnLoopRequest(
                    "等待 provider 以验证 active-run cancellation",
                    WorkingDirectory: workspace.Path,
                    ResumeThreadId: threadId,
                    TurnTimeoutSeconds: 30,
                    Config: CreateConfiguredProviderConfig(envKey, provider.BaseUrl)),
                CancellationToken.None);

            await provider.WaitForRequestAsync(TimeSpan.FromSeconds(10));
            Assert.True(File.Exists(BuildHostControlThreadIndexPath(workspace.Path, threadId)));

            var interrupt = await KernelRuntimeTurnLoopBridge.RunInterruptAsync(
                new KernelRuntimeInterruptRequest(
                    threadId,
                    TurnId: null,
                    Reason: "user.cancel",
                    WorkingDirectory: workspace.Path,
                    Config: CreateConfiguredProviderConfig($"TIANSHU_TEST_MISSING_{Guid.NewGuid():N}")),
                CancellationToken.None);
            var runResult = await runTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("interrupt", interrupt.Operation);
            Assert.Equal("interrupted", interrupt.Status);
            Assert.True(interrupt.TerminalProjection.ActiveRunCancellation.Available);
            Assert.Null(interrupt.TerminalProjection.ActiveRunCancellation.Reason);
            Assert.StartsWith("active-run://", interrupt.TerminalProjection.ActiveRunCancellation.Reference, StringComparison.Ordinal);
            Assert.DoesNotContain("active_run_registry_not_migrated_23_7", interrupt.TerminalProjection.DowngradeReasons);
            Assert.DoesNotContain("active_run_not_found", interrupt.TerminalProjection.DowngradeReasons);

            Assert.Equal("interrupted", runResult.TurnStatus);
            Assert.Equal("interrupted", runResult.TerminalProjection.TurnStatus);
            Assert.True(runResult.TerminalProjection.ActiveRunCancellation.Available);
            Assert.StartsWith("active-run://", runResult.TerminalProjection.ActiveRunCancellation.Reference, StringComparison.Ordinal);
            Assert.DoesNotContain("active_run_registry_not_migrated_23_7", runResult.TerminalProjection.DowngradeReasons);
            Assert.DoesNotContain("active_run_not_found", runResult.TerminalProjection.DowngradeReasons);
            Assert.False(File.Exists(BuildHostControlThreadIndexPath(workspace.Path, threadId)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }

    [Fact]
    public void KernelRuntimeHostControlFileStore_ShouldResolveActiveRunAndWriteCancelSignal()
    {
        using var workspace = new TempWorkspace();
        var record = KernelRuntimeHostControlFileStore.TryRegister(
            workspace.Path,
            "thread-cross-process-001",
            "turn-cross-process-001",
            "run-cross-process-001",
            "等待跨进程取消");

        Assert.NotNull(record);
        Assert.True(File.Exists(record.ThreadIndexPath));
        Assert.True(File.Exists(record.TurnIndexPath));

        var projection = KernelRuntimeHostControlFileStore.TryCancel(
            workspace.Path,
            "thread-cross-process-001",
            turnId: null,
            "user.cancel");

        Assert.NotNull(projection);
        Assert.True(projection.Available);
        Assert.Equal("active-run://thread-cross-process-001/turn-cross-process-001/run-cross-process-001", projection.Reference);
        Assert.Equal("thread-cross-process-001", projection.TargetThreadId);
        Assert.Equal("turn-cross-process-001", projection.TargetTurnId);
        Assert.Equal("run-cross-process-001", projection.TargetRunId);
        Assert.Equal("user.cancel", KernelRuntimeHostControlFileStore.TryReadCancellationReason(record.CancelSignalPath));

        KernelRuntimeHostControlFileStore.TryUnregister(record);
        Assert.False(File.Exists(record.ThreadIndexPath));
        Assert.False(File.Exists(record.TurnIndexPath));
        Assert.False(File.Exists(record.CancelSignalPath));
    }

    [Fact]
    public async Task KernelRuntimeTurnLoopBridge_RunResumeAsync_ShouldFailClosedWithoutCheckpoint()
    {
        var result = await KernelRuntimeTurnLoopBridge.RunResumeAsync(
            new KernelRuntimeResumeRequest(
                "thread-resume-001",
                "turn-resume-001",
                "resume-token-001",
                " ",
                WorkingDirectory: null,
                Config: CreateConfiguredProviderConfig($"TIANSHU_TEST_MISSING_{Guid.NewGuid():N}")),
            CancellationToken.None);

        Assert.Equal("resume", result.Operation);
        Assert.Equal("failed", result.Status);
        Assert.Equal("kernel_runtime_resume_checkpoint_missing", result.FailureCode);
        Assert.Null(result.ExecutionResult);
        Assert.False(result.TerminalProjection.CheckpointResume.Available);
        Assert.Equal("checkpoint_missing", result.TerminalProjection.CheckpointResume.Reason);
    }

    [Fact]
    public async Task KernelRuntimeTurnLoopBridge_RunResumeAsync_ShouldUseHostInteractionWhenCheckpointExists()
    {
        using var workspace = new TempWorkspace();
        var seed = await KernelRuntimeTurnLoopBridge.RunAsync(
            new KernelRuntimeTurnLoopRequest(
                "生成 resume checkpoint",
                WorkingDirectory: workspace.Path,
                ResumeThreadId: null,
                TurnTimeoutSeconds: 5,
                Config: CreateConfiguredProviderConfig($"TIANSHU_TEST_MISSING_{Guid.NewGuid():N}")),
            CancellationToken.None);
        var checkpointRef = KernelRuntimeHostControlFileStore.BuildTerminalCheckpointRef(seed.ThreadId, seed.TurnId);
        Assert.True(KernelRuntimeHostControlFileStore.TryEnqueuePendingSteers(
            workspace.Path,
            seed.ThreadId,
            turnId: seed.TurnId,
            ["恢复后继续遵循这条引导。"]));

        var result = await KernelRuntimeTurnLoopBridge.RunResumeAsync(
            new KernelRuntimeResumeRequest(
                seed.ThreadId,
                "turn-resume-002",
                "resume-token-002",
                checkpointRef,
                WorkingDirectory: workspace.Path,
                Config: CreateConfiguredProviderConfig($"TIANSHU_TEST_MISSING_{Guid.NewGuid():N}")),
            CancellationToken.None);

        Assert.Equal("resume", result.Operation);
        Assert.Equal("completed", result.Status);
        Assert.Null(result.FailureCode);
        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.ExecutionResult!.Disposition);
        Assert.Equal(["stage.resume-host"], result.ReplaySummary!.StagePath);
        Assert.Contains(result.ReplaySummary.Steps, static step => step.StepKind == RuntimeStepKind.HostInteraction);
        var hostStep = Assert.IsType<HostInteractionStep>(Assert.Single(result.ExecutionResult.KernelResult.ExecutionPlan!.Steps));
        Assert.Equal(SideEffectLevel.ReadOnly, hostStep.SideEffect.Level);
        Assert.True(hostStep.SideEffect.RequiresAudit);
        Assert.False(hostStep.Permission.RequiresHumanGate);
        Assert.True(result.TerminalProjection.CheckpointResume.Available);
        Assert.Equal(checkpointRef, result.TerminalProjection.CheckpointResume.Reference);
        Assert.True(result.TerminalProjection.Steer.Available);
        Assert.Equal("applied_to_model_input", result.TerminalProjection.Steer.Disposition);
        Assert.Equal(["恢复后继续遵循这条引导。"], result.TerminalProjection.Steer.Inputs);
    }

    [Fact]
    public async Task KernelRuntimeTurnLoopBridge_RunAsync_ShouldConsumePendingSteersFromHostControlStore()
    {
        using var workspace = new TempWorkspace();
        const string threadId = "thread-pending-steer-001";
        Assert.True(KernelRuntimeHostControlFileStore.TryEnqueuePendingSteers(
            workspace.Path,
            threadId,
            turnId: "turn-active-steer-001",
            ["请优先回答 OK。"]));

        var result = await KernelRuntimeTurnLoopBridge.RunAsync(
            new KernelRuntimeTurnLoopRequest(
                "执行带 pending steer 的下一轮",
                WorkingDirectory: workspace.Path,
                ResumeThreadId: threadId,
                TurnTimeoutSeconds: 5,
                Config: CreateConfiguredProviderConfig($"TIANSHU_TEST_MISSING_{Guid.NewGuid():N}")),
            CancellationToken.None);

        Assert.True(result.TerminalProjection.Steer.Available);
        Assert.Equal("applied_to_model_input", result.TerminalProjection.Steer.Disposition);
        Assert.Equal(["请优先回答 OK。"], result.TerminalProjection.Steer.Inputs);

        var second = await KernelRuntimeTurnLoopBridge.RunAsync(
            new KernelRuntimeTurnLoopRequest(
                "确认 pending steer 不重复消费",
                WorkingDirectory: workspace.Path,
                ResumeThreadId: threadId,
                TurnTimeoutSeconds: 5,
                Config: CreateConfiguredProviderConfig($"TIANSHU_TEST_MISSING_{Guid.NewGuid():N}")),
            CancellationToken.None);

        Assert.False(second.TerminalProjection.Steer.Available);
        Assert.Equal("not_requested", second.TerminalProjection.Steer.Disposition);
    }

    [Fact]
    public async Task KernelRuntimeTurnLoopBridge_RunAsync_ShouldAllowNewMainlineAfterInterrupt()
    {
        using var workspace = new TempWorkspace();
        using var provider = new DelayedProviderServer();
        const string threadId = "thread-interrupt-continue-001";
        var envKey = $"TIANSHU_TEST_INTERRUPT_CONTINUE_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envKey, "test-api-key");
        try
        {
            var oldRunTask = KernelRuntimeTurnLoopBridge.RunAsync(
                new KernelRuntimeTurnLoopRequest(
                    "旧方向：等待取消",
                    WorkingDirectory: workspace.Path,
                    ResumeThreadId: threadId,
                    TurnTimeoutSeconds: 30,
                    Config: CreateConfiguredProviderConfig(envKey, provider.BaseUrl)),
                CancellationToken.None);

            await provider.WaitForRequestAsync(TimeSpan.FromSeconds(10));
            _ = await KernelRuntimeTurnLoopBridge.RunInterruptAsync(
                new KernelRuntimeInterruptRequest(
                    threadId,
                    TurnId: null,
                    Reason: "user.cancel",
                    WorkingDirectory: workspace.Path,
                    Config: CreateConfiguredProviderConfig($"TIANSHU_TEST_MISSING_{Guid.NewGuid():N}")),
                CancellationToken.None);
            var oldRun = await oldRunTask.WaitAsync(TimeSpan.FromSeconds(10));

            var newRun = await KernelRuntimeTurnLoopBridge.RunAsync(
                new KernelRuntimeTurnLoopRequest(
                    "新方向：不要继续旧尾流",
                    WorkingDirectory: workspace.Path,
                    ResumeThreadId: threadId,
                    TurnTimeoutSeconds: 5,
                    Config: CreateConfiguredProviderConfig($"TIANSHU_TEST_MISSING_{Guid.NewGuid():N}")),
                CancellationToken.None);

            Assert.Equal("interrupted", oldRun.TurnStatus);
            Assert.NotEqual(oldRun.TurnId, newRun.TurnId);
            Assert.Equal(threadId, newRun.ThreadId);
            Assert.False(newRun.TerminalProjection.ActiveRunCancellation.Available);
            Assert.Equal("not_applicable", newRun.TerminalProjection.ActiveRunCancellation.Reason);
            Assert.True(File.Exists(newRun.TerminalProjection.TurnLog.Reference));
            Assert.DoesNotContain("active_run_not_found", newRun.TerminalProjection.DowngradeReasons);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldFinalizeWithoutSecondToolExecWhenToolCallBudgetIsConsumed()
    {
        var runId = new KernelRunId("run-reactive-budget");
        var core = new StableKernelCore();
        var modelCallCount = 0;
        var runtime = RecordingExecutionRuntime.Create(plan =>
        {
            if (plan.Steps[0].SourceStageId.Value == "stage.model-reason")
            {
                modelCallCount++;
                return CreateStageRuntimeResult(
                    plan,
                    modelCallCount == 1
                        ? CreateToolRequestOutput("call-read-file", "read_file", "read")
                        : CreateToolRequestOutput("call-grep-after-budget", "grep", "search"));
            }

            return plan.Steps[0].SourceStageId.Value == "stage.tool-exec"
                ? CreateToolResultRuntimeResult(plan)
                : CreateStageRuntimeResult(plan, ResolveSignalsForStage(plan));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime.Instance);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.Equal(
            ["stage.prepare-context", "stage.model-reason", "stage.tool-exec", "stage.model-reason", "stage.finalize"],
            result.KernelResult.StageResults.Select(stage => stage.StageId.Value).ToArray());
        var exhaustedStage = Assert.Single(result.KernelResult.StageResults, stage =>
            stage.StageId == new StageId("stage.model-reason")
            && stage.Status == StageResultStatus.Failed);
        Assert.Contains("budget.exhausted", ReadSignals(exhaustedStage.Output));
        Assert.Equal(5, runtime.ExecuteCallCount);
        Assert.Single(runtime.Plans, plan => plan.Steps[0].SourceStageId.Value == "stage.tool-exec");
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldKeepProviderRetryInsideSingleModelReasonStage()
    {
        var runId = new KernelRunId("run-reactive-provider-retry");
        var core = new StableKernelCore();
        var sink = new RecordingMetricsSink();
        var provider = new RetryThenSuccessProviderModule("provider.default", failuresBeforeSuccess: 4);
        var runtime = new TianShuExecutionRuntime(
            new ExecutionRuntimeStepBindingRegistry(
                providers: new Dictionary<string, IProviderModule>(StringComparer.Ordinal)
                {
                    ["provider.default"] = provider,
                }),
            sink);
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.Equal(
            ["stage.prepare-context", "stage.model-reason", "stage.finalize"],
            result.KernelResult.StageResults.Select(stage => stage.StageId.Value).ToArray());
        Assert.Single(result.KernelResult.StageResults, stage => stage.StageId == new StageId("stage.model-reason"));
        Assert.DoesNotContain(result.KernelResult.StageResults, stage => stage.StageId == new StageId("stage.tool-exec"));
        Assert.Equal(5, provider.InvokeCount);
        Assert.Equal([1, 2, 3, 4, 5], sink.Events.Select(static item => item.AttemptIndex).ToArray());
        Assert.Equal(5, sink.Events.Sum(static item => item.ModelCallCount));
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldFailModelReasonStageAfterProviderRetryBudgetExhausted()
    {
        var runId = new KernelRunId("run-reactive-provider-retry-exhausted");
        var core = new StableKernelCore();
        var provider = new RetryThenSuccessProviderModule("provider.default", failuresBeforeSuccess: 5);
        var runtime = new TianShuExecutionRuntime(
            new ExecutionRuntimeStepBindingRegistry(
                providers: new Dictionary<string, IProviderModule>(StringComparer.Ordinal)
                {
                    ["provider.default"] = provider,
                }));
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeFailed, result.Disposition);
        Assert.Equal(
            ["stage.prepare-context", "stage.model-reason", "stage.finalize"],
            result.KernelResult.StageResults.Select(stage => stage.StageId.Value).ToArray());
        var failedModelStage = Assert.Single(result.KernelResult.StageResults, stage => stage.StageId == new StageId("stage.model-reason"));
        Assert.Equal(StageResultStatus.Failed, failedModelStage.Status);
        Assert.Contains("model_failed", ReadSignals(failedModelStage.Output));
        Assert.Equal(5, provider.InvokeCount);
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldDriveRegisteredProviderToolBridgeAndReturnEvidenceToProvider()
    {
        var runId = new KernelRunId("run-reactive-bridge-local-tool");
        var core = new StableKernelCore();
        var sink = new RecordingMetricsSink();
        var provider = new BridgeLoopProviderModule("provider.default");
        var tool = new BridgeLoopTool("read_file");
        var runtime = new TianShuExecutionRuntime(
            new ExecutionRuntimeStepBindingRegistry(
                providers: new Dictionary<string, IProviderModule>(StringComparer.Ordinal)
                {
                    ["provider.default"] = provider,
                },
                tools: new Dictionary<string, ITianShuTool>(StringComparer.Ordinal)
                {
                    ["read_file"] = tool,
                }),
            sink);
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.Equal(
            ["stage.prepare-context", "stage.model-reason", "stage.tool-exec", "stage.model-reason", "stage.finalize"],
            result.KernelResult.StageResults.Select(stage => stage.StageId.Value).ToArray());
        Assert.Equal(2, provider.InvokeCount);
        Assert.Equal(1, tool.InvokeCount);

        Assert.NotNull(tool.LastInvocation);
        Assert.Equal("call-bridge-read", tool.LastInvocation!.CallId.Value);
        Assert.Equal("read_file", tool.LastInvocation.ToolId);
        Assert.Equal("read", tool.LastInvocation.Operation);
        Assert.Equal("README.md", tool.LastInvocation.Input.GetProperty("path").GetString());

        var evidence = Assert.Single(provider.SecondRequestToolResults);
        Assert.Equal("call-bridge-read", evidence.CallId.Value);
        Assert.Equal("read_file", evidence.Result.GetProperty("toolId").GetString());
        Assert.Equal("succeeded", evidence.Result.GetProperty("status").GetString());
        var streamItem = Assert.Single(evidence.Result.GetProperty("output").GetProperty("streamItems").Items);
        Assert.Equal("text", streamItem.GetProperty("channel").GetString());
        Assert.Equal("bridge-read-ok", streamItem.GetProperty("payload").GetProperty("summary").GetString());

        Assert.NotNull(result.RuntimeResult);
        Assert.Contains(result.RuntimeResult!.StepResults, step =>
            step.StepKind == RuntimeStepKind.ModelInvocation
            && step.TraceRef is not null
            && step.Output?.TryGetProperty("toolRequests", out _) == true);
        Assert.Contains(result.RuntimeResult.StepResults, step =>
            step.StepKind == RuntimeStepKind.ToolInvocation
            && step.TraceRef is not null
            && step.Output?.TryGetProperty("toolResults", out _) == true);
        Assert.All(result.RuntimeResult.StepResults.Where(step => step.Output is not null), step =>
        {
            Assert.Equal(step.StepId, step.Output!.GetProperty("stepId").GetString());
            Assert.Equal(step.StepKind.ToString(), step.Output.GetProperty("stepKind").GetString());
            Assert.Equal("intent-loop", step.Output.GetProperty("sourceIntentId").GetString());
            Assert.Equal("graph.turn.default", step.Output.GetProperty("sourceGraphId").GetString());
            Assert.StartsWith("stage.", step.Output.GetProperty("sourceStageId").GetString(), StringComparison.Ordinal);
            Assert.StartsWith("operation-stage.", step.Output.GetProperty("sourceKernelOperationId").GetString(), StringComparison.Ordinal);
        });
        Assert.Contains(sink.Events, item => item.ModelCallCount == 1 && item.StepId is not null && item.StepId.Contains("model-reason", StringComparison.Ordinal));
        Assert.Contains(sink.Events, item => item.ModelCallCount == 0 && item.ModelId == "read_file");

        var summary = KernelRuntimeReplayProjector.Build(result, sink.Events);
        Assert.Equal("live-pass-reactive-graph", summary.Completeness);
        Assert.Equal("graph.turn.default", summary.GraphId);
        Assert.Equal(
            ["stage.prepare-context", "stage.model-reason", "stage.tool-exec", "stage.model-reason", "stage.finalize"],
            summary.StagePath);
        Assert.Equal(2, summary.Steps.Count(static step => step.StageId == "stage.finalize"));
        Assert.Contains(summary.Steps, step => step.StageId == "stage.finalize" && step.StepKind == RuntimeStepKind.StateCommit);
        Assert.Contains(summary.Steps, step => step.StageId == "stage.finalize" && step.StepKind == RuntimeStepKind.Diagnostic);
        Assert.Contains(summary.Steps, step =>
            step.StepKind == RuntimeStepKind.ToolInvocation
            && step.StepId.Contains("request-", StringComparison.Ordinal)
            && step.MetricsEventIds.Count == 1
            && step.RuntimePlanId is not null
            && step.RuntimePlanId.Contains("stage.tool-exec", StringComparison.Ordinal));
        Assert.Contains(summary.Steps, step =>
            step.StepKind == RuntimeStepKind.ModelInvocation
            && step.MetricsEventIds.Count == 1
            && step.RuntimePlanId is not null
            && step.RuntimePlanId.Contains("stage.model-reason", StringComparison.Ordinal));
        Assert.All(summary.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step.RuntimeTraceRef)));
        Assert.Empty(summary.FailureCodes);
    }

    [Fact]
    public void KernelRuntimeTurnLoopComposition_ShouldRegisterOnlyReadOnlyFilesystemToolsByDefault()
    {
        var tools = KernelRuntimeTurnLoopComposition.CreateReadOnlyTools();

        Assert.Equal(["glob", "grep", "list_dir", "read_file"], tools.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("apply_patch", tools.Keys);
        Assert.DoesNotContain("write", tools.Keys);
        Assert.All(tools.Values, tool =>
        {
            Assert.Equal(SideEffectLevel.ReadOnly, tool.Descriptor.SideEffects.Level);
            Assert.False(tool.Descriptor.Permissions.RequiresHumanGate);
        });
    }

    [Fact]
    public void KernelRuntimeTurnLoopComposition_ShouldRegisterWorkspaceWriteToolOnlyWhenExplicitlyApproved()
    {
        var tools = KernelRuntimeTurnLoopComposition.CreateTools(includeWorkspaceWrite: true);

        Assert.Contains("write", tools.Keys);
        Assert.DoesNotContain("apply_patch", tools.Keys);
        var writeTool = tools["write"];
        Assert.Equal(SideEffectLevel.WorkspaceWrite, writeTool.Descriptor.SideEffects.Level);
        Assert.True(writeTool.Descriptor.Permissions.RequiresHumanGate);
        Assert.Equal(ToolApprovalRequirement.Required, writeTool.Descriptor.ApprovalRequirement);
    }

    [Fact]
    public async Task WorkspaceWriteTool_ShouldRejectAbsolutePathEvenUnderWorkspace()
    {
        using var workspace = new TempWorkspace();
        var targetFile = Path.Combine(workspace.Path, "absolute-denied.txt");
        var tools = KernelRuntimeTurnLoopComposition.CreateTools(includeWorkspaceWrite: true);
        var writeTool = tools["write"];
        var result = await writeTool.InvokeAsync(
            new ToolInvocationEnvelope(
                new CallId("call-write-absolute-denied"),
                "write",
                "write",
                StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["path"] = targetFile,
                    ["content"] = "should not be written",
                }),
                new PermissionEnvelope(["tool.write"], requiresHumanGate: true),
                new SideEffectProfile(SideEffectLevel.WorkspaceWrite, ["workspace"], reversible: false, requiresAudit: true)),
            new ToolInvocationContext(
                "runtime-step-write-absolute-denied",
                "intent-write-absolute-denied",
                "graph-write-absolute-denied",
                "stage-tool-exec",
                "operation-write-absolute-denied",
                workspace.Path),
            CancellationToken.None);

        Assert.NotNull(result.Failure);
        Assert.Contains("workspace-relative", result.Failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(targetFile));
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldFailClosedForWriteWhenApprovalIsMissing()
    {
        var runId = new KernelRunId("run-reactive-provider-write-denied");
        using var workspace = new TempWorkspace();
        var targetFile = Path.Combine(workspace.Path, "denied.txt");
        const string envKey = "TIANSHU_TEST_WRITE_DENIED_KEY";
        var handler = new SequencedProviderSseHandler(
            BuildSse(
                new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        call_id = "call-write-denied",
                        name = "write",
                        arguments = JsonSerializer.Serialize(new
                        {
                            path = targetFile,
                            content = "should not be written",
                        }),
                    },
                },
                new
                {
                    type = "response.completed",
                    response = new { output_text = "write requested" },
                }));
        var config = CreateConfiguredProviderConfig(envKey);
        await using var runtime = KernelRuntimeTurnLoopComposition.CreateRuntime(
            config,
            providerHttpHandler: handler,
            readEnvironmentVariable: name => string.Equals(name, envKey, StringComparison.Ordinal) ? "test-api-key" : null);
        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            new KernelRuntimeExecutionOptions(
                new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                new ExecutionRuntimeContext(
                    new ExecutionId("execution-provider-write-denied"),
                    runId,
                    CreateCliBridgeGovernance(),
                    workspace.Path),
                ExecuteRuntimePlan: true),
            CancellationToken.None);

        Assert.NotEqual(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.DoesNotContain(result.RuntimeResult?.StepResults ?? [], step =>
            step.StepKind == RuntimeStepKind.ToolInvocation
            && step.Status == RuntimeStepResultStatus.Succeeded
            && step.Output?.GetProperty("toolId").GetString() == "write");
        Assert.False(File.Exists(targetFile));
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldExecuteProviderWriteToolWhenCliApprovalGranted()
    {
        var runId = new KernelRunId("run-reactive-provider-write-approved");
        using var workspace = new TempWorkspace();
        var targetFile = Path.Combine(workspace.Path, "approved.txt");
        const string envKey = "TIANSHU_TEST_WRITE_APPROVED_KEY";
        var handler = new SequencedProviderSseHandler(
            BuildSse(
                new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        call_id = "call-write-approved",
                        name = "write",
                        arguments = JsonSerializer.Serialize(new
                        {
                            path = "approved.txt",
                            content = "approved workspace write",
                        }),
                    },
                },
                new
                {
                    type = "response.completed",
                    response = new
                    {
                        output_text = "write requested",
                        usage = new { input_tokens = 4, output_tokens = 2 },
                    },
                }),
            BuildSse(
                new
                {
                    type = "response.output_text.delta",
                    delta = "done",
                },
                new
                {
                    type = "response.completed",
                    response = new
                    {
                        output_text = "done",
                        usage = new { input_tokens = 7, output_tokens = 3 },
                    },
                }));
        var config = CreateConfiguredProviderConfig(envKey);
        var governance = CreateCliBridgeGovernanceWithWorkspaceWrite();
        await using var runtime = KernelRuntimeTurnLoopComposition.CreateRuntime(
            config,
            includeWorkspaceWrite: true,
            providerHttpHandler: handler,
            readEnvironmentVariable: name => string.Equals(name, envKey, StringComparison.Ordinal) ? "test-api-key" : null);
        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1, governance: governance),
            new KernelRuntimeExecutionOptions(
                new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                new ExecutionRuntimeContext(
                    new ExecutionId("execution-provider-write-approved"),
                    runId,
                    governance,
                    workspace.Path),
                ExecuteRuntimePlan: true),
            CancellationToken.None);

        Assert.True(
            result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted,
            BuildRuntimeFailureMessage(result));
        Assert.Equal("approved workspace write", await File.ReadAllTextAsync(targetFile, Encoding.UTF8));
        Assert.Equal(2, handler.Requests.Count);
        using var secondPayload = JsonDocument.Parse(handler.RequestBodies[1]);
        var secondInputJson = secondPayload.RootElement.GetProperty("input").GetRawText();
        Assert.Contains("function_call_output", secondInputJson, StringComparison.Ordinal);
        Assert.Contains("approved.txt", secondInputJson, StringComparison.Ordinal);
        Assert.Contains(result.RuntimeResult!.StepResults, step =>
            step.StepKind == RuntimeStepKind.ToolInvocation
            && step.Output?.GetProperty("toolId").GetString() == "write");
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldUseConfiguredProviderModuleAndRealReadOnlyTool()
    {
        var runId = new KernelRunId("run-reactive-configured-provider-tool");
        using var workspace = new TempWorkspace();
        var targetFile = Path.Combine(workspace.Path, "sample.txt");
        await File.WriteAllTextAsync(targetFile, "configured provider read_file result", Encoding.UTF8);

        const string envKey = "TIANSHU_TEST_CONFIGURED_PROVIDER_KEY";
        var handler = new SequencedProviderSseHandler(
            BuildSse(
                new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "function_call",
                        call_id = "call-configured-read",
                        name = "read_file",
                        arguments = JsonSerializer.Serialize(new
                        {
                            file_path = targetFile,
                            offset = 1,
                            limit = 3,
                        }),
                    },
                },
                new
                {
                    type = "response.completed",
                    response = new
                    {
                        output_text = "tool requested",
                        usage = new
                        {
                            input_tokens = 5,
                            output_tokens = 2,
                        },
                    },
                }),
            BuildSse(
                new
                {
                    type = "response.output_text.delta",
                    delta = "done",
                },
                new
                {
                    type = "response.completed",
                    response = new
                    {
                        output_text = "done",
                        usage = new
                        {
                            input_tokens = 8,
                            output_tokens = 3,
                        },
                    },
                }));
        var config = new ResolvedTianShuConfig
        {
            Model = "gpt-5.5",
            ModelProvider = "openai",
            ProviderBaseUrl = "https://provider.test/v1",
            ProviderEnvKey = envKey,
            ProviderWireApi = ProviderWireApi.Responses,
            ProtocolAdapter = ProviderWireApi.Responses,
            ServiceTier = "flex",
            ModelReasoningEnabled = true,
            ModelReasoningEffort = "medium",
            ModelReasoningSummary = "auto",
            ModelVerbosity = "concise",
        };
        var sink = new RecordingMetricsSink();
        await using var runtime = KernelRuntimeTurnLoopComposition.CreateRuntime(
            config,
            providerHttpHandler: handler,
            readEnvironmentVariable: name => string.Equals(name, envKey, StringComparison.Ordinal) ? "test-api-key" : null,
            metricsSink: sink);
        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            new KernelRuntimeExecutionOptions(
                new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                new ExecutionRuntimeContext(
                    new ExecutionId("execution-configured-provider-tool"),
                    runId,
                    CreateCliBridgeGovernance(),
                    workspace.Path),
                ExecuteRuntimePlan: true),
            CancellationToken.None);

        Assert.True(
            result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted,
            BuildRuntimeFailureMessage(result));
        Assert.Equal(
            ["stage.prepare-context", "stage.model-reason", "stage.tool-exec", "stage.model-reason", "stage.finalize"],
            result.KernelResult.StageResults.Select(stage => stage.StageId.Value).ToArray());
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer test-api-key", request.Headers.Authorization?.ToString());
            Assert.Equal("https://provider.test/v1/responses", request.RequestUri?.ToString());
            Assert.True(request.Headers.Accept.Any(value => string.Equals(value.MediaType, "text/event-stream", StringComparison.Ordinal)));
            var traceParent = Assert.Single(request.Headers.GetValues("traceparent"));
            AssertW3CTraceParent(traceParent);
            var metadata = Assert.Single(request.Headers.GetValues("x-codex-turn-metadata"));
            using var metadataJson = JsonDocument.Parse(metadata);
            Assert.StartsWith(
                "execution-run-reactive-configured-provider-tool-stage.model-reason",
                metadataJson.RootElement.GetProperty("executionId").GetString(),
                StringComparison.Ordinal);
            Assert.Equal("thread-loop", metadataJson.RootElement.GetProperty("threadId").GetString());
            Assert.Equal("graph.turn.default", metadataJson.RootElement.GetProperty("sourceGraphId").GetString());
            Assert.Equal("stage.model-reason", metadataJson.RootElement.GetProperty("sourceStageId").GetString());
        });

        using var firstPayload = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal("gpt-5.5", firstPayload.RootElement.GetProperty("model").GetString());
        Assert.True(firstPayload.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(firstPayload.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("auto", firstPayload.RootElement.GetProperty("tool_choice").GetString());
        Assert.False(firstPayload.RootElement.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.Equal("flex", firstPayload.RootElement.GetProperty("service_tier").GetString());
        Assert.Equal("medium", firstPayload.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("auto", firstPayload.RootElement.GetProperty("reasoning").GetProperty("summary").GetString());
        Assert.Equal("low", firstPayload.RootElement.GetProperty("text").GetProperty("verbosity").GetString());
        Assert.False(string.IsNullOrWhiteSpace(firstPayload.RootElement.GetProperty("instructions").GetString()));
        Assert.Contains(firstPayload.RootElement.GetProperty("input").EnumerateArray(), item =>
            string.Equals(item.GetProperty("role").GetString(), "user", StringComparison.Ordinal));
        Assert.Contains(firstPayload.RootElement.GetProperty("tools").EnumerateArray(), tool =>
            string.Equals(tool.GetProperty("name").GetString(), "read_file", StringComparison.Ordinal));

        using var secondPayload = JsonDocument.Parse(handler.RequestBodies[1]);
        var secondInputJson = secondPayload.RootElement.GetProperty("input").GetRawText();
        Assert.Contains("function_call_output", secondInputJson, StringComparison.Ordinal);
        Assert.Contains("configured provider read_file result", secondInputJson, StringComparison.Ordinal);

        Assert.Contains(result.RuntimeResult!.StepResults, step =>
            step.StepKind == RuntimeStepKind.ModelInvocation
            && step.Output?.GetProperty("runtimeBoundary").GetString() == "execution.runtime.provider_bridge");
        Assert.Contains(result.RuntimeResult.StepResults, step =>
            step.StepKind == RuntimeStepKind.ToolInvocation
            && step.Output?.GetProperty("runtimeBoundary").GetString() == "execution.runtime.tool_bridge");
        var replay = KernelRuntimeReplayProjector.Build(result, sink.Events);
        var diagnosticsProjection = KernelRuntimeDiagnosticsProjector.Build(result, replay, sink.Events);
        Assert.Equal(3, diagnosticsProjection.MetricsEventCount);
        Assert.Equal(2, diagnosticsProjection.ModelMetricsEventCount);
        Assert.Equal(1, diagnosticsProjection.ToolMetricsEventCount);
        Assert.True(diagnosticsProjection.TokenUsage.Available);
        Assert.False(diagnosticsProjection.TokenUsage.Estimated);
        Assert.Equal(13, diagnosticsProjection.TokenUsage.InputTokens);
        Assert.Equal(5, diagnosticsProjection.TokenUsage.OutputTokens);
        Assert.Equal(18, diagnosticsProjection.TokenUsage.TotalTokens);
        Assert.Equal(["provider.completion.usage"], diagnosticsProjection.TokenUsage.Sources);
        Assert.False(diagnosticsProjection.Cost.Available);
        Assert.Contains("price_model_missing", diagnosticsProjection.MissingReasons);
        Assert.Contains("tool_usage_not_applicable", diagnosticsProjection.MissingReasons);
        Assert.All(replay.Steps.Where(static step => step.StepKind is RuntimeStepKind.ModelInvocation or RuntimeStepKind.ToolInvocation), step =>
            Assert.NotEmpty(step.MetricsEventIds));
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldExposeSubAgentToolSurfaceWhenEnabled()
    {
        var runId = new KernelRunId("run-reactive-subagent-tool-surface");
        using var workspace = new TempWorkspace();
        const string envKey = "TIANSHU_TEST_SUBAGENT_SURFACE_KEY";
        var handler = new SequencedProviderSseHandler(
            BuildSse(
                new
                {
                    type = "response.output_text.delta",
                    delta = "no subagent needed",
                },
                new
                {
                    type = "response.completed",
                    response = new
                    {
                        output_text = "no subagent needed",
                        usage = new { input_tokens = 4, output_tokens = 2 },
                    },
                }));
        var config = CreateConfiguredProviderConfig(envKey);
        await using var runtime = KernelRuntimeTurnLoopComposition.CreateRuntime(
            config,
            providerHttpHandler: handler,
            readEnvironmentVariable: name => string.Equals(name, envKey, StringComparison.Ordinal) ? "test-api-key" : null,
            subAgentModules: new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal));
        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            new KernelRuntimeExecutionOptions(
                new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                new ExecutionRuntimeContext(
                    new ExecutionId("execution-subagent-tool-surface"),
                    runId,
                    CreateCliBridgeGovernanceWithSubAgents(),
                    workspace.Path),
                ExecuteRuntimePlan: true),
            CancellationToken.None);

        Assert.True(
            result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted,
            BuildRuntimeFailureMessage(result));
        using var payload = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var tools = payload.RootElement.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Contains(tools, tool => string.Equals(tool.GetProperty("name").GetString(), "read_file", StringComparison.Ordinal));
        var spawnAgent = Assert.Single(tools, tool => string.Equals(tool.GetProperty("name").GetString(), "spawn_agent", StringComparison.Ordinal));
        var description = spawnAgent.GetProperty("description").GetString();
        Assert.Contains("separate evidence domain", description, StringComparison.Ordinal);
        Assert.Contains("independent verification of a claim", description, StringComparison.Ordinal);
        Assert.Contains("waits for it to finish", description, StringComparison.Ordinal);
        Assert.Contains("Do not use for a single directory listing", description, StringComparison.Ordinal);
        var parameters = spawnAgent.GetProperty("parameters");
        Assert.Contains("operation", parameters.GetProperty("required").EnumerateArray().Select(static item => item.GetString()));
        Assert.Contains("taskBrief", parameters.GetProperty("required").EnumerateArray().Select(static item => item.GetString()));
        Assert.Contains(
            "standalone task brief",
            parameters.GetProperty("properties").GetProperty("taskBrief").GetProperty("description").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "read-only analysis",
            parameters.GetProperty("properties").GetProperty("requiresHumanGate").GetProperty("description").GetString(),
            StringComparison.Ordinal);
        var replay = KernelRuntimeReplayProjector.Build(result);
        var diagnostics = KernelRuntimeDiagnosticsProjector.Build(result, replay, Array.Empty<RuntimeMetricsEvent>());
        Assert.True(diagnostics.ProviderToolSurface.Available);
        Assert.Contains("responses", diagnostics.ProviderToolSurface.WireApis);
        Assert.Contains("spawn_agent", diagnostics.ProviderToolSurface.Names);
        Assert.True(diagnostics.ProviderToolSurface.HasSpawnAgent);
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldNotProjectChatReasoningContentAsAssistantText()
    {
        var runId = new KernelRunId("run-reactive-chat-reasoning-filter");
        const string envKey = "TIANSHU_TEST_CHAT_REASONING_FILTER_KEY";
        var handler = new SequencedProviderSseHandler(
            BuildSse(
                new
                {
                    choices = new[]
                    {
                        new { delta = new { reasoning_content = "The user wants a terse answer." } },
                    },
                },
                new
                {
                    choices = new[]
                    {
                        new { delta = new { content = "OK" } },
                    },
                },
                new
                {
                    choices = new[]
                    {
                        new { finish_reason = "stop" },
                    },
                }));
        var config = CreateConfiguredProviderConfig(envKey, wireApi: ProviderWireApi.OpenAiChatCompletions);
        await using var runtime = KernelRuntimeTurnLoopComposition.CreateRuntime(
            config,
            providerHttpHandler: handler,
            readEnvironmentVariable: name => string.Equals(name, envKey, StringComparison.Ordinal) ? "test-api-key" : null);
        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            new KernelRuntimeExecutionOptions(
                new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                new ExecutionRuntimeContext(
                    new ExecutionId("execution-chat-reasoning-filter"),
                    runId,
                    CreateCliBridgeGovernance()),
                ExecuteRuntimePlan: true),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        var modelResult = Assert.Single(
            result.RuntimeResult!.StepResults,
            step => step.StepKind == RuntimeStepKind.ModelInvocation);
        Assert.Equal("OK", modelResult.Output!.GetProperty("assistantText").GetString());
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldReplayChatToolCallAndToolResultIntoNextProviderInput()
    {
        var runId = new KernelRunId("run-reactive-chat-tool-replay");
        using var workspace = new TempWorkspace();
        var targetFile = Path.Combine(workspace.Path, "chat-sample.txt");
        await File.WriteAllTextAsync(targetFile, "chat tool replay result", Encoding.UTF8);

        const string envKey = "TIANSHU_TEST_CHAT_TOOL_REPLAY_KEY";
        var argumentsJson = JsonSerializer.Serialize(new
        {
            file_path = targetFile,
            offset = 1,
            limit = 2,
        });
        var argumentSplit = Math.Max(1, argumentsJson.Length / 2);
        var handler = new SequencedProviderSseHandler(
            BuildSse(
                new
                {
                    choices = new[]
                    {
                        new
                        {
                            delta = new
                            {
                                tool_calls = new[]
                                {
                                    new
                                    {
                                        id = "call-chat-read",
                                            type = "function",
                                            function = new
                                            {
                                                name = "read_file",
                                                arguments = argumentsJson[..argumentSplit],
                                            },
                                        },
                                    },
                            },
                        },
                    },
                },
                new
                {
                    choices = new[]
                    {
                        new
                        {
                            delta = new
                            {
                                tool_calls = new[]
                                {
                                    new
                                    {
                                        index = 0,
                                        function = new
                                        {
                                            arguments = argumentsJson[argumentSplit..],
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
                new
                {
                    choices = new[]
                    {
                        new { finish_reason = "tool_calls" },
                    },
                }),
            BuildSse(
                new
                {
                    choices = new[]
                    {
                        new { delta = new { content = "chat done" } },
                    },
                },
                new
                {
                    choices = new[]
                    {
                        new { finish_reason = "stop" },
                    },
                }));
        var config = new ResolvedTianShuConfig
        {
            Model = "deepseek-v4-flash",
            ModelProvider = "openai",
            ProviderBaseUrl = "https://provider.test/v1",
            ProviderEnvKey = envKey,
            ProviderWireApi = ProviderWireApi.OpenAiChatCompletions,
            ProtocolAdapter = ProviderWireApi.OpenAiChatCompletions,
        };
        await using var runtime = KernelRuntimeTurnLoopComposition.CreateRuntime(
            config,
            providerHttpHandler: handler,
            readEnvironmentVariable: name => string.Equals(name, envKey, StringComparison.Ordinal) ? "test-api-key" : null);
        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            new KernelRuntimeExecutionOptions(
                new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                new ExecutionRuntimeContext(
                    new ExecutionId("execution-chat-tool-replay"),
                    runId,
                    CreateCliBridgeGovernance(),
                    workspace.Path),
                ExecuteRuntimePlan: true),
            CancellationToken.None);

        Assert.True(
            result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted,
            BuildRuntimeFailureMessage(result));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("https://provider.test/v1/chat/completions", request.RequestUri?.ToString());
            var traceParent = Assert.Single(request.Headers.GetValues("traceparent"));
            AssertW3CTraceParent(traceParent);
        });

        using var secondPayload = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.Equal("deepseek-v4-flash", secondPayload.RootElement.GetProperty("model").GetString());
        var messages = secondPayload.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        var assistantToolMessage = Assert.Single(messages, message =>
            string.Equals(message.GetProperty("role").GetString(), "assistant", StringComparison.Ordinal)
            && message.TryGetProperty("tool_calls", out _));
        var toolCall = Assert.Single(assistantToolMessage.GetProperty("tool_calls").EnumerateArray());
        Assert.Equal("call-chat-read", toolCall.GetProperty("id").GetString());
        Assert.Equal("read_file", toolCall.GetProperty("function").GetProperty("name").GetString());
        var toolResultMessage = Assert.Single(messages, message =>
            string.Equals(message.GetProperty("role").GetString(), "tool", StringComparison.Ordinal)
            && string.Equals(message.GetProperty("tool_call_id").GetString(), "call-chat-read", StringComparison.Ordinal));
        Assert.Contains("chat tool replay result", toolResultMessage.GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldProjectAnthropicContentBlockDeltaAsAssistantText()
    {
        var runId = new KernelRunId("run-reactive-anthropic-delta");
        const string envKey = "TIANSHU_TEST_ANTHROPIC_DELTA_KEY";
        var handler = new SequencedProviderSseHandler(
            BuildSse(
                new
                {
                    type = "content_block_delta",
                    delta = new
                    {
                        type = "text_delta",
                        text = "OK",
                    },
                },
                new
                {
                    type = "message_stop",
                }));
        var config = new ResolvedTianShuConfig
        {
            Model = "claude-opus-4.8",
            ModelProvider = "anthropic",
            ProviderBaseUrl = "https://provider.test",
            ProviderEnvKey = envKey,
            ProviderWireApi = ProviderWireApi.AnthropicMessages,
            ProtocolAdapter = ProviderWireApi.AnthropicMessages,
        };
        await using var runtime = KernelRuntimeTurnLoopComposition.CreateRuntime(
            config,
            providerHttpHandler: handler,
            readEnvironmentVariable: name => string.Equals(name, envKey, StringComparison.Ordinal) ? "test-api-key" : null);
        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            new KernelRuntimeExecutionOptions(
                new KernelRunOptions(runId, enableAdaptive: false, requireHumanGate: false),
                new ExecutionRuntimeContext(
                    new ExecutionId("execution-anthropic-delta"),
                    runId,
                    CreateCliBridgeGovernance()),
                ExecuteRuntimePlan: true),
            CancellationToken.None);

        Assert.True(
            result.Disposition == KernelRuntimeExecutionDisposition.RuntimeCompleted,
            BuildRuntimeFailureMessage(result));
        var modelResult = Assert.Single(
            result.RuntimeResult!.StepResults,
            step => step.StepKind == RuntimeStepKind.ModelInvocation);
        Assert.Equal("OK", modelResult.Output!.GetProperty("assistantText").GetString());
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldMaterializeToolStepsFromModelToolRequests()
    {
        var runId = new KernelRunId("run-reactive-tool-requests");
        var core = new StableKernelCore();
        var modelCallCount = 0;
        var runtime = RecordingExecutionRuntime.Create(plan =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCallCount++;
                return modelCallCount == 1
                    ? CreateStageRuntimeResult(
                        plan,
                        StructuredValue.FromPlainObject(new Dictionary<string, object?>
                        {
                            ["signals"] = new[] { "tool_requests_available" },
                            ["toolRequests"] = new object?[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["callId"] = "call-read-file",
                                    ["toolId"] = "read_file",
                                    ["operation"] = "read",
                                    ["input"] = new Dictionary<string, object?> { ["path"] = "README.md" },
                                },
                                new Dictionary<string, object?>
                                {
                                    ["callId"] = "call-grep",
                                    ["toolId"] = "grep",
                                    ["operation"] = "search",
                                    ["input"] = new Dictionary<string, object?> { ["pattern"] = "StageGraph" },
                                },
                            },
                        }))
                    : CreateStageRuntimeResult(plan, ["model_final_response"]);
            }

            return stageId == "stage.tool-exec"
                ? CreateToolResultRuntimeResult(plan)
                : CreateStageRuntimeResult(plan, ResolveSignalsForStage(plan));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime.Instance);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        var toolPlan = Assert.Single(runtime.Plans, plan => plan.Steps[0].SourceStageId.Value == "stage.tool-exec");
        var toolSteps = toolPlan.Steps.Cast<ToolInvocationStep>().ToArray();
        Assert.Equal(["read_file", "grep"], toolSteps.Select(step => step.CapabilityToolId).ToArray());
        Assert.Equal(["call-read-file", "call-grep"], toolSteps.Select(step => step.InputEnvelope.CallId.Value).ToArray());
        Assert.Equal(["read", "search"], toolSteps.Select(step => step.InputEnvelope.Operation).ToArray());
        Assert.Equal("README.md", toolSteps[0].InputEnvelope.Input.GetProperty("path").GetString());
        Assert.Equal("StageGraph", toolSteps[1].InputEnvelope.Input.GetProperty("pattern").GetString());
        Assert.DoesNotContain(toolSteps, step => step.InputEnvelope.Operation == "execute_approved_requests");
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldFailClosedWhenModelToolRequestIsNotAllowed()
    {
        var runId = new KernelRunId("run-reactive-tool-denied");
        var core = new StableKernelCore();
        var runtime = RecordingExecutionRuntime.Create(plan =>
            plan.Steps[0].SourceStageId.Value == "stage.model-reason"
                ? CreateStageRuntimeResult(
                    plan,
                    StructuredValue.FromPlainObject(new Dictionary<string, object?>
                    {
                        ["signals"] = new[] { "tool_requests_available" },
                        ["toolRequests"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["callId"] = "call-shell",
                                ["toolId"] = "shell",
                                ["operation"] = "exec",
                                ["input"] = new Dictionary<string, object?> { ["command"] = "echo denied" },
                            },
                        },
                    }))
                : CreateStageRuntimeResult(plan, ResolveSignalsForStage(plan)));
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime.Instance);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeFailed, result.Disposition);
        Assert.Equal(RuntimeStepResultStatus.Failed, result.RuntimeResult!.Status);
        Assert.Contains(result.RuntimeResult.StepResults, step => step.Failure?.Code == "runtime.reactive.tool_request_not_allowed");
        Assert.DoesNotContain(runtime.Plans, plan => plan.Steps[0].SourceStageId.Value == "stage.tool-exec");
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldFailClosedWhenToolRequestDetailsAreMissing()
    {
        var runId = new KernelRunId("run-reactive-tool-missing");
        var core = new StableKernelCore();
        var runtime = RecordingExecutionRuntime.Create(plan =>
            plan.Steps[0].SourceStageId.Value == "stage.model-reason"
                ? CreateStageRuntimeResult(plan, ["tool_requests_available"])
                : CreateStageRuntimeResult(plan, ResolveSignalsForStage(plan)));
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime.Instance);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeFailed, result.Disposition);
        Assert.Equal(RuntimeStepResultStatus.Failed, result.RuntimeResult!.Status);
        Assert.Contains(result.RuntimeResult.StepResults, step => step.Failure?.Code == "runtime.reactive.tool_requests_missing");
        Assert.DoesNotContain(runtime.Plans, plan => plan.Steps[0].SourceStageId.Value == "stage.tool-exec");
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldInjectToolResultsIntoNextModelReasonInput()
    {
        var runId = new KernelRunId("run-reactive-tool-results");
        var core = new StableKernelCore();
        var modelCallCount = 0;
        IReadOnlyList<ToolResultProviderInputItem> injectedEvidence = Array.Empty<ToolResultProviderInputItem>();
        IReadOnlyList<ToolCallProviderInputItem> injectedToolCalls = Array.Empty<ToolCallProviderInputItem>();
        var runtime = RecordingExecutionRuntime.Create(plan =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCallCount++;
                if (modelCallCount == 1)
                {
                    return CreateStageRuntimeResult(
                        plan,
                        CreateToolRequestsOutput(
                            ("call-read-file", "read_file", "read", new Dictionary<string, object?> { ["path"] = "README.md" }),
                            ("call-grep", "grep", "search", new Dictionary<string, object?> { ["pattern"] = "StageGraph" })));
                }

                var modelStep = Assert.IsType<ModelInvocationStep>(Assert.Single(plan.Steps));
                injectedToolCalls = modelStep.InputEnvelope.Inputs.OfType<ToolCallProviderInputItem>().ToArray();
                injectedEvidence = modelStep.InputEnvelope.Inputs.OfType<ToolResultProviderInputItem>().ToArray();
                return CreateStageRuntimeResult(plan, ["model_final_response"]);
            }

            if (stageId == "stage.tool-exec")
            {
                return CreateToolResultRuntimeResult(plan);
            }

            return CreateStageRuntimeResult(plan, ResolveSignalsForStage(plan));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime.Instance);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.Equal(2, modelCallCount);
        Assert.Equal(["call-read-file", "call-grep"], injectedToolCalls.Select(item => item.CallId.Value).ToArray());
        Assert.Equal(["read_file", "grep"], injectedToolCalls.Select(item => item.ToolId).ToArray());
        Assert.Equal(["call-read-file", "call-grep"], injectedEvidence.Select(item => item.CallId.Value).ToArray());
        Assert.Equal(["read_file", "grep"], injectedEvidence.Select(item => item.Result.GetProperty("toolId").GetString()!).ToArray());
        Assert.All(injectedEvidence, item => Assert.Equal("succeeded", item.Result.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldFailClosedWhenToolResultCallIdDoesNotMatchPendingRequest()
    {
        var runId = new KernelRunId("run-reactive-tool-result-mismatch");
        var core = new StableKernelCore();
        var modelCallCount = 0;
        var runtime = RecordingExecutionRuntime.Create(plan =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCallCount++;
                return CreateStageRuntimeResult(
                    plan,
                    modelCallCount == 1
                        ? CreateToolRequestOutput("call-read-file", "read_file", "read")
                        : StructuredValue.FromPlainObject(new Dictionary<string, object?>
                        {
                            ["signals"] = new[] { "model_final_response" },
                        }));
            }

            if (stageId == "stage.tool-exec")
            {
                return CreateToolResultRuntimeResult(plan, callIdOverride: "call-not-requested");
            }

            return CreateStageRuntimeResult(plan, ResolveSignalsForStage(plan));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(core, runtime.Instance);

        var result = await loop.RunReactiveAsync(
            CreateIntent(toolCallBudget: 1),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: runId),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeFailed, result.Disposition);
        Assert.Equal(RuntimeStepResultStatus.Failed, result.RuntimeResult!.Status);
        Assert.Contains(result.RuntimeResult.StepResults, step => step.Failure?.Code == "runtime.reactive.tool_result_unmatched");
        Assert.Equal(1, modelCallCount);
    }

    [Fact]
    public async Task ReplayProjector_ShouldRebuildFixedGraphLiveRunRelationships()
    {
        var kernelResult = CreateKernelResult(KernelValidationDecision.Approved, CreatePlan());
        var kernel = new RecordingKernelCore(kernelResult);
        var runtime = RecordingExecutionRuntime.Create(CreateRuntimeResult(RuntimeStepResultStatus.Succeeded));
        var loop = new AdaptiveRuntimeExecutionLoop(kernel, runtime.Instance);
        var result = await loop.RunAsync(
            CreateIntent(),
            CreateOptions(executeRuntimePlan: true),
            CancellationToken.None);

        var summary = KernelRuntimeReplayProjector.Build(result, [CreateMetricsEvent(result)]);

        Assert.Equal("live-pass-fixed-graph", summary.Completeness);
        Assert.Equal(result.KernelResult.RunId.Value, summary.RunId);
        Assert.Equal(result.KernelResult.ExecutionPlan!.SourceGraphId.Value, summary.GraphId);
        Assert.Equal(result.RuntimeResult!.ExecutionId.Value, summary.ExecutionId);
        var step = Assert.Single(summary.Steps);
        Assert.Equal("step-loop", step.StepId);
        Assert.Equal("stage-loop", step.StageId);
        Assert.Equal(RuntimeStepResultStatus.Succeeded, step.Status);
        Assert.Equal("metrics-runtime-loop", Assert.Single(step.MetricsEventIds));
        Assert.Empty(summary.FailureCodes);
    }

    [Fact]
    public async Task DiagnosticsProjector_ShouldExposeMetricsUsageAndTraceRefsForHostProjection()
    {
        var kernelResult = CreateKernelResult(KernelValidationDecision.Approved, CreatePlan());
        var kernel = new RecordingKernelCore(kernelResult);
        var runtime = RecordingExecutionRuntime.Create(CreateRuntimeResult(RuntimeStepResultStatus.Succeeded));
        var loop = new AdaptiveRuntimeExecutionLoop(kernel, runtime.Instance);
        var result = await loop.RunAsync(
            CreateIntent(),
            CreateOptions(executeRuntimePlan: true),
            CancellationToken.None);
        var metrics = new[] { CreateMetricsEvent(result) };
        var replay = KernelRuntimeReplayProjector.Build(result, metrics);

        var projection = KernelRuntimeDiagnosticsProjector.Build(result, replay, metrics);

        Assert.Equal(result.RuntimeTraceRef, projection.RuntimeTraceRef);
        Assert.Equal(result.DiagnosticsRef, projection.DiagnosticsRef);
        Assert.Contains("trace://execution/execution-loop/result", projection.RuntimeTraceRefs);
        Assert.Contains("diagnostics://execution/execution-loop/result", projection.DiagnosticsRefs);
        Assert.Equal(1, projection.MetricsEventCount);
        Assert.Equal(["metrics-runtime-loop"], projection.MetricsEventIds);
        Assert.Equal(1, projection.ModelMetricsEventCount);
        Assert.Equal(0, projection.ToolMetricsEventCount);
        Assert.True(projection.TokenUsage.Available);
        Assert.False(projection.TokenUsage.Estimated);
        Assert.Equal(10, projection.TokenUsage.InputTokens);
        Assert.Equal(20, projection.TokenUsage.OutputTokens);
        Assert.Equal(30, projection.TokenUsage.TotalTokens);
        Assert.Equal(["provider.completion.usage"], projection.TokenUsage.Sources);
        Assert.False(projection.Cost.Available);
        Assert.Equal("price_model_missing", projection.Cost.MissingReason);
        Assert.Contains("price_model_missing", projection.MissingReasons);
        Assert.Empty(projection.FailureCodes);
    }

    [Fact]
    public void ReplayProjector_ShouldFailClosedWhenRuntimeStepResultIsMissing()
    {
        var kernelResult = CreateKernelResult(KernelValidationDecision.Approved, CreatePlan());
        var runtimeResult = new ExecutionRunResult(
            "plan-loop",
            new ExecutionId("execution-loop"),
            RuntimeStepResultStatus.Succeeded,
            stepResults: []);
        var result = new KernelRuntimeExecutionResult(
            kernelResult,
            runtimeResult,
            KernelRuntimeExecutionDisposition.RuntimeCompleted,
            kernelResult.TraceId,
            runtimeResult.TraceRef,
            runtimeResult.DiagnosticsRef);

        var error = Assert.Throws<InvalidOperationException>(() => KernelRuntimeReplayProjector.Build(result));

        Assert.Contains("RuntimeResult 缺少 step 结果", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryStateMachine_ShouldFailClosedWhenBlockedRunHasNoCheckpoint()
    {
        var result = CreateBlockedRuntimeExecutionResult();

        var decision = KernelRuntimeRecoveryStateMachine.Evaluate(result);

        Assert.Equal(KernelRuntimeRecoveryDisposition.RecoveryBlockedMissingCheckpoint, decision.Disposition);
        Assert.Equal("runtime.blocked.test", decision.FailureCode);
        Assert.Null(decision.RollbackTargetRef);
        Assert.Empty(decision.CheckpointRefs);
    }

    [Fact]
    public void RecoveryStateMachine_ShouldRequireKernelValidationWhenCheckpointExists()
    {
        var result = CreateBlockedRuntimeExecutionResult();
        var summary = new KernelTraceSummary(
            result.KernelResult.RunId,
            eventCount: 4,
            checkpointRefs: ["Checkpoint policy materialized for graph graph-loop."]);

        var decision = KernelRuntimeRecoveryStateMachine.Evaluate(result, summary);

        Assert.Equal(KernelRuntimeRecoveryDisposition.RecoveryCandidatePendingKernelValidation, decision.Disposition);
        Assert.Equal("runtime.blocked.test", decision.FailureCode);
        Assert.Equal("graph-loop", decision.RollbackTargetRef);
        Assert.Single(decision.CheckpointRefs);
    }

    [Fact]
    public async Task RunAsync_ShouldNotCallRuntimeWhenKernelRejected()
    {
        var kernelResult = CreateKernelResult(KernelValidationDecision.Rejected, executionPlan: null);
        var kernel = new RecordingKernelCore(kernelResult);
        var runtime = RecordingExecutionRuntime.Create(CreateRuntimeResult(RuntimeStepResultStatus.Succeeded));
        var loop = new AdaptiveRuntimeExecutionLoop(kernel, runtime.Instance);

        var result = await loop.RunAsync(
            CreateIntent(),
            CreateOptions(executeRuntimePlan: true),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.KernelRejected, result.Disposition);
        Assert.Null(result.RuntimeResult);
        Assert.Equal(0, runtime.ExecuteCallCount);
    }

    [Fact]
    public async Task RunAsync_ShouldKeepApprovalOnlyWhenRuntimeExecutionDisabled()
    {
        var plan = CreatePlan();
        var kernelResult = CreateKernelResult(KernelValidationDecision.Approved, plan);
        var kernel = new RecordingKernelCore(kernelResult);
        var runtime = RecordingExecutionRuntime.Create(CreateRuntimeResult(RuntimeStepResultStatus.Succeeded));
        var loop = new AdaptiveRuntimeExecutionLoop(kernel, runtime.Instance);

        var result = await loop.RunAsync(
            CreateIntent(),
            CreateOptions(executeRuntimePlan: false),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.ApprovalOnly, result.Disposition);
        Assert.Null(result.RuntimeResult);
        Assert.Equal(0, runtime.ExecuteCallCount);
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteApprovedPlanAndAlignRuntimeContextToKernelRun()
    {
        var plan = CreatePlan();
        var kernelRunId = new KernelRunId("run-kernel-approved");
        var kernelResult = CreateKernelResult(KernelValidationDecision.Approved, plan, kernelRunId);
        var kernel = new RecordingKernelCore(kernelResult);
        var runtime = RecordingExecutionRuntime.Create(CreateRuntimeResult(RuntimeStepResultStatus.Succeeded));
        var loop = new AdaptiveRuntimeExecutionLoop(kernel, runtime.Instance);

        var result = await loop.RunAsync(
            CreateIntent(),
            CreateOptions(executeRuntimePlan: true, runtimeKernelRunId: new KernelRunId("run-runtime-stale")),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.NotNull(result.RuntimeResult);
        Assert.Equal(1, runtime.ExecuteCallCount);
        Assert.Same(plan, runtime.LastPlan);
        Assert.NotNull(runtime.LastContext);
        Assert.Equal(kernelRunId, runtime.LastContext!.KernelRunId);
        Assert.Equal(kernelResult.TraceId, result.KernelTraceId);
        Assert.Equal("trace://execution/execution-loop/result", result.RuntimeTraceRef);
        Assert.Equal("diagnostics://execution/execution-loop/result", result.DiagnosticsRef);
    }

    [Fact]
    public async Task RunAsync_ShouldReturnRuntimeBlockedDispositionWhenRuntimeBlocks()
    {
        var kernelResult = CreateKernelResult(KernelValidationDecision.Approved, CreatePlan());
        var kernel = new RecordingKernelCore(kernelResult);
        var runtime = RecordingExecutionRuntime.Create(CreateRuntimeResult(RuntimeStepResultStatus.Blocked));
        var loop = new AdaptiveRuntimeExecutionLoop(kernel, runtime.Instance);

        var result = await loop.RunAsync(
            CreateIntent(),
            CreateOptions(executeRuntimePlan: true),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeBlocked, result.Disposition);
        Assert.NotNull(result.RuntimeResult);
        Assert.Equal(RuntimeStepResultStatus.Blocked, result.RuntimeResult!.Status);
    }

    [Fact]
    public async Task RunAsync_ShouldReturnRuntimeFailedDispositionForFailedRuntimeStatus()
    {
        var kernelResult = CreateKernelResult(KernelValidationDecision.Approved, CreatePlan());
        var kernel = new RecordingKernelCore(kernelResult);
        var runtime = RecordingExecutionRuntime.Create(CreateRuntimeResult(RuntimeStepResultStatus.Failed));
        var loop = new AdaptiveRuntimeExecutionLoop(kernel, runtime.Instance);

        var result = await loop.RunAsync(
            CreateIntent(),
            CreateOptions(executeRuntimePlan: true),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeFailed, result.Disposition);
        Assert.NotNull(result.RuntimeResult);
    }

    private static KernelRuntimeExecutionOptions CreateOptions(
        bool executeRuntimePlan,
        KernelRunId? runtimeKernelRunId = null)
        => new(
            new KernelRunOptions(runId: null, enableAdaptive: false, requireHumanGate: false),
            new ExecutionRuntimeContext(
                new ExecutionId("execution-loop"),
                runtimeKernelRunId ?? new KernelRunId("run-loop"),
                CreateGovernance()),
            executeRuntimePlan);

    private static KernelRunResult CreateKernelResult(
        KernelValidationDecision decision,
        ExecutionPlan? executionPlan,
        KernelRunId? runId = null)
    {
        var validation = new KernelValidationResult(
            decision,
            decision == KernelValidationDecision.Approved
                ? Array.Empty<KernelValidationIssue>()
                :
                [
                    new KernelValidationIssue("kernel.rejected.test", "Kernel rejected by test fixture."),
                ]);
        var finalState = decision == KernelValidationDecision.Approved
            ? KernelRunLifecycleState.Executing
            : KernelRunLifecycleState.Failed;

        return new KernelRunResult(
            runId ?? new KernelRunId("run-loop"),
            SourceIntentId,
            finalState,
            validation,
            executionPlan,
            new KernelTraceId("trace-loop"));
    }

    private static ExecutionRunResult CreateRuntimeResult(RuntimeStepResultStatus status)
        => new(
            "plan-loop",
            new ExecutionId("execution-loop"),
            status,
            status == RuntimeStepResultStatus.Succeeded
                ?
                [
                    new RuntimeStepResult(
                        "step-loop",
                        RuntimeStepKind.Diagnostic,
                        RuntimeStepResultStatus.Succeeded,
                        StructuredValue.FromPlainObject(new Dictionary<string, object?>
                        {
                            ["ok"] = true,
                        }),
                        diagnosticsRef: "diagnostics://execution/execution-loop/step-loop",
                        traceRef: "trace://execution/execution-loop/step-loop"),
                ]
                :
                [
                    new RuntimeStepResult(
                        "step-loop",
                        RuntimeStepKind.Diagnostic,
                        status,
                        failure: new ExecutionFailure("runtime.test", "Runtime status provided by test fixture.")),
                ],
            "diagnostics://execution/execution-loop/result",
            "trace://execution/execution-loop/result");

    private static RuntimeMetricsEvent CreateMetricsEvent(KernelRuntimeExecutionResult result)
        => new(
            "metrics-runtime-loop",
            result.KernelResult.RunId.Value,
            result.RuntimeResult!.ExecutionId.Value,
            result.KernelResult.ExecutionPlan!.PlanId,
            result.KernelResult.ExecutionPlan.SourceGraphId.Value,
            "stage-loop",
            "step-loop",
            "gpt-5",
            attemptIndex: 1,
            reviseRound: null,
            new TokenUsageSnapshot(
                Available: true,
                MissingReason: null,
                Estimated: false,
                InputTokens: 10,
                CachedInputTokens: null,
                OutputTokens: 20,
                ReasoningOutputTokens: null,
                TotalTokens: 30,
                Source: "provider.completion.usage"),
            new RuntimeCostSnapshot(false, "price_model_missing", null, null, null),
            modelCallCount: 1,
            TimeSpan.FromMilliseconds(1),
            ["price_model_missing"]);

    private static KernelRuntimeExecutionResult CreateBlockedRuntimeExecutionResult()
    {
        var kernelResult = CreateKernelResult(KernelValidationDecision.Approved, CreatePlan());
        var runtimeResult = new ExecutionRunResult(
            "plan-loop",
            new ExecutionId("execution-loop"),
            RuntimeStepResultStatus.Blocked,
            [
                new RuntimeStepResult(
                    "step-loop",
                    RuntimeStepKind.Diagnostic,
                    RuntimeStepResultStatus.Blocked,
                    failure: new ExecutionFailure("runtime.blocked.test", "Runtime blocked by test fixture.")),
            ],
            "diagnostics://execution/execution-loop/result",
            "trace://execution/execution-loop/result");

        return new KernelRuntimeExecutionResult(
            kernelResult,
            runtimeResult,
            KernelRuntimeExecutionDisposition.RuntimeBlocked,
            kernelResult.TraceId,
            runtimeResult.TraceRef,
            runtimeResult.DiagnosticsRef);
    }

    private static ExecutionPlan CreatePlan()
        => new(
            "plan-loop",
            SourceGraphId,
            SourceIntentId,
            [CreateDiagnosticStep()],
            new ExecutionPlanPolicy(),
            new TracePolicy());

    private static DiagnosticStep CreateDiagnosticStep()
        => new(
            "step-loop",
            SourceIntentId,
            SourceGraphId,
            new StageId("stage-loop"),
            new KernelOperationId("operation-loop"),
            "diagnostic.test",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["message"] = "test",
            }),
            new PermissionEnvelope(requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.ReadOnly),
            new KernelBudget(tokenBudget: 100, timeBudgetMs: 1000, toolCallBudget: 1),
            new ContractRef("contract.loop.output", "1.0"),
            new TracePolicy());

    private static TurnIntent CreateIntent(
        int toolCallBudget = 1,
        GovernanceEnvelope? governance = null,
        MetadataBag? metadata = null)
        => new(
            SourceIntentId,
            new KernelSubjectRef(new SessionId("session-loop"), new ThreadId("thread-loop")),
            governance ?? CreateGovernance(),
            "input://loop",
            new KernelBudget(tokenBudget: 4_096, timeBudgetMs: 30_000, retryBudget: 5, toolCallBudget: toolCallBudget),
            metadata);

    private static IReadOnlyList<string> ResolveSignalsForStage(ExecutionPlan plan, IReadOnlyList<string>? modelSignals = null)
        => plan.Steps[0].SourceStageId.Value switch
        {
            "stage.prepare-context" => ["context.prepared"],
            "stage.model-reason" => modelSignals ?? ["model_final_response"],
            "stage.tool-exec" => ["tool.results.materialized"],
            "stage.finalize" => ["turn.finalized"],
            _ => ["stage.succeeded"],
        };

    private static ExecutionRunResult CreateStageRuntimeResult(ExecutionPlan plan, IReadOnlyList<string> signals)
        => CreateStageRuntimeResult(
            plan,
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["signals"] = signals,
            }));

    private static ExecutionRunResult CreateStageRuntimeResult(ExecutionPlan plan, StructuredValue output)
        => new(
            plan.PlanId,
            new ExecutionId($"execution-{plan.PlanId}"),
            RuntimeStepResultStatus.Succeeded,
            plan.Steps.Select(step => new RuntimeStepResult(
                step.StepId,
                step.StepKind,
                RuntimeStepResultStatus.Succeeded,
                output,
                diagnosticsRef: $"diagnostics://execution/{plan.PlanId}/{step.StepId}",
                traceRef: $"trace://execution/{plan.PlanId}/{step.StepId}")).ToArray(),
            $"diagnostics://execution/{plan.PlanId}/result",
            $"trace://execution/{plan.PlanId}/result");

    private static StructuredValue CreateToolRequestOutput(string callId, string toolId, string operation)
        => CreateToolRequestsOutput((callId, toolId, operation, new Dictionary<string, object?>()));

    private static StructuredValue CreateToolRequestsOutput(params (string CallId, string ToolId, string Operation, object? Input)[] requests)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["signals"] = new[] { "tool_requests_available" },
            ["toolRequests"] = requests
                .Select(request => new Dictionary<string, object?>
                {
                    ["callId"] = request.CallId,
                    ["toolId"] = request.ToolId,
                    ["operation"] = request.Operation,
                    ["input"] = request.Input,
                })
                .ToArray(),
        });

    private static ExecutionRunResult CreateToolResultRuntimeResult(ExecutionPlan plan, string? callIdOverride = null)
        => new(
            plan.PlanId,
            new ExecutionId($"execution-{plan.PlanId}"),
            RuntimeStepResultStatus.Succeeded,
            plan.Steps.Select(step =>
            {
                var toolStep = (ToolInvocationStep)step;
                var callId = callIdOverride ?? toolStep.InputEnvelope.CallId.Value;
                return new RuntimeStepResult(
                    step.StepId,
                    step.StepKind,
                    RuntimeStepResultStatus.Succeeded,
                    StructuredValue.FromPlainObject(new Dictionary<string, object?>
                    {
                        ["signals"] = new[] { "tool.results.materialized" },
                        ["toolResults"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["callId"] = callId,
                                ["toolId"] = toolStep.InputEnvelope.ToolId,
                                ["status"] = "succeeded",
                                ["output"] = new Dictionary<string, object?>
                                {
                                    ["echoOperation"] = toolStep.InputEnvelope.Operation,
                                },
                            },
                        },
                    }),
                    diagnosticsRef: $"diagnostics://execution/{plan.PlanId}/{step.StepId}",
                    traceRef: $"trace://execution/{plan.PlanId}/{step.StepId}");
            }).ToArray(),
            $"diagnostics://execution/{plan.PlanId}/result",
            $"trace://execution/{plan.PlanId}/result");

    private static IReadOnlyList<string> ReadSignals(StructuredValue output)
    {
        if (!output.TryGetProperty("signals", out var signals))
        {
            return Array.Empty<string>();
        }

        return signals!.Items
            .Select(static item => item.StringValue)
            .Where(static signal => !string.IsNullOrWhiteSpace(signal))
            .Select(static signal => signal!)
            .ToArray();
    }

    private static GovernanceEnvelope CreateGovernance()
        => new(
            "governance-loop",
            allowedToolIds:
            [
                "kernel.request_capability_call",
                "module.core_loop",
                "tool.test",
                "update_context_policy",
                "request_capability_call",
                "read_file",
                "list_dir",
                "grep",
                "glob",
                "apply_patch",
                "write",
                "memory_search",
                "artifacts",
            ],
            allowedModuleIds: ["kernel.default", "provider.default", "module.test"],
            maxSideEffectLevel: SideEffectLevel.Privileged,
            requiresHumanGate: false);

    private static GovernanceEnvelope CreateCliBridgeGovernance()
        => new(
            "governance-cli-bridge-test",
            allowedToolIds:
            [
                "kernel.request_capability_call",
                "update_context_policy",
                "request_capability_call",
                "module.core_loop",
                "read_file",
                "list_dir",
                "grep",
                "glob",
            ],
            allowedModuleIds:
            [
                "kernel.default",
                "provider.default",
                "module.context",
                "module.memory",
                "module.artifact",
                "module.diagnostics",
            ],
            maxSideEffectLevel: SideEffectLevel.ExternalNetwork,
            requiresHumanGate: false);

    private static GovernanceEnvelope CreateCliBridgeGovernanceWithWorkspaceWrite()
        => new(
            "governance-cli-bridge-write-test",
            allowedToolIds:
            [
                "kernel.request_capability_call",
                "update_context_policy",
                "request_capability_call",
                "module.core_loop",
                "read_file",
                "list_dir",
                "grep",
                "glob",
                "write",
            ],
            allowedModuleIds:
            [
                "kernel.default",
                "provider.default",
                "module.context",
                "module.memory",
                "module.artifact",
                "module.diagnostics",
            ],
            maxSideEffectLevel: SideEffectLevel.ExternalNetwork,
            requiresHumanGate: true,
            approvalIds: [new ApprovalId("approval-cli-kernel-write-test")]);

    private static GovernanceEnvelope CreateCliBridgeGovernanceWithSubAgents()
        => new(
            "governance-cli-bridge-subagent-test",
            allowedToolIds:
            [
                "kernel.request_capability_call",
                "update_context_policy",
                "request_capability_call",
                "module.core_loop",
                "read_file",
                "list_dir",
                "grep",
                "glob",
                "spawn_agent",
            ],
            allowedModuleIds:
            [
                "kernel.default",
                "provider.default",
                "module.context",
                "module.memory",
                "module.artifact",
                "module.diagnostics",
                "module.sub_agent",
            ],
            maxSideEffectLevel: SideEffectLevel.HostMutation,
            requiresHumanGate: true,
            approvalIds: [new ApprovalId("approval-cli-kernel-subagent-test")]);

    private static ResolvedTianShuConfig CreateConfiguredProviderConfig(
        string envKey,
        string baseUrl = "https://provider.test/v1",
        string wireApi = ProviderWireApi.Responses)
        => new()
        {
            Model = "gpt-test",
            ModelProvider = "openai",
            ProviderBaseUrl = baseUrl,
            ProviderEnvKey = envKey,
            ProviderWireApi = wireApi,
            ProtocolAdapter = wireApi,
        };

    private static string BuildRuntimeFailureMessage(KernelRuntimeExecutionResult result)
    {
        var stages = string.Join(", ", result.KernelResult.StageResults.Select(stage =>
            $"{stage.StageId.Value}:{stage.Status}/{stage.RuntimeStatus}"));
        var steps = string.Join(", ", result.RuntimeResult?.StepResults.Select(step =>
            $"{step.StepId}:{step.StepKind}:{step.Status}:{step.Failure?.Code ?? "-"}:{FormatFailureDetails(step.Failure)}") ?? []);

        return $"Expected RuntimeCompleted but was {result.Disposition}. Stages=[{stages}] Steps=[{steps}]";
    }

    private static void AssertW3CTraceParent(string traceParent)
    {
        var parts = traceParent.Split('-');
        Assert.Equal(4, parts.Length);
        Assert.Equal("00", parts[0]);
        Assert.Equal(32, parts[1].Length);
        Assert.Equal(16, parts[2].Length);
        Assert.Equal("01", parts[3]);
        Assert.All(parts[1] + parts[2], character =>
            Assert.True(Uri.IsHexDigit(character), $"Expected hexadecimal traceparent, got `{traceParent}`."));
    }

    private static string FormatFailureDetails(ExecutionFailure? failure)
        => failure?.Details is null
            ? "-"
            : JsonSerializer.Serialize(failure.Details.ToPlainObject());

    private static string BuildHostControlThreadIndexPath(string workspacePath, string threadId)
        => System.IO.Path.Combine(
            workspacePath,
            ".tianshu",
            "kernel-runtime",
            "host-control",
            "active-runs",
            "by-thread",
            threadId + ".json");

    private static string BuildSse(params object[] events)
    {
        var builder = new StringBuilder();
        foreach (var streamEvent in events)
        {
            builder.Append("data: ");
            builder.Append(JsonSerializer.Serialize(streamEvent));
            builder.Append("\n\n");
        }

        builder.Append("data: [DONE]\n\n");
        return builder.ToString();
    }

    private static readonly CoreIntentId SourceIntentId = new("intent-loop");
    private static readonly StageGraphId SourceGraphId = new("graph-loop");

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tianshu-kernel-loop-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class DelayedProviderServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource shutdown = new();
        private readonly TaskCompletionSource requestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task acceptTask;
        private HttpListenerContext? context;

        public DelayedProviderServer()
        {
            var port = GetFreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            listener.Prefixes.Add($"{BaseUrl}/");
            listener.Start();
            acceptTask = AcceptAsync();
        }

        public string BaseUrl { get; }

        public async Task WaitForRequestAsync(TimeSpan timeout)
        {
            await requestReceived.Task.WaitAsync(timeout);
        }

        public void Dispose()
        {
            try
            {
                context?.Response.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            listener.Close();
            shutdown.Cancel();
            shutdown.Dispose();
            try
            {
                acceptTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }
        }

        private async Task AcceptAsync()
        {
            try
            {
                context = await listener.GetContextAsync();
                requestReceived.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
            }
            catch (ObjectDisposedException)
            {
                requestReceived.TrySetCanceled();
            }
            catch (HttpListenerException)
            {
                requestReceived.TrySetCanceled();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static int GetFreePort()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }
    }

    private sealed class SequencedProviderSseHandler : HttpMessageHandler
    {
        private readonly Queue<string> responses;

        public SequencedProviderSseHandler(params string[] responses)
        {
            this.responses = new Queue<string>(responses);
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            if (responses.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("No more SSE responses."),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private sealed class RecordingKernelCore : IStableKernelCore
    {
        private readonly KernelRunResult result;

        public RecordingKernelCore(KernelRunResult result)
        {
            this.result = result;
        }

        public KernelRunOptions? LastOptions { get; private set; }

        public Task<KernelRunResult> RunAsync(CoreIntent intent, KernelRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(result);
        }

        public Task<KernelValidationResult> ReviewProposalAsync(KernelValidationContext context, KernelProposal proposal, CancellationToken cancellationToken = default)
            => Task.FromResult(new KernelValidationResult(KernelValidationDecision.Approved));

        public Task<KernelValidationResult> ReviewOperationAsync(KernelValidationContext context, KernelOperation operation, CancellationToken cancellationToken = default)
            => Task.FromResult(new KernelValidationResult(KernelValidationDecision.Approved));

        public Task<KernelValidationResult> ReviewExecutionPlanAsync(KernelValidationContext context, ExecutionPlan executionPlan, CancellationToken cancellationToken = default)
            => Task.FromResult(new KernelValidationResult(KernelValidationDecision.Approved));
    }

    private class RecordingExecutionRuntime : DispatchProxy
    {
        private Func<ExecutionPlan, ExecutionRunResult>? resultFactory;

        public IExecutionRuntime Instance { get; private set; } = null!;

        public int ExecuteCallCount { get; private set; }

        public ExecutionPlan? LastPlan { get; private set; }

        public ExecutionRuntimeContext? LastContext { get; private set; }

        public IReadOnlyList<ExecutionPlan> Plans => plans;

        private readonly List<ExecutionPlan> plans = [];

        public static RecordingExecutionRuntime Create(ExecutionRunResult result)
            => Create(_ => result);

        public static RecordingExecutionRuntime Create(Func<ExecutionPlan, ExecutionRunResult> resultFactory)
        {
            var proxy = Create<IExecutionRuntime, RecordingExecutionRuntime>();
            var recording = (RecordingExecutionRuntime)(object)proxy;
            recording.Instance = proxy;
            recording.resultFactory = resultFactory;
            return recording;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IExecutionRuntime.ExecuteAsync))
            {
                ExecuteCallCount++;
                LastPlan = (ExecutionPlan)args![0]!;
                LastContext = (ExecutionRuntimeContext)args[1]!;
                plans.Add(LastPlan);
                return Task.FromResult(resultFactory!(LastPlan));
            }

            if (targetMethod?.Name == nameof(IAsyncDisposable.DisposeAsync))
            {
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException($"Unexpected runtime member call: {targetMethod?.Name}");
        }
    }

    private sealed class RecordingMetricsSink : IExecutionRuntimeMetricsSink
    {
        public List<RuntimeMetricsEvent> Events { get; } = [];

        public ValueTask RecordAsync(RuntimeMetricsEvent metricsEvent, CancellationToken cancellationToken)
        {
            Events.Add(metricsEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BridgeLoopProviderModule : IProviderModule
    {
        public BridgeLoopProviderModule(string providerId)
        {
            Descriptor = new ProviderDescriptor(
                providerId,
                "Bridge Loop Provider",
                ProviderProtocolKind.OpenAiResponses,
                new ProviderCapabilityProfile(SupportsStreaming: true),
                [new TianShu.Contracts.Provider.ProviderModelDescriptor("model-name")]);
        }

        public ProviderDescriptor Descriptor { get; }

        public int InvokeCount { get; private set; }

        public ProviderInvocationRequest? FirstRequest { get; private set; }

        public ProviderInvocationRequest? SecondRequest { get; private set; }

        public IReadOnlyList<ToolResultProviderInputItem> SecondRequestToolResults { get; private set; } = [];

        public async IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
            ProviderInvocationRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            InvokeCount++;
            await Task.Yield();
            if (InvokeCount == 1)
            {
                FirstRequest = request;
                yield return new ProviderToolDirectiveEvent(new ProviderToolDirective(
                    new CallId("call-bridge-read"),
                    "read_file",
                    StructuredValue.FromPlainObject(new Dictionary<string, object?>
                    {
                        ["operation"] = "read",
                        ["path"] = "README.md",
                    })));
                yield break;
            }

            SecondRequest = request;
            SecondRequestToolResults = request.Inputs.OfType<ToolResultProviderInputItem>().ToArray();
            yield return new ProviderCompletionEvent(new ProviderCompletion("bridge final", new ProviderUsage(7, 5)));
        }
    }

    private sealed class BridgeLoopTool : ITianShuTool
    {
        public BridgeLoopTool(string toolId)
        {
            Descriptor = new ToolDescriptor(
                toolId,
                "Bridge Loop Tool",
                "Records bridge-loop invocations.",
                inputSchemaRef: new JsonSchemaRef("schema.bridge.tool.input"),
                outputSchemaRef: new JsonSchemaRef("schema.bridge.tool.output"),
                permissions: new PermissionDeclaration([toolId], requiresHumanGate: false),
                sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly),
                audit: new AuditProfile(eventKinds: ["bridge.tool.invoked"]));
        }

        public ToolDescriptor Descriptor { get; }

        public int InvokeCount { get; private set; }

        public ToolInvocationEnvelope? LastInvocation { get; private set; }

        public ToolInvocationContext? LastContext { get; private set; }

        public ValueTask<ToolInvocationResult> InvokeAsync(
            ToolInvocationEnvelope invocation,
            ToolInvocationContext context,
            CancellationToken cancellationToken)
        {
            InvokeCount++;
            LastInvocation = invocation;
            LastContext = context;
            return ValueTask.FromResult(new ToolInvocationResult(
                invocation.CallId,
                invocation.ToolId,
                [new ToolStreamItem(
                    "text",
                    StructuredValue.FromPlainObject(new Dictionary<string, object?>
                    {
                        ["summary"] = "bridge-read-ok",
                        ["path"] = invocation.Input.GetProperty("path").GetString(),
                    }),
                    isTerminal: true)]));
        }
    }

    private sealed class RetryThenSuccessProviderModule : IProviderModule
    {
        private readonly int failuresBeforeSuccess;

        public RetryThenSuccessProviderModule(string providerId, int failuresBeforeSuccess)
        {
            this.failuresBeforeSuccess = failuresBeforeSuccess;
            Descriptor = new ProviderDescriptor(
                providerId,
                "Retry Provider",
                ProviderProtocolKind.OpenAiResponses,
                new ProviderCapabilityProfile(SupportsStreaming: true),
                [new TianShu.Contracts.Provider.ProviderModelDescriptor("model-name")]);
        }

        public ProviderDescriptor Descriptor { get; }

        public int InvokeCount { get; private set; }

        public async IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
            ProviderInvocationRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            InvokeCount++;
            await Task.Yield();
            if (InvokeCount <= failuresBeforeSuccess)
            {
                yield return new ProviderFailureEvent(new ProviderFailure(
                    "provider_transient_failure",
                    "Transient provider failure.",
                    isRetryable: true));
                yield break;
            }

            yield return new ProviderCompletionEvent(new ProviderCompletion("done", new ProviderUsage(2, 3)));
        }
    }
}
