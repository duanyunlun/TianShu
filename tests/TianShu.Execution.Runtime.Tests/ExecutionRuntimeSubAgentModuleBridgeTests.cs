using TianShu.Configuration;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;
using TianShu.Provider.Abstractions;
using TianShu.RuntimeComposition;

namespace TianShu.Execution.Runtime.Tests;

public sealed class ExecutionRuntimeSubAgentModuleBridgeTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldInvokeSubAgentModuleThroughModuleCapabilityStep()
    {
        var module = new RecordingSubAgentModule(new SubAgentRunResult(
            "call-spawn-001",
            "run-child",
            "thread-child",
            SubAgentRunStatus.Completed,
            resultText: "child answer",
            replaySummaryRef: "trace://child/run"));
        await using var runtime = new TianShuExecutionRuntime(new ExecutionRuntimeStepBindingRegistry(
            subAgentModules: new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
            {
                ["module.sub_agent"] = module,
            }));
        var step = CreateSubAgentStep();
        var plan = CreatePlan(step);

        var result = await runtime.ExecuteAsync(plan, CreateContext(), CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(module.LastRequest);
        Assert.Equal("call-spawn-001", module.LastRequest!.SpawnCallId);
        Assert.Equal("run-child", module.LastChildLineage?.CurrentRunId);
        var stepResult = Assert.Single(result.StepResults);
        Assert.Equal(RuntimeStepResultStatus.Succeeded, stepResult.Status);
        var toolResult = Assert.Single(stepResult.Output!.GetProperty("toolResults").Items);
        Assert.Equal("call-spawn-001", toolResult.GetProperty("callId").GetString());
        Assert.Equal("spawn_agent", toolResult.GetProperty("toolId").GetString());
        Assert.Equal("succeeded", toolResult.GetProperty("status").GetString());
        Assert.Equal("child answer", toolResult.GetProperty("output").GetProperty("resultText").GetString());
    }

    [Fact]
    public async Task KernelRuntimeTurnLoopComposition_ShouldRegisterInjectedSubAgentModule()
    {
        var module = new RecordingSubAgentModule(new SubAgentRunResult(
            "call-spawn-001",
            "run-child",
            "thread-child",
            SubAgentRunStatus.Completed,
            resultText: "child answer"));
        await using var runtime = KernelRuntimeTurnLoopComposition.CreateRuntime(
            new ResolvedTianShuConfig
            {
                Model = "test-model",
                ModelProvider = "openai",
                ProviderBaseUrl = "https://provider.test/v1",
                ProviderEnvKey = "TIANSHU_TEST_SUBAGENT_KEY",
                ProviderWireApi = ProviderWireApi.OpenAiResponses,
                ProtocolAdapter = ProviderWireApi.OpenAiResponses,
            },
            readEnvironmentVariable: static _ => "test-api-key",
            subAgentModules: new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
            {
                ["module.sub_agent"] = module,
            });

        var result = await runtime.ExecuteAsync(CreatePlan(CreateSubAgentStep()), CreateContext(), CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(module.LastRequest);
        var toolResult = Assert.Single(Assert.Single(result.StepResults).Output!.GetProperty("toolResults").Items);
        Assert.Equal("succeeded", toolResult.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnToolResultWhenChildRunIsBlocked()
    {
        var module = new RecordingSubAgentModule(new SubAgentRunResult(
            "call-spawn-001",
            "run-child",
            "thread-child",
            SubAgentRunStatus.Blocked,
            failure: new SubAgentFailure("subagent.spawn_depth_exceeded", "depth exceeded")));
        await using var runtime = new TianShuExecutionRuntime(new ExecutionRuntimeStepBindingRegistry(
            subAgentModules: new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
            {
                ["module.sub_agent"] = module,
            }));

        var result = await runtime.ExecuteAsync(CreatePlan(CreateSubAgentStep()), CreateContext(), CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        var toolResult = Assert.Single(Assert.Single(result.StepResults).Output!.GetProperty("toolResults").Items);
        Assert.Equal("blocked", toolResult.GetProperty("status").GetString());
        Assert.Equal("subagent.spawn_depth_exceeded", toolResult.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnToolResultWhenChildRunFailedWithoutFailingParentStep()
    {
        var module = new RecordingSubAgentModule(new SubAgentRunResult(
            "call-spawn-001",
            "run-child",
            "thread-child",
            SubAgentRunStatus.Failed,
            failure: new SubAgentFailure("subagent.child_failed", "child run failed")));
        await using var runtime = new TianShuExecutionRuntime(new ExecutionRuntimeStepBindingRegistry(
            subAgentModules: new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
            {
                ["module.sub_agent"] = module,
            }));

        var result = await runtime.ExecuteAsync(CreatePlan(CreateSubAgentStep()), CreateContext(), CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        var stepResult = Assert.Single(result.StepResults);
        Assert.Equal(RuntimeStepResultStatus.Succeeded, stepResult.Status);
        var toolResult = Assert.Single(stepResult.Output!.GetProperty("toolResults").Items);
        Assert.Equal("failed", toolResult.GetProperty("status").GetString());
        Assert.Equal("subagent.child_failed", toolResult.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectSubAgentModuleWhenGovernanceDoesNotAllowModule()
    {
        var module = new RecordingSubAgentModule(new SubAgentRunResult(
            "call-spawn-001",
            "run-child",
            "thread-child",
            SubAgentRunStatus.Completed));
        await using var runtime = new TianShuExecutionRuntime(new ExecutionRuntimeStepBindingRegistry(
            subAgentModules: new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
            {
                ["module.sub_agent"] = module,
            }));

        var result = await runtime.ExecuteAsync(
            CreatePlan(CreateSubAgentStep()),
            CreateContext(allowedModuleIds: ["module.other"]),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("runtime_step_module_not_allowed", Assert.Single(result.StepResults).Failure?.Code);
        Assert.Null(module.LastRequest);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectSubAgentRequestBudgetExceedingParentStepBudget()
    {
        var module = new RecordingSubAgentModule(new SubAgentRunResult(
            "call-spawn-001",
            "run-child",
            "thread-child",
            SubAgentRunStatus.Completed));
        await using var runtime = new TianShuExecutionRuntime(new ExecutionRuntimeStepBindingRegistry(
            subAgentModules: new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
            {
                ["module.sub_agent"] = module,
            }));
        var step = CreateSubAgentStep(inputEnvelope: CreateSpawnRequestInput(requestedBudget: Budget(tokenBudget: 2000)));

        var result = await runtime.ExecuteAsync(CreatePlan(step), CreateContext(), CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("subagent_requested_budget_exceeds_step_budget", Assert.Single(result.StepResults).Failure?.Code);
        Assert.Null(module.LastRequest);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPassParentGovernanceTraceAndBudgetMetadataToSubAgentModule()
    {
        var module = new RecordingSubAgentModule(new SubAgentRunResult(
            "call-spawn-001",
            "run-child",
            "thread-child",
            SubAgentRunStatus.Completed));
        await using var runtime = new TianShuExecutionRuntime(new ExecutionRuntimeStepBindingRegistry(
            subAgentModules: new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
            {
                ["module.sub_agent"] = module,
            }));
        var step = CreateSubAgentStep(tracePolicy: new TracePolicy(requiredEventKinds: ["subagent.spawn"]));

        var result = await runtime.ExecuteAsync(
            CreatePlan(step),
            CreateContext(
                policyIds: ["policy-parent"],
                requiresHumanGate: true,
                approvalIds: [new ApprovalId("approval-parent")]),
            CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Succeeded, result.Status);
        Assert.NotNull(module.LastContext);
        Assert.True(module.LastContext!.Governance.RequiresHumanGate);
        Assert.Equal("D:/Work/TianShu", module.LastContext.WorkingDirectory);
        var metadata = module.LastContext.Metadata;
        Assert.Equal("governance-parent", metadata.Entries["subAgent.parentGovernance"].GetProperty("envelopeId").GetString());
        Assert.Equal("policy-parent", Assert.Single(metadata.Entries["subAgent.parentGovernance"].GetProperty("policyIds").Items).GetString());
        Assert.True(metadata.Entries["subAgent.parentTracePolicy"].GetProperty("enabled").GetBoolean());
        Assert.Equal("subagent.spawn", Assert.Single(metadata.Entries["subAgent.parentTracePolicy"].GetProperty("requiredEventKinds").Items).GetString());
        Assert.Equal(1000, metadata.Entries["subAgent.parentRuntimeStepBudget"].GetProperty("tokenBudget").GetInt32());
        Assert.False(metadata.Entries["subAgent.parentPermission"].GetProperty("requiresHumanGate").GetBoolean());
        Assert.Equal("HostMutation", metadata.Entries["subAgent.parentSideEffect"].GetProperty("level").GetString());
        Assert.Equal("D:/Work/TianShu", metadata.Entries["subAgent.parentWorkingDirectory"].GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectSubAgentStepWithoutChildLineageMetadata()
    {
        var module = new RecordingSubAgentModule(new SubAgentRunResult(
            "call-spawn-001",
            "run-child",
            "thread-child",
            SubAgentRunStatus.Completed));
        await using var runtime = new TianShuExecutionRuntime(new ExecutionRuntimeStepBindingRegistry(
            subAgentModules: new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
            {
                ["module.sub_agent"] = module,
            }));
        var step = CreateSubAgentStep(metadata: MetadataBag.Empty);

        var result = await runtime.ExecuteAsync(CreatePlan(step), CreateContext(), CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("subagent_child_lineage_missing", Assert.Single(result.StepResults).Failure?.Code);
        Assert.Null(module.LastRequest);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailClosedWhenSubAgentModuleIsNotBound()
    {
        await using var runtime = new TianShuExecutionRuntime(ExecutionRuntimeStepBindingRegistry.Empty);

        var result = await runtime.ExecuteAsync(CreatePlan(CreateSubAgentStep()), CreateContext(), CancellationToken.None);

        Assert.Equal(RuntimeStepResultStatus.Blocked, result.Status);
        Assert.Equal("subagent_module_not_bound", Assert.Single(result.StepResults).Failure?.Code);
    }

    private static ExecutionPlan CreatePlan(ModuleCapabilityStep step)
        => new(
            "plan-subagent",
            new StageGraphId("graph.turn.default"),
            new CoreIntentId("intent-parent"),
            [step],
            new ExecutionPlanPolicy(stopOnFailure: true),
            new TracePolicy());

    private static ModuleCapabilityStep CreateSubAgentStep(
        MetadataBag? metadata = null,
        StructuredValue? inputEnvelope = null,
        KernelBudget? budget = null,
        TracePolicy? tracePolicy = null)
        => new(
            "step-subagent-spawn",
            new CoreIntentId("intent-parent"),
            new StageGraphId("graph.turn.default"),
            new StageId("stage.tool-exec"),
            new KernelOperationId("operation-tool-exec"),
            "module.sub_agent",
            "sub_agent.spawn",
            inputEnvelope ?? CreateSpawnRequestInput(),
            new PermissionEnvelope(["module.sub_agent"], requiresHumanGate: false, reason: "test"),
            new SideEffectProfile(SideEffectLevel.HostMutation, ["subagent"], reversible: false, requiresAudit: true),
            budget ?? new KernelBudget(tokenBudget: 1000, timeBudgetMs: 30000, costBudget: 0, retryBudget: 1, toolCallBudget: 2),
            new ContractRef("sub_agent.spawn.output", "v1"),
            tracePolicy ?? new TracePolicy(),
            metadata ?? CreateSpawnMetadata());

    private static ExecutionRuntimeContext CreateContext(
        IReadOnlyList<string>? allowedModuleIds = null,
        IReadOnlyList<string>? policyIds = null,
        bool requiresHumanGate = false,
        IReadOnlyList<ApprovalId>? approvalIds = null)
        => new(
            new ExecutionId("execution-subagent"),
            new KernelRunId("run-parent"),
            new GovernanceEnvelope(
                "governance-parent",
                policyIds: policyIds,
                allowedToolIds: ["spawn_agent", "read_file"],
                allowedModuleIds: allowedModuleIds ?? ["module.sub_agent"],
                maxSideEffectLevel: SideEffectLevel.HostMutation,
                requiresHumanGate: requiresHumanGate,
                approvalIds: approvalIds),
            workingDirectory: "D:/Work/TianShu");

    private static StructuredValue CreateSpawnRequestInput(IReadOnlyDictionary<string, object?>? requestedBudget = null)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["spawnCallId"] = "call-spawn-001",
            ["parentLineage"] = Lineage("run-root", "run-parent", null, 0, 0),
            ["parentSubject"] = new Dictionary<string, object?>
            {
                ["sessionId"] = "session-parent",
                ["threadId"] = "thread-parent",
                ["workflowId"] = "workflow-parent",
                ["turnId"] = "turn-parent",
            },
            ["taskBrief"] = "answer as a child agent",
            ["evidenceRefs"] = new[] { "evidence://parent/one" },
            ["requestedGovernance"] = Governance("governance-child"),
            ["requestedBudget"] = requestedBudget ?? Budget(),
            ["requiresHumanGate"] = false,
            ["metadata"] = new Dictionary<string, object?>
            {
                ["source"] = "test",
            },
        });

    private static MetadataBag CreateSpawnMetadata()
        => new(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["subAgent.childLineage"] = StructuredValue.FromPlainObject(Lineage("run-root", "run-child", "run-parent", 1, 0)),
            ["subAgent.quota"] = StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["maxSpawnDepth"] = 1,
                ["maxFanoutPerAgent"] = 8,
                ["maxTreeNodes"] = 32,
                ["maxConcurrentAgents"] = 0,
            }),
        });

    private static Dictionary<string, object?> Lineage(string rootRunId, string currentRunId, string? parentRunId, int depth, int siblingIndex)
        => new(StringComparer.Ordinal)
        {
            ["rootRunId"] = rootRunId,
            ["currentRunId"] = currentRunId,
            ["parentRunId"] = parentRunId,
            ["depth"] = depth,
            ["siblingIndex"] = siblingIndex,
            ["ledgerRef"] = "ledger-001",
        };

    private static Dictionary<string, object?> Governance(string envelopeId)
        => new(StringComparer.Ordinal)
        {
            ["envelopeId"] = envelopeId,
            ["policyIds"] = Array.Empty<string>(),
            ["allowedToolIds"] = new[] { "read_file" },
            ["allowedModuleIds"] = new[] { "module.sub_agent" },
            ["maxSideEffectLevel"] = SideEffectLevel.HostMutation.ToString(),
            ["requiresHumanGate"] = false,
            ["approvalIds"] = Array.Empty<string>(),
            ["auditRecordIds"] = Array.Empty<string>(),
        };

    private static Dictionary<string, object?> Budget(
        int tokenBudget = 1000,
        long timeBudgetMs = 30000,
        decimal costBudget = 0,
        int retryBudget = 1,
        int toolCallBudget = 2)
        => new(StringComparer.Ordinal)
        {
            ["tokenBudget"] = tokenBudget,
            ["timeBudgetMs"] = timeBudgetMs,
            ["costBudget"] = costBudget,
            ["retryBudget"] = retryBudget,
            ["toolCallBudget"] = toolCallBudget,
        };

    private sealed class RecordingSubAgentModule : ISubAgentModule
    {
        private readonly SubAgentRunResult result;

        public RecordingSubAgentModule(SubAgentRunResult result)
        {
            this.result = result;
        }

        public ModuleDescriptor Descriptor { get; } = BuiltInModuleDescriptors.SubAgent();

        public SubAgentSpawnRequest? LastRequest { get; private set; }

        public SubAgentLineage? LastChildLineage { get; private set; }

        public SubAgentSpawnQuota? LastQuota { get; private set; }

        public SubAgentModuleInvocationContext? LastContext { get; private set; }

        public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new ModuleSmokeCheckResult(Descriptor.ModuleId, true, ModuleHealthStatus.Healthy));

        public ValueTask<SubAgentRunResult> SpawnAsync(
            SubAgentSpawnRequest request,
            SubAgentLineage childLineage,
            SubAgentSpawnQuota quota,
            SubAgentModuleInvocationContext context,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastChildLineage = childLineage;
            LastQuota = quota;
            LastContext = context;
            return ValueTask.FromResult(result);
        }
    }
}
