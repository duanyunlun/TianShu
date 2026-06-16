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
}
