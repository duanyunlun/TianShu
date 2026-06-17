using TianShu.Contracts.Agents;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Agents.Tests;

public sealed class AgentContractTests
{
    [Fact]
    public void AgentLineage_RejectsNegativeDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentLineage(null, -1));
    }

    [Fact]
    public void Agent_PreservesParticipantAndDelegationPolicy()
    {
        var participant = new AgentParticipant(
            new ParticipantId("participant-agent"),
            "Worker",
            new AgentId("agent-001"),
            "implementer");
        var policy = new DelegationPolicy(DelegationMode.Automatic, allowParallelDelegation: true, maxChildren: 4);
        var agent = new Agent(
            new AgentId("agent-001"),
            ParticipantRef.From(participant),
            "Worker",
            AgentRole.Implementer,
            delegationPolicy: policy);

        Assert.Equal("participant-agent", agent.AgentParticipant.Id.Value);
        Assert.Equal(4, agent.DelegationPolicy?.MaxChildren);
        Assert.Equal(AgentRole.Implementer, agent.Role);
    }

    [Fact]
    public void ControlPlaneAgentListQuery_PreservesPagingFlags()
    {
        var query = new ControlPlaneAgentListQuery
        {
            Limit = 20,
            Cursor = "agent-cursor-001",
            IncludePrimaryThreads = true,
        };

        Assert.Equal(20, query.Limit);
        Assert.Equal("agent-cursor-001", query.Cursor);
        Assert.True(query.IncludePrimaryThreads);
    }

    [Fact]
    public void ControlPlaneAgentRosterResult_PreservesDescriptorLineage()
    {
        var result = new ControlPlaneAgentRosterResult
        {
            Agents =
            [
                new ControlPlaneAgentDescriptor
                {
                    ThreadId = new ThreadId("thread-agent-001"),
                    Preview = "agent preview",
                    AgentNickname = "Athena",
                    AgentRole = "architect",
                    UpdatedAt = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero),
                    Lineage = new ControlPlaneAgentLineage
                    {
                        ParentThreadId = new ThreadId("thread-root-001"),
                        Depth = 2,
                    },
                },
            ],
            NextCursor = "agent-cursor-002",
        };

        Assert.Equal("agent-cursor-002", result.NextCursor);
        var agent = Assert.Single(result.Agents);
        Assert.Equal("thread-agent-001", agent.ThreadId.Value);
        Assert.Equal("Athena", agent.AgentNickname);
        Assert.Equal("architect", agent.AgentRole);
        Assert.Equal("thread-root-001", agent.Lineage?.ParentThreadId?.Value);
        Assert.Equal(2, agent.Lineage?.Depth);
    }

    [Fact]
    public void SubAgentLineage_DescendShouldPreserveRootAndAdvanceCurrentRun()
    {
        var root = new SubAgentLineage("run-root", "run-parent", null, depth: 0, siblingIndex: 0, "ledger-001");

        var child = root.Descend("run-child", siblingIndex: 3);

        Assert.Equal("run-root", child.RootRunId);
        Assert.Equal("run-parent", child.ParentRunId);
        Assert.Equal("run-child", child.CurrentRunId);
        Assert.Equal(1, child.Depth);
        Assert.Equal(3, child.SiblingIndex);
        Assert.Equal("ledger-001", child.LedgerRef);
    }

    [Fact]
    public void SubAgentSpawnRequest_ShouldNotCarryChildLineage()
    {
        var request = new SubAgentSpawnRequest(
            "call-subagent-001",
            new SubAgentLineage("run-root", "run-parent", null, 0, 0, "ledger-001"),
            new KernelSubjectRef(new SessionId("session-subagent"), new ThreadId("thread-parent"), new WorkflowId("workflow-subagent"), new TurnId("turn-parent")),
            "Summarize the evidence.",
            ["evidence://one"],
            new GovernanceEnvelope("governance-subagent", allowedToolIds: ["read_file"], allowedModuleIds: ["module.sub_agent"], maxSideEffectLevel: SideEffectLevel.HostMutation),
            new KernelBudget(tokenBudget: 1000, timeBudgetMs: 30000, toolCallBudget: 2),
            requiresHumanGate: false);

        Assert.Equal("call-subagent-001", request.SpawnCallId);
        Assert.Equal("run-parent", request.ParentLineage.CurrentRunId);
        Assert.Equal("thread-parent", request.ParentSubject.ThreadId.Value);
        Assert.Single(request.EvidenceRefs);
        Assert.DoesNotContain(
            typeof(SubAgentSpawnRequest).GetProperties().Select(static property => property.Name),
            static name => string.Equals(name, "ChildLineage", StringComparison.Ordinal));
    }

    [Fact]
    public void SubAgentSpawnQuota_ShouldRejectInvalidStructuralBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentSpawnQuota(0, 1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentSpawnQuota(1, -1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentSpawnQuota(1, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentSpawnQuota(1, 1, 1, -1));
    }

    [Fact]
    public void SubAgentFanoutPolicy_ShouldRejectInvalidGateValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentFanoutPolicy(-1, 1, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentFanoutPolicy(0, 0, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentFanoutPolicy(0, 1, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentFanoutPolicy(0, 1, TimeSpan.FromSeconds(1), SubAgentFailureMode.Unspecified));
    }

    [Fact]
    public void SubAgentFanoutRequest_ShouldRejectEmptyExceededOrDuplicateItems()
    {
        var policy = new SubAgentFanoutPolicy(maxConcurrentAgents: 2, maxSubTasks: 2, itemTimeout: TimeSpan.FromSeconds(30));

        Assert.Throws<ArgumentException>(() => new SubAgentFanoutRequest(
            "fanout-001",
            new SubAgentLineage("run-root", "run-parent", null, 0, 0, "ledger-001"),
            new KernelSubjectRef(new SessionId("session-subagent"), new ThreadId("thread-parent")),
            [],
            policy,
            new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare),
            new GovernanceEnvelope("governance-fanout")));

        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentFanoutRequest(
            "fanout-001",
            new SubAgentLineage("run-root", "run-parent", null, 0, 0, "ledger-001"),
            new KernelSubjectRef(new SessionId("session-subagent"), new ThreadId("thread-parent")),
            [
                new SubAgentFanoutItem("item-001", "task one"),
                new SubAgentFanoutItem("item-002", "task two"),
                new SubAgentFanoutItem("item-003", "task three"),
            ],
            policy,
            new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare),
            new GovernanceEnvelope("governance-fanout")));

        Assert.Throws<ArgumentException>(() => new SubAgentFanoutRequest(
            "fanout-001",
            new SubAgentLineage("run-root", "run-parent", null, 0, 0, "ledger-001"),
            new KernelSubjectRef(new SessionId("session-subagent"), new ThreadId("thread-parent")),
            [
                new SubAgentFanoutItem("item-001", "task one"),
                new SubAgentFanoutItem("item-001", "task two"),
            ],
            policy,
            new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare),
            new GovernanceEnvelope("governance-fanout")));
    }

    [Fact]
    public void SubAgentFanInSummary_ShouldPreserveResultsConflictsAndRejectInvalidStatus()
    {
        var runResult = new SubAgentRunResult(
            "call-subagent-001",
            "run-child-001",
            "thread-child-001",
            SubAgentRunStatus.Completed,
            "child result",
            "replay://child-001",
            ["diagnostics://child-001"],
            artifactRefs: ["artifact://child-001/report"]);
        var conflict = new SubAgentConflict(
            "conflict-001",
            ["run-child-001", "run-child-002"],
            "claim_conflict",
            "same claim has incompatible values",
            ["evidence://one"]);

        var summary = new SubAgentFanInSummary(
            "fanout-001",
            SubAgentFanInStatus.CompletedWithFailures,
            [runResult],
            [conflict],
            ["evidence://one"],
            ["diagnostics://fanin-001"],
            "summary text");

        Assert.Equal("fanout-001", summary.FanoutCallId);
        Assert.Equal(SubAgentFanInStatus.CompletedWithFailures, summary.Status);
        Assert.Single(summary.Results);
        Assert.Single(summary.Results[0].ArtifactRefs);
        Assert.Single(summary.Conflicts);
        Assert.Single(summary.EvidenceRefs);
        Assert.Single(summary.DiagnosticsRefs);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentFanInSummary("fanout-002", SubAgentFanInStatus.Unspecified, [runResult]));
        Assert.Throws<ArgumentException>(() => new SubAgentFanInSummary("fanout-003", SubAgentFanInStatus.Completed, []));
    }

    [Fact]
    public void SubAgentTreeDiagnostics_ShouldPreserveNodesEdgesArtifactsAndFailures()
    {
        var quota = new SubAgentSpawnQuota(1, 4, 8, 2);
        var budget = new SubAgentTreeBudget(
            new KernelBudget(tokenBudget: 4000, timeBudgetMs: 30000, costBudget: 1, retryBudget: 1, toolCallBudget: 4),
            maxSubTasks: 4,
            maxDepth: 1,
            maxConcurrentAgents: 2);
        var root = new SubAgentNodeDiagnostics(
            "run-root",
            parentRunId: null,
            depth: 0,
            siblingIndex: 0,
            SubAgentRunStatus.Completed,
            "replay://root",
            ["diagnostics://root"],
            ["artifact://root/fanin"]);
        var failure = new SubAgentFailure("subagent.child_failed", "child failed");
        var child = new SubAgentNodeDiagnostics(
            "run-child-001",
            "run-root",
            depth: 1,
            siblingIndex: 0,
            SubAgentRunStatus.Failed,
            replaySummaryRef: "replay://child-001",
            diagnosticsRefs: ["diagnostics://child-001"],
            artifactRefs: ["artifact://child-001/report"],
            failure);
        var edge = new SubAgentEdgeDiagnostics(
            "run-root",
            "run-child-001",
            "call-subagent-001",
            "fanout-001",
            SubAgentRunStatus.Failed,
            ["diagnostics://edge-001"],
            ["artifact://child-001/report"],
            failure);

        var diagnostics = new SubAgentTreeDiagnostics(
            "run-root",
            "ledger-run-root",
            quota,
            budget,
            [root, child],
            [edge],
            ["replay://root", "replay://child-001"],
            ["diagnostics://edge-001"],
            "tree report");

        Assert.Equal("run-root", diagnostics.RootRunId);
        Assert.Equal("ledger-run-root", diagnostics.LedgerRef);
        Assert.Same(quota, diagnostics.Quota);
        Assert.Same(budget, diagnostics.Budget);
        Assert.Equal(2, diagnostics.Nodes.Count);
        Assert.Single(diagnostics.Edges);
        Assert.Equal("subagent.child_failed", diagnostics.Nodes[1].Failure?.Code);
        Assert.Equal("artifact://child-001/report", Assert.Single(diagnostics.Edges[0].ArtifactRefs));
        Assert.Equal("tree report", diagnostics.ReportText);
        Assert.Throws<ArgumentException>(() => new SubAgentTreeDiagnostics("run-root", "ledger-run-root", quota, budget, [], []));
    }

    [Fact]
    public void SubAgentTreeBudget_ShouldRejectInvalidBudgetGates()
    {
        var rootBudget = new KernelBudget(tokenBudget: 1000, timeBudgetMs: 30000, costBudget: 1, retryBudget: 1, toolCallBudget: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentTreeBudget(rootBudget, 0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentTreeBudget(rootBudget, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentTreeBudget(rootBudget, 1, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentTreeBudget(rootBudget, 1, 1, 0, maxCost: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentTreeBudget(rootBudget, 1, 1, 0, maxBudgetPerAgent: KernelBudget.Zero));
    }

    [Fact]
    public void SubAgentBudgetSplit_ShouldRejectInvalidPerAgentCeilings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentBudgetSplit(SubAgentBudgetSplitMode.Unspecified));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare, maxTokensPerAgent: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare, maxCostPerAgent: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare, maxTimePerAgent: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare, maxToolCallsPerAgent: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare, maxRetriesPerAgent: 0));
    }
}
