using System.Reflection;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Execution.Runtime;
using TianShu.Kernel;
using TianShu.Kernel.Abstractions;
using TianShu.RuntimeComposition;

namespace TianShu.Execution.Runtime.Tests;

public sealed class AdaptiveRuntimeSubAgentMaterializationTests
{
    [Fact]
    public async Task RunReactiveAsync_ShouldMaterializeSpawnAgentAsModuleCapabilityStep()
    {
        var modelCalls = 0;
        ExecutionPlan? toolPlan = null;
        var runtime = RecordingExecutionRuntime.Create(plan =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCalls++;
                return modelCalls == 1
                    ? CreateStageRuntimeResult(plan, CreateSpawnToolRequestOutput())
                    : CreateStageRuntimeResult(plan, CreateModelFinalOutput());
            }

            if (stageId == "stage.tool-exec")
            {
                toolPlan = plan;
                return CreateStageRuntimeResult(plan, CreateSpawnToolResultOutput("succeeded"));
            }

            return CreateStageRuntimeResult(plan, CreateSignalsOutput($"{stageId}.succeeded"));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(new StableKernelCore(), runtime.Instance);

        var result = await loop.RunReactiveAsync(CreateIntent(), CreateOptions(), CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.NotNull(toolPlan);
        var step = Assert.Single(toolPlan!.Steps);
        var moduleStep = Assert.IsType<ModuleCapabilityStep>(step);
        Assert.Equal("module.sub_agent", moduleStep.ModuleId);
        Assert.Equal("sub_agent.spawn", moduleStep.CapabilityId);
        Assert.True(moduleStep.Metadata.TryGetValue("subAgent.childLineage", out _));
        Assert.Equal(["stage.prepare-context", "stage.model-reason", "stage.tool-exec", "stage.model-reason", "stage.finalize"], result.KernelResult.StageResults.Select(stage => stage.StageId.Value).ToArray());
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldReturnBlockedToolResultWhenSpawnAdmissionIsDenied()
    {
        var modelCalls = 0;
        ExecutionPlan? toolPlan = null;
        IReadOnlyList<ToolResultProviderInputItem> secondModelToolResults = [];
        var runtime = RecordingExecutionRuntime.Create(plan =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCalls++;
                if (modelCalls == 2)
                {
                    var modelStep = Assert.IsType<ModelInvocationStep>(Assert.Single(plan.Steps));
                    secondModelToolResults = modelStep.InputEnvelope.Inputs.OfType<ToolResultProviderInputItem>().ToArray();
                    return CreateStageRuntimeResult(plan, CreateModelFinalOutput());
                }

                return CreateStageRuntimeResult(plan, CreateSpawnToolRequestOutput());
            }

            if (stageId == "stage.tool-exec")
            {
                toolPlan = plan;
                Assert.DoesNotContain(plan.Steps, step => step is ModuleCapabilityStep);
                return CreateStageRuntimeResult(plan, CreateSignalsOutput("tool.results.materialized"));
            }

            return CreateStageRuntimeResult(plan, CreateSignalsOutput($"{stageId}.succeeded"));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(
            new StableKernelCore(),
            runtime.Instance,
            subAgentSpawnQuota: new SubAgentSpawnQuota(maxSpawnDepth: 1, maxFanoutPerAgent: 0, maxTreeNodes: 32, maxConcurrentAgents: 0));

        var result = await loop.RunReactiveAsync(CreateIntent(), CreateOptions(), CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.NotNull(toolPlan);
        var toolResult = Assert.Single(secondModelToolResults);
        Assert.Equal("call-spawn-001", toolResult.CallId.Value);
        Assert.Equal("blocked", toolResult.Result.GetProperty("status").GetString());
        Assert.Equal("subagent.fanout_exceeded", toolResult.Result.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldExecuteSpawnAgentRequestsThroughBoundedParallelFanout()
    {
        var modelCalls = 0;
        var activeChildren = 0;
        var maxActiveChildren = 0;
        var childPlans = new System.Collections.Concurrent.ConcurrentBag<ExecutionPlan>();
        IReadOnlyList<ToolResultProviderInputItem> secondModelToolResults = [];
        var runtime = RecordingExecutionRuntime.CreateAsync(async plan =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCalls++;
                if (modelCalls == 2)
                {
                    var modelStep = Assert.IsType<ModelInvocationStep>(Assert.Single(plan.Steps));
                    secondModelToolResults = modelStep.InputEnvelope.Inputs.OfType<ToolResultProviderInputItem>().ToArray();
                    return CreateStageRuntimeResult(plan, CreateModelFinalOutput());
                }

                return CreateStageRuntimeResult(plan, CreateSpawnToolRequestOutput("call-spawn-001", "call-spawn-002", "call-spawn-003"));
            }

            if (stageId == "stage.tool-exec" && plan.Steps[0] is ModuleCapabilityStep moduleStep)
            {
                childPlans.Add(plan);
                var active = Interlocked.Increment(ref activeChildren);
                UpdateMax(ref maxActiveChildren, active);
                await Task.Delay(75);
                Interlocked.Decrement(ref activeChildren);

                var callId = moduleStep.Metadata.TryGetValue("modelToolCallId", out var callIdValue)
                    ? callIdValue.GetString()!
                    : "call-spawn-unknown";
                return CreateStageRuntimeResult(plan, CreateSpawnToolResultOutput(callId, "succeeded"));
            }

            return CreateStageRuntimeResult(plan, CreateSignalsOutput($"{stageId}.succeeded"));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(
            new StableKernelCore(),
            runtime.Instance,
            subAgentSpawnQuota: new SubAgentSpawnQuota(maxSpawnDepth: 1, maxFanoutPerAgent: 4, maxTreeNodes: 8, maxConcurrentAgents: 2));

        var result = await loop.RunReactiveAsync(CreateIntent(), CreateOptions(), CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.Equal(3, childPlans.Count);
        Assert.True(maxActiveChildren > 1);
        Assert.True(maxActiveChildren <= 2);
        Assert.Equal(["call-spawn-001", "call-spawn-002", "call-spawn-003"], secondModelToolResults.Select(result => result.CallId.Value).OrderBy(static value => value, StringComparer.Ordinal).ToArray());
        var fanoutDiagnostics = Assert.Single(result.RuntimeResult!.StepResults, step =>
            step.Output is not null
            && step.Output.TryGetProperty("signals", out var signals)
            && signals is not null
            && signals.Items.Any(signal => string.Equals(signal.GetString(), "subagent.fanout.diagnostics", StringComparison.Ordinal)));
        Assert.Equal(3, int.Parse(fanoutDiagnostics.Output!.GetProperty("plannedSubTaskCount").NumberValue!));
        Assert.Equal(2, int.Parse(fanoutDiagnostics.Output.GetProperty("maxConcurrentAgents").NumberValue!));
        Assert.Equal(3, fanoutDiagnostics.Output.GetProperty("jobs").Items.Count);
        Assert.All(childPlans, plan =>
        {
            var step = Assert.IsType<ModuleCapabilityStep>(Assert.Single(plan.Steps));
            Assert.True(step.Metadata.TryGetValue("subAgent.fanout.allocatedBudget", out _));
            Assert.True(step.Metadata.TryGetValue("subAgent.fanout.callId", out _));
        });
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldFanInSubAgentResultsAndExposeSummaryToParentModel()
    {
        var modelCalls = 0;
        IReadOnlyList<ToolResultProviderInputItem> secondModelToolResults = [];
        var runtime = RecordingExecutionRuntime.Create(plan =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCalls++;
                if (modelCalls == 2)
                {
                    var modelStep = Assert.IsType<ModelInvocationStep>(Assert.Single(plan.Steps));
                    secondModelToolResults = modelStep.InputEnvelope.Inputs.OfType<ToolResultProviderInputItem>().ToArray();
                    return CreateStageRuntimeResult(plan, CreateModelFinalOutput());
                }

                return CreateStageRuntimeResult(plan, CreateSpawnToolRequestOutput("call-spawn-001", "call-spawn-002", "call-spawn-003"));
            }

            if (stageId == "stage.tool-exec" && plan.Steps[0] is ModuleCapabilityStep moduleStep)
            {
                var callId = moduleStep.Metadata.TryGetValue("modelToolCallId", out var callIdValue)
                    ? callIdValue.GetString()!
                    : "call-spawn-unknown";
                return callId switch
                {
                    "call-spawn-001" => CreateStageRuntimeResult(
                        plan,
                        CreateSpawnToolResultOutput(
                            callId,
                            "succeeded",
                            "child says yes",
                            claims: new Dictionary<string, object?> { ["decision"] = "yes" })),
                    "call-spawn-002" => CreateStageRuntimeResult(
                        plan,
                        CreateSpawnToolResultOutput(
                            callId,
                            "succeeded",
                            "child says no",
                            claims: new Dictionary<string, object?> { ["decision"] = "no" })),
                    _ => CreateStageRuntimeResult(
                        plan,
                        CreateSpawnToolResultOutput(
                            callId,
                            "failed",
                            resultText: null,
                            failureCode: "subagent.child_failed",
                            failureMessage: "child failed deterministically")),
                };
            }

            return CreateStageRuntimeResult(plan, CreateSignalsOutput($"{stageId}.succeeded"));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(
            new StableKernelCore(),
            runtime.Instance,
            subAgentSpawnQuota: new SubAgentSpawnQuota(maxSpawnDepth: 1, maxFanoutPerAgent: 4, maxTreeNodes: 8, maxConcurrentAgents: 2));

        var result = await loop.RunReactiveAsync(CreateIntent(), CreateOptions(), CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.Equal(3, secondModelToolResults.Count);
        var fanInSummary = secondModelToolResults[0].Result
            .GetProperty("output")
            .GetProperty("subAgentFanInSummary");
        Assert.Equal("CompletedWithFailures", fanInSummary.GetProperty("status").GetString());
        Assert.Equal(3, fanInSummary.GetProperty("results").Items.Count);
        Assert.Single(fanInSummary.GetProperty("conflicts").Items);
        Assert.Contains("conflicts=1", fanInSummary.GetProperty("summaryText").GetString(), StringComparison.Ordinal);
        Assert.Contains("call-spawn-003:subagent.child_failed", fanInSummary.GetProperty("summaryText").GetString(), StringComparison.Ordinal);

        var treeDiagnostics = secondModelToolResults[0].Result
            .GetProperty("output")
            .GetProperty("subAgentTreeDiagnostics");
        Assert.Equal("run-subagent-materialization", treeDiagnostics.GetProperty("rootRunId").GetString());
        Assert.Equal("ledger-run-subagent-materialization", treeDiagnostics.GetProperty("ledgerRef").GetString());
        Assert.Equal(4, treeDiagnostics.GetProperty("nodes").Items.Count);
        Assert.Equal(3, treeDiagnostics.GetProperty("edges").Items.Count);
        Assert.Contains("artifacts=3", treeDiagnostics.GetProperty("reportText").GetString(), StringComparison.Ordinal);
        Assert.Contains("subagent.child_failed", treeDiagnostics.GetProperty("reportText").GetString(), StringComparison.Ordinal);

        var failedChild = Assert.Single(
            fanInSummary.GetProperty("results").Items,
            item => string.Equals(item.GetProperty("spawnCallId").GetString(), "call-spawn-003", StringComparison.Ordinal));
        Assert.Equal("Failed", failedChild.GetProperty("status").GetString());
        Assert.Equal("subagent.child_failed", failedChild.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal("artifact://child/call-spawn-003/report", Assert.Single(failedChild.GetProperty("artifactRefs").Items).GetString());
        var failedNode = Assert.Single(
            treeDiagnostics.GetProperty("nodes").Items,
            item => string.Equals(item.GetProperty("runId").GetString(), "run-child-call-spawn-003", StringComparison.Ordinal));
        Assert.Equal("Failed", failedNode.GetProperty("status").GetString());
        Assert.Equal("subagent.child_failed", failedNode.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal("artifact://child/call-spawn-003/report", Assert.Single(failedNode.GetProperty("artifactRefs").Items).GetString());
        var failedEdge = Assert.Single(
            treeDiagnostics.GetProperty("edges").Items,
            item => string.Equals(item.GetProperty("spawnCallId").GetString(), "call-spawn-003", StringComparison.Ordinal));
        Assert.Equal("run-subagent-materialization", failedEdge.GetProperty("parentRunId").GetString());
        Assert.Equal("run-child-call-spawn-003", failedEdge.GetProperty("childRunId").GetString());
        Assert.Equal("Failed", failedEdge.GetProperty("status").GetString());
        Assert.Equal("subagent.child_failed", failedEdge.GetProperty("failure").GetProperty("code").GetString());
        var fanInDiagnostics = Assert.Single(result.RuntimeResult!.StepResults, step =>
            step.Output is not null
            && step.Output.TryGetProperty("signals", out var signals)
            && signals is not null
            && signals.Items.Any(signal => string.Equals(signal.GetString(), "subagent.fanin.summary", StringComparison.Ordinal)));
        Assert.Equal("CompletedWithFailures", fanInDiagnostics.Output!.GetProperty("subAgentFanInSummary").GetProperty("status").GetString());
        Assert.Equal(4, fanInDiagnostics.Output.GetProperty("subAgentTreeDiagnostics").GetProperty("nodes").Items.Count);
        var fanoutDiagnostics = Assert.Single(result.RuntimeResult.StepResults, step =>
            step.Output is not null
            && step.Output.TryGetProperty("signals", out var signals)
            && signals is not null
            && signals.Items.Any(signal => string.Equals(signal.GetString(), "subagent.fanout.diagnostics", StringComparison.Ordinal)));
        Assert.Equal(3, fanoutDiagnostics.Output!.GetProperty("subAgentTreeDiagnostics").GetProperty("edges").Items.Count);
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldBlockFanoutItemsWhenSubTaskBudgetIsExceededWithoutStartingChildren()
    {
        var modelCalls = 0;
        var childPlans = new System.Collections.Concurrent.ConcurrentBag<ExecutionPlan>();
        IReadOnlyList<ToolResultProviderInputItem> secondModelToolResults = [];
        var runtime = RecordingExecutionRuntime.Create(plan =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCalls++;
                if (modelCalls == 2)
                {
                    var modelStep = Assert.IsType<ModelInvocationStep>(Assert.Single(plan.Steps));
                    secondModelToolResults = modelStep.InputEnvelope.Inputs.OfType<ToolResultProviderInputItem>().ToArray();
                    return CreateStageRuntimeResult(plan, CreateModelFinalOutput());
                }

                return CreateStageRuntimeResult(plan, CreateSpawnToolRequestOutput("call-spawn-001", "call-spawn-002", "call-spawn-003"));
            }

            if (stageId == "stage.tool-exec" && plan.Steps[0] is ModuleCapabilityStep)
            {
                childPlans.Add(plan);
                return CreateStageRuntimeResult(plan, CreateSpawnToolResultOutput("unexpected-child", "succeeded"));
            }

            return CreateStageRuntimeResult(plan, CreateSignalsOutput($"{stageId}.succeeded"));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(
            new StableKernelCore(),
            runtime.Instance,
            subAgentSpawnQuota: new SubAgentSpawnQuota(maxSpawnDepth: 1, maxFanoutPerAgent: 2, maxTreeNodes: 8, maxConcurrentAgents: 2));

        var result = await loop.RunReactiveAsync(CreateIntent(), CreateOptions(), CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.Empty(childPlans);
        Assert.Equal(3, secondModelToolResults.Count);
        Assert.All(secondModelToolResults, item => Assert.Equal("blocked", item.Result.GetProperty("status").GetString()));
        var failureCodes = secondModelToolResults
            .Select(item => item.Result.GetProperty("failure").GetProperty("code").GetString())
            .OrderBy(static code => code, StringComparer.Ordinal)
            .ToArray();
        Assert.Contains("subagent.fanout_exceeded", failureCodes);
        Assert.Contains("subagent.fanout_item_count_exceeded", failureCodes);
        var fanInSummary = secondModelToolResults[0].Result
            .GetProperty("output")
            .GetProperty("subAgentFanInSummary");
        Assert.Equal("Blocked", fanInSummary.GetProperty("status").GetString());
        Assert.Equal(3, fanInSummary.GetProperty("results").Items.Count);
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldReturnTimeoutToolResultsWhenFanoutItemTimeoutCancelsChildren()
    {
        var modelCalls = 0;
        var childTimeoutCancellationObserved = false;
        IReadOnlyList<ToolResultProviderInputItem> secondModelToolResults = [];
        var runtime = RecordingExecutionRuntime.CreateAsync(async (plan, cancellationToken) =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCalls++;
                if (modelCalls == 2)
                {
                    var modelStep = Assert.IsType<ModelInvocationStep>(Assert.Single(plan.Steps));
                    secondModelToolResults = modelStep.InputEnvelope.Inputs.OfType<ToolResultProviderInputItem>().ToArray();
                    return CreateStageRuntimeResult(plan, CreateModelFinalOutput());
                }

                return CreateStageRuntimeResult(plan, CreateSpawnToolRequestOutput("call-spawn-001", "call-spawn-002"));
            }

            if (stageId == "stage.tool-exec" && plan.Steps[0] is ModuleCapabilityStep)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    childTimeoutCancellationObserved = cancellationToken.IsCancellationRequested;
                    throw;
                }
            }

            return CreateStageRuntimeResult(plan, CreateSignalsOutput($"{stageId}.succeeded"));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(
            new StableKernelCore(),
            runtime.Instance,
            subAgentSpawnQuota: new SubAgentSpawnQuota(maxSpawnDepth: 1, maxFanoutPerAgent: 4, maxTreeNodes: 8, maxConcurrentAgents: 2));

        var result = await loop.RunReactiveAsync(
            CreateIntent(new KernelBudget(tokenBudget: 4000, timeBudgetMs: 20, retryBudget: 1, toolCallBudget: 2)),
            CreateOptions(),
            CancellationToken.None);

        Assert.Equal(KernelRuntimeExecutionDisposition.RuntimeCompleted, result.Disposition);
        Assert.True(childTimeoutCancellationObserved);
        Assert.Equal(2, secondModelToolResults.Count);
        Assert.All(secondModelToolResults, item => Assert.Equal("timeout", item.Result.GetProperty("status").GetString()));
        Assert.All(secondModelToolResults, item => Assert.Equal("subagent.item_timeout", item.Result.GetProperty("failure").GetProperty("code").GetString()));
        var fanInSummary = secondModelToolResults[0].Result
            .GetProperty("output")
            .GetProperty("subAgentFanInSummary");
        Assert.Equal("CompletedWithFailures", fanInSummary.GetProperty("status").GetString());
        Assert.Contains("failed=2", fanInSummary.GetProperty("summaryText").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunReactiveAsync_ShouldPropagateParentCancellationToActiveFanoutChildren()
    {
        var modelCalls = 0;
        var startedChildren = 0;
        var childCancellationObserved = false;
        var allChildrenStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = RecordingExecutionRuntime.CreateAsync(async (plan, cancellationToken) =>
        {
            var stageId = plan.Steps[0].SourceStageId.Value;
            if (stageId == "stage.model-reason")
            {
                modelCalls++;
                return modelCalls == 1
                    ? CreateStageRuntimeResult(plan, CreateSpawnToolRequestOutput("call-spawn-001", "call-spawn-002"))
                    : CreateStageRuntimeResult(plan, CreateModelFinalOutput());
            }

            if (stageId == "stage.tool-exec" && plan.Steps[0] is ModuleCapabilityStep)
            {
                if (Interlocked.Increment(ref startedChildren) == 2)
                {
                    allChildrenStarted.TrySetResult();
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    childCancellationObserved = cancellationToken.IsCancellationRequested;
                    throw;
                }
            }

            return CreateStageRuntimeResult(plan, CreateSignalsOutput($"{stageId}.succeeded"));
        });
        var loop = new AdaptiveRuntimeExecutionLoop(
            new StableKernelCore(),
            runtime.Instance,
            subAgentSpawnQuota: new SubAgentSpawnQuota(maxSpawnDepth: 1, maxFanoutPerAgent: 4, maxTreeNodes: 8, maxConcurrentAgents: 2));
        using var cancellation = new CancellationTokenSource();

        var runTask = loop.RunReactiveAsync(CreateIntent(), CreateOptions(), cancellation.Token);
        await allChildrenStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        Assert.True(childCancellationObserved);
    }

    private static TurnIntent CreateIntent(KernelBudget? budget = null)
        => new(
            new CoreIntentId("intent-subagent-materialization"),
            new KernelSubjectRef(
                new SessionId("session-subagent"),
                new ThreadId("thread-parent"),
                new WorkflowId("workflow-subagent"),
                new TurnId("turn-parent")),
            new GovernanceEnvelope(
                "governance-subagent-materialization",
                allowedToolIds:
                [
                    "update_context_policy",
                    "request_capability_call",
                    "spawn_agent",
                    "read_file",
                ],
                allowedModuleIds:
                [
                    "kernel.default",
                    "provider.default",
                    "module.sub_agent",
                ],
                maxSideEffectLevel: SideEffectLevel.HostMutation,
                requiresHumanGate: false),
            "turn://subagent-materialization",
            budget ?? new KernelBudget(tokenBudget: 4000, timeBudgetMs: 30000, retryBudget: 1, toolCallBudget: 2),
            new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["message"] = StructuredValue.FromString("spawn a child"),
            }));

    private static KernelRuntimeExecutionOptions CreateOptions()
        => new(
            new KernelRunOptions(new KernelRunId("run-subagent-materialization"), enableAdaptive: false, requireHumanGate: false),
            new ExecutionRuntimeContext(
                new ExecutionId("execution-subagent-materialization"),
                new KernelRunId("run-subagent-materialization"),
                CreateIntent().Governance,
                "D:/Work/TianShu"),
            ExecuteRuntimePlan: true);

    private static StructuredValue CreateSpawnToolRequestOutput()
        => CreateSpawnToolRequestOutput("call-spawn-001");

    private static StructuredValue CreateSpawnToolRequestOutput(params string[] callIds)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["signals"] = new[] { "tool_requests_available" },
            ["toolRequests"] = callIds.Select(static callId => new Dictionary<string, object?>
            {
                ["callId"] = callId,
                ["toolId"] = "spawn_agent",
                ["operation"] = "spawn",
                ["input"] = new Dictionary<string, object?>
                {
                    ["taskBrief"] = $"child task {callId}",
                    ["evidenceRefs"] = new[] { "evidence://one" },
                },
            }).ToArray(),
        });

    private static StructuredValue CreateSpawnToolResultOutput(string status)
        => CreateSpawnToolResultOutput("call-spawn-001", status);

    private static StructuredValue CreateSpawnToolResultOutput(
        string callId,
        string status,
        string? resultText = "child result",
        IReadOnlyDictionary<string, object?>? claims = null,
        string? failureCode = null,
        string? failureMessage = null)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["signals"] = new[] { "tool.results.materialized" },
            ["toolResults"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["callId"] = callId,
                    ["toolId"] = "spawn_agent",
                    ["status"] = status,
                    ["output"] = new Dictionary<string, object?>
                    {
                        ["resultText"] = resultText,
                        ["childRunId"] = $"run-child-{callId}",
                        ["childThreadId"] = $"thread-child-{callId}",
                        ["replaySummaryRef"] = $"replay://child/{callId}",
                        ["diagnosticsRefs"] = new[] { $"diagnostics://child/{callId}" },
                        ["artifactRefs"] = new[] { $"artifact://child/{callId}/report" },
                        ["claims"] = claims,
                    },
                    ["failure"] = failureCode is null
                        ? null
                        : new Dictionary<string, object?>
                        {
                            ["code"] = failureCode,
                            ["message"] = failureMessage,
                        },
                },
            },
        });

    private static StructuredValue CreateModelFinalOutput()
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["signals"] = new[] { "model_final_response" },
            ["assistantText"] = "parent final",
        });

    private static StructuredValue CreateSignalsOutput(string signal)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["signals"] = new[] { signal },
        });

    private static ExecutionRunResult CreateStageRuntimeResult(ExecutionPlan plan, StructuredValue output)
        => new(
            plan.PlanId,
            new ExecutionId("execution-subagent-materialization"),
            RuntimeStepResultStatus.Succeeded,
            plan.Steps.Select(step => new RuntimeStepResult(step.StepId, step.StepKind, RuntimeStepResultStatus.Succeeded, output)).ToArray(),
            diagnosticsRef: $"diagnostics://test/{plan.PlanId}",
            traceRef: $"trace://test/{plan.PlanId}");

    private static void UpdateMax(ref int target, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (value <= current || Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    private class RecordingExecutionRuntime : DispatchProxy
    {
        private Func<ExecutionPlan, CancellationToken, Task<ExecutionRunResult>>? resultFactory;

        public IExecutionRuntime Instance { get; private set; } = null!;

        public static RecordingExecutionRuntime Create(Func<ExecutionPlan, ExecutionRunResult> resultFactory)
            => CreateAsync((plan, _) => Task.FromResult(resultFactory(plan)));

        public static RecordingExecutionRuntime CreateAsync(Func<ExecutionPlan, Task<ExecutionRunResult>> resultFactory)
            => CreateAsync((plan, _) => resultFactory(plan));

        public static RecordingExecutionRuntime CreateAsync(Func<ExecutionPlan, CancellationToken, Task<ExecutionRunResult>> resultFactory)
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
                return resultFactory!((ExecutionPlan)args![0]!, (CancellationToken)args[2]!);
            }

            if (targetMethod?.Name == nameof(IAsyncDisposable.DisposeAsync))
            {
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException($"Unexpected runtime member call: {targetMethod?.Name}");
        }
    }
}
