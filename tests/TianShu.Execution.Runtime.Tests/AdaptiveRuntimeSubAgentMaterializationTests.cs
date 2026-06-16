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

    private static TurnIntent CreateIntent()
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
            new KernelBudget(tokenBudget: 4000, timeBudgetMs: 30000, retryBudget: 1, toolCallBudget: 2),
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
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["signals"] = new[] { "tool_requests_available" },
            ["toolRequests"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["callId"] = "call-spawn-001",
                    ["toolId"] = "spawn_agent",
                    ["operation"] = "spawn",
                    ["input"] = new Dictionary<string, object?>
                    {
                        ["taskBrief"] = "child task",
                        ["evidenceRefs"] = new[] { "evidence://one" },
                    },
                },
            },
        });

    private static StructuredValue CreateSpawnToolResultOutput(string status)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["signals"] = new[] { "tool.results.materialized" },
            ["toolResults"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["callId"] = "call-spawn-001",
                    ["toolId"] = "spawn_agent",
                    ["status"] = status,
                    ["output"] = new Dictionary<string, object?>
                    {
                        ["resultText"] = "child result",
                        ["childRunId"] = "run-child",
                        ["childThreadId"] = "thread-child",
                    },
                    ["failure"] = null,
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

    private class RecordingExecutionRuntime : DispatchProxy
    {
        private Func<ExecutionPlan, ExecutionRunResult>? resultFactory;

        public IExecutionRuntime Instance { get; private set; } = null!;

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
                return Task.FromResult(resultFactory!((ExecutionPlan)args![0]!));
            }

            if (targetMethod?.Name == nameof(IAsyncDisposable.DisposeAsync))
            {
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException($"Unexpected runtime member call: {targetMethod?.Name}");
        }
    }
}
