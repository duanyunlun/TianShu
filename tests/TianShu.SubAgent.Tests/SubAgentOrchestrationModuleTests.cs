using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;
using TianShu.RuntimeComposition;
using TianShu.SubAgent;

namespace TianShu.SubAgent.Tests;

public sealed class SubAgentOrchestrationModuleTests
{
    [Fact]
    public async Task SpawnAsync_ShouldDeriveNarrowedChildTurnAndInvokeReactiveLoop()
    {
        var loop = new RecordingKernelRuntimeExecutionLoop();
        var module = new SubAgentOrchestrationModule(loop);
        var request = CreateRequest();
        var childLineage = request.ParentLineage.Descend("run-child", siblingIndex: 0);
        using var cancellation = new CancellationTokenSource();

        var result = await module.SpawnAsync(
            request,
            childLineage,
            new SubAgentSpawnQuota(1, 8, 32, 0),
            CreateInvocationContext(),
            cancellation.Token);

        Assert.Equal(SubAgentRunStatus.Completed, result.Status);
        Assert.Equal("child answer", result.ResultText);
        Assert.Equal("trace://child", result.ReplaySummaryRef);
        Assert.Equal("artifact://child/report", Assert.Single(result.ArtifactRefs));
        Assert.NotNull(loop.LastIntent);
        var childIntent = Assert.IsType<TurnIntent>(loop.LastIntent);
        Assert.Equal("session-parent", childIntent.Subject.SessionId.Value);
        Assert.Equal("thread-run-child", childIntent.Subject.ThreadId.Value);
        Assert.Equal("workflow-parent", childIntent.Subject.WorkflowId?.Value);
        Assert.Equal(request.RequestedBudget, childIntent.Budget);
        Assert.True(childIntent.Metadata.TryGetValue("message", out var childMessage));
        Assert.Equal("answer as a child", childMessage.GetString());
        Assert.DoesNotContain("spawn_agent", childIntent.Governance.AllowedToolIds);
        Assert.Contains("read_file", childIntent.Governance.AllowedToolIds);
        Assert.DoesNotContain("write", childIntent.Governance.AllowedToolIds);
        Assert.Equal(["policy-parent"], childIntent.Governance.PolicyIds);
        Assert.Contains(childIntent.Governance.ApprovalIds, approval => approval.Value == "approval-parent");
        Assert.NotNull(loop.LastOptions);
        Assert.Equal("run-child", loop.LastOptions!.RuntimeContext.KernelRunId.Value);
        Assert.Equal(childIntent.Governance, loop.LastOptions.RuntimeContext.Governance);
        Assert.Equal(request.RequestedBudget, loop.LastOptions.KernelOptions.BudgetOverride);
        Assert.Equal(cancellation.Token, loop.LastCancellationToken);
    }

    [Fact]
    public async Task SpawnAsync_ShouldReturnBlockedWhenChildLineageDoesNotMatchParent()
    {
        var loop = new RecordingKernelRuntimeExecutionLoop();
        var module = new SubAgentOrchestrationModule(loop);
        var request = CreateRequest();
        var invalidLineage = new SubAgentLineage("run-root", "run-child", "run-other", depth: 1, siblingIndex: 0, "ledger-001");

        var result = await module.SpawnAsync(
            request,
            invalidLineage,
            new SubAgentSpawnQuota(1, 8, 32, 0),
            CreateInvocationContext(),
            CancellationToken.None);

        Assert.Equal(SubAgentRunStatus.Blocked, result.Status);
        Assert.Equal("subagent_child_lineage_invalid", result.Failure?.Code);
        Assert.Null(loop.LastIntent);
    }

    [Fact]
    public async Task SpawnAsync_ShouldProjectChildRuntimeFailureAsSubAgentFailure()
    {
        var loop = new RecordingKernelRuntimeExecutionLoop
        {
            ResultFactory = CreateFailedResult,
        };
        var module = new SubAgentOrchestrationModule(loop);
        var request = CreateRequest();
        var childLineage = request.ParentLineage.Descend("run-child", siblingIndex: 0);

        var result = await module.SpawnAsync(
            request,
            childLineage,
            new SubAgentSpawnQuota(1, 8, 32, 0),
            CreateInvocationContext(),
            CancellationToken.None);

        Assert.Equal(SubAgentRunStatus.Failed, result.Status);
        Assert.Equal("provider.failed", result.Failure?.Code);
        Assert.Equal("diagnostics://child-failed", Assert.Single(result.DiagnosticsRefs));
    }

    [Fact]
    public async Task SpawnAsync_ShouldPropagateParentGovernanceHumanGateWorkspaceAndTraceMetadata()
    {
        var loop = new RecordingKernelRuntimeExecutionLoop();
        var module = new SubAgentOrchestrationModule(loop);
        var parentGovernance = CreateParentGovernance(requiresHumanGate: true);
        var request = CreateRequest();
        var childLineage = request.ParentLineage.Descend("run-child", siblingIndex: 0);
        var parentMetadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["subAgent.parentTracePolicy"] = StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["requireDiagnosticsRef"] = true,
                ["requireRuntimeTraceRef"] = true,
                ["requiredEventKinds"] = new[] { "subagent.spawn" },
            }),
            ["subAgent.parentRuntimeStepBudget"] = StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["tokenBudget"] = 1000,
                ["timeBudgetMs"] = 30000,
                ["costBudget"] = 0,
                ["retryBudget"] = 1,
                ["toolCallBudget"] = 2,
            }),
        });

        var result = await module.SpawnAsync(
            request,
            childLineage,
            new SubAgentSpawnQuota(1, 8, 32, 0),
            CreateInvocationContext(
                parentGovernance,
                new PermissionEnvelope(["module.sub_agent"], requiresHumanGate: true, reason: "parent approved subtree"),
                parentMetadata),
            CancellationToken.None);

        Assert.Equal(SubAgentRunStatus.Completed, result.Status);
        var childIntent = Assert.IsType<TurnIntent>(loop.LastIntent);
        Assert.True(childIntent.Governance.RequiresHumanGate);
        Assert.Contains(childIntent.Governance.ApprovalIds, approval => approval.Value == "approval-parent");
        Assert.NotNull(loop.LastOptions);
        Assert.True(loop.LastOptions!.KernelOptions.RequireHumanGate);
        Assert.Equal("D:/Work/TianShu", loop.LastOptions.RuntimeContext.WorkingDirectory);
        var runtimeMetadata = loop.LastOptions.RuntimeContext.Metadata;
        Assert.Equal("governance-parent", runtimeMetadata.Entries["subAgent.parentGovernance"].GetProperty("envelopeId").GetString());
        Assert.True(runtimeMetadata.Entries["subAgent.childGovernance"].GetProperty("requiresHumanGate").GetBoolean());
        Assert.Equal("policy-parent", Assert.Single(runtimeMetadata.Entries["subAgent.childGovernance"].GetProperty("policyIds").Items).GetString());
        Assert.Equal("HostMutation", runtimeMetadata.Entries["subAgent.parentSideEffectLevel"].GetString());
        Assert.True(runtimeMetadata.TryGetValue("subAgent.parentTracePolicy", out _));
        Assert.True(runtimeMetadata.TryGetValue("subAgent.parentRuntimeStepBudget", out _));
        Assert.Equal("D:/Work/TianShu", runtimeMetadata.Entries["subAgent.parentWorkingDirectory"].GetString());
    }

    [Fact]
    public void SubAgentGovernanceNarrowing_ShouldNeverGrantToolsModulesOrApprovalsOutsideParent()
    {
        var parent = CreateParentGovernance();
        var requested = new GovernanceEnvelope(
            "governance-requested",
            policyIds: ["policy-parent", "policy-new"],
            allowedToolIds: ["read_file", "write", "spawn_agent", "unknown_tool"],
            allowedModuleIds: ["provider.default", "module.sub_agent", "unknown.module"],
            maxSideEffectLevel: SideEffectLevel.Privileged,
            requiresHumanGate: false,
            approvalIds: [new ApprovalId("approval-parent"), new ApprovalId("approval-new")]);

        var narrowed = SubAgentGovernanceNarrowing.Narrow(parent, requested, "run-child", requiresHumanGate: false);

        Assert.Equal(["policy-parent"], narrowed.PolicyIds);
        Assert.Equal(["read_file"], narrowed.AllowedToolIds);
        Assert.Equal(["provider.default", "module.sub_agent"], narrowed.AllowedModuleIds);
        Assert.Equal(SideEffectLevel.HostMutation, narrowed.MaxSideEffectLevel);
        Assert.Contains(narrowed.ApprovalIds, approval => approval.Value == "approval-parent");
        Assert.DoesNotContain(narrowed.ApprovalIds, approval => approval.Value == "approval-new");
    }

    [Fact]
    public void SubAgentGovernanceNarrowing_ShouldAllowRequestedApprovalSubsetToNarrowInheritedApprovals()
    {
        var parent = new GovernanceEnvelope(
            "governance-parent",
            allowedToolIds: ["read_file", "spawn_agent"],
            allowedModuleIds: ["provider.default", "module.sub_agent"],
            maxSideEffectLevel: SideEffectLevel.HostMutation,
            requiresHumanGate: false,
            approvalIds: [new ApprovalId("approval-a"), new ApprovalId("approval-b")]);
        var requested = new GovernanceEnvelope(
            "governance-requested",
            allowedToolIds: ["read_file"],
            allowedModuleIds: ["module.sub_agent"],
            maxSideEffectLevel: SideEffectLevel.HostMutation,
            requiresHumanGate: false,
            approvalIds: [new ApprovalId("approval-b")]);

        var narrowed = SubAgentGovernanceNarrowing.Narrow(parent, requested, "run-child", requiresHumanGate: false);

        var approval = Assert.Single(narrowed.ApprovalIds);
        Assert.Equal("approval-b", approval.Value);
    }

    [Fact]
    public void SubAgentGovernanceNarrowing_ShouldInheritParentPoliciesWhenRequestOmitsPolicies()
    {
        var parent = CreateParentGovernance();
        var requested = new GovernanceEnvelope(
            "governance-requested",
            allowedToolIds: ["read_file"],
            allowedModuleIds: ["provider.default"],
            maxSideEffectLevel: SideEffectLevel.ReadOnly,
            requiresHumanGate: false);

        var narrowed = SubAgentGovernanceNarrowing.Narrow(parent, requested, "run-child", requiresHumanGate: false);

        Assert.Equal(["policy-parent"], narrowed.PolicyIds);
    }

    [Fact]
    public void SubAgentOrchestrationModule_SourceShouldOnlyConsumeRuntimeLoop()
    {
        var source = File.ReadAllText(FindRepoFile("src/Core/TianShu.SubAgent/SubAgentOrchestrationModule.cs"));

        Assert.DoesNotContain("new StableKernelCore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageGraph(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new RuntimeStep", source, StringComparison.Ordinal);
    }

    private static SubAgentSpawnRequest CreateRequest()
        => new(
            "call-spawn-001",
            new SubAgentLineage("run-root", "run-parent", null, depth: 0, siblingIndex: 0, "ledger-001"),
            new KernelSubjectRef(
                new SessionId("session-parent"),
                new ThreadId("thread-parent"),
                new WorkflowId("workflow-parent"),
                new TurnId("turn-parent")),
            "answer as a child",
            ["evidence://parent/one"],
            new GovernanceEnvelope(
                "governance-requested",
                allowedToolIds: ["read_file", "write", "spawn_agent"],
                allowedModuleIds: ["provider.default", "module.sub_agent"],
                maxSideEffectLevel: SideEffectLevel.HostMutation,
                requiresHumanGate: false),
            new KernelBudget(tokenBudget: 1000, timeBudgetMs: 30000, costBudget: 0, retryBudget: 1, toolCallBudget: 2),
            requiresHumanGate: false);

    private static SubAgentModuleInvocationContext CreateInvocationContext(
        GovernanceEnvelope? governance = null,
        PermissionEnvelope? permission = null,
        MetadataBag? metadata = null)
        => new(
            "step-subagent",
            "intent-parent",
            "graph.turn.default",
            "stage.tool-exec",
            "operation-tool-exec",
            permission ?? new PermissionEnvelope(["module.sub_agent"], requiresHumanGate: false, reason: "test"),
            new SideEffectProfile(SideEffectLevel.HostMutation, ["subagent"], reversible: false, requiresAudit: true),
            "execution-parent",
            "run-parent",
            governance ?? CreateParentGovernance(),
            "D:/Work/TianShu",
            metadata);

    private static GovernanceEnvelope CreateParentGovernance(bool requiresHumanGate = false)
        => new(
            "governance-parent",
            policyIds: ["policy-parent"],
            allowedToolIds: ["read_file", "spawn_agent"],
            allowedModuleIds: ["provider.default", "module.sub_agent"],
            maxSideEffectLevel: SideEffectLevel.HostMutation,
            requiresHumanGate: requiresHumanGate,
            approvalIds: [new ApprovalId("approval-parent")]);

    private static KernelRuntimeExecutionResult CreateCompletedResult(CoreIntent intent, KernelRuntimeExecutionOptions options)
    {
        var runId = options.KernelOptions.RunId ?? options.RuntimeContext.KernelRunId;
        var runtimeResult = new ExecutionRunResult(
            "plan-child",
            options.RuntimeContext.ExecutionId,
            RuntimeStepResultStatus.Succeeded,
            [
                new RuntimeStepResult(
                    "step-model",
                    RuntimeStepKind.ModelInvocation,
                    RuntimeStepResultStatus.Succeeded,
                    StructuredValue.FromPlainObject(new Dictionary<string, object?>
                    {
                        ["assistantText"] = "child answer",
                        ["artifactRefs"] = new[] { "artifact://child/report" },
                    })),
            ],
            diagnosticsRef: "diagnostics://child",
            traceRef: "trace://child");
        var kernelResult = new KernelRunResult(
            runId,
            intent.IntentId,
            KernelRunLifecycleState.Completed,
            new KernelValidationResult(KernelValidationDecision.Approved),
            traceId: new KernelTraceId("trace-child"));
        return new KernelRuntimeExecutionResult(
            kernelResult,
            runtimeResult,
            KernelRuntimeExecutionDisposition.RuntimeCompleted,
            kernelResult.TraceId,
            runtimeResult.TraceRef,
            runtimeResult.DiagnosticsRef);
    }

    private static KernelRuntimeExecutionResult CreateFailedResult(CoreIntent intent, KernelRuntimeExecutionOptions options)
    {
        var runId = options.KernelOptions.RunId ?? options.RuntimeContext.KernelRunId;
        var runtimeResult = new ExecutionRunResult(
            "plan-child",
            options.RuntimeContext.ExecutionId,
            RuntimeStepResultStatus.Failed,
            [
                new RuntimeStepResult(
                    "step-model",
                    RuntimeStepKind.ModelInvocation,
                    RuntimeStepResultStatus.Failed,
                    failure: new ExecutionFailure("provider.failed", "child provider failed")),
            ],
            diagnosticsRef: "diagnostics://child-failed",
            traceRef: "trace://child-failed");
        var kernelResult = new KernelRunResult(
            runId,
            intent.IntentId,
            KernelRunLifecycleState.Failed,
            new KernelValidationResult(KernelValidationDecision.Approved),
            traceId: new KernelTraceId("trace-child-failed"));
        return new KernelRuntimeExecutionResult(
            kernelResult,
            runtimeResult,
            KernelRuntimeExecutionDisposition.RuntimeFailed,
            kernelResult.TraceId,
            runtimeResult.TraceRef,
            runtimeResult.DiagnosticsRef);
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate repo file: {relativePath}");
    }

    private sealed class RecordingKernelRuntimeExecutionLoop : IKernelRuntimeExecutionLoop
    {
        public CoreIntent? LastIntent { get; private set; }

        public KernelRuntimeExecutionOptions? LastOptions { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Func<CoreIntent, KernelRuntimeExecutionOptions, KernelRuntimeExecutionResult> ResultFactory { get; init; } = CreateCompletedResult;

        public Task<KernelRuntimeExecutionResult> RunAsync(
            CoreIntent intent,
            KernelRuntimeExecutionOptions options,
            CancellationToken cancellationToken)
            => RunReactiveAsync(intent, options, cancellationToken);

        public Task<KernelRuntimeExecutionResult> RunReactiveAsync(
            CoreIntent intent,
            KernelRuntimeExecutionOptions options,
            CancellationToken cancellationToken)
        {
            LastIntent = intent;
            LastOptions = options;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(ResultFactory(intent, options));
        }
    }
}
