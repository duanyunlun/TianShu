using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Workflows;

namespace TianShu.Contracts.Projections.Tests;

public sealed class ProjectionContractTests
{
    [Fact]
    public void ProjectionSubscription_RejectsBlankScopeKey()
    {
        Assert.Throws<ArgumentException>(() => new ProjectionSubscription(
            new SubscriptionToken("subscription-001"),
            ProjectionScopeKind.Thread,
            " "));
    }

    [Fact]
    public void AgentRosterProjection_PreservesEntries()
    {
        var participant = new ServiceParticipant(
            new ParticipantId("participant-010"),
            "Worker",
            "implementer");
        var projection = new AgentRosterProjection(new[]
        {
            new AgentRosterEntry(
                new AgentId("agent-010"),
                ParticipantRef.From(participant),
                "implementer",
                1,
                IsBusy: true),
        });

        var entry = Assert.Single(projection.Items);
        Assert.Equal("implementer", entry.Role);
        Assert.True(entry.IsBusy);
    }

    [Fact]
    public void ProjectionDelta_CarriesTypedPayload()
    {
        var collaboration = new CollaborationSpace(
            new CollaborationSpaceId("space-010"),
            "contracts",
            "Contracts",
            new CollaborationSpaceProfile("设计"),
            CollaborationDefaultSet.Empty);
        var payload = new CollaborationSpaceProjectionPayload(
            new CollaborationSpaceProjection(CollaborationSpaceRef.From(collaboration), 1, 2, false));
        var delta = new ProjectionDelta(payload, new ProjectionCursor("cursor-010"));

        Assert.Equal("collaboration_space", delta.Payload.Kind);
        Assert.Equal("cursor-010", delta.Cursor?.Value);
    }

    [Fact]
    public void ProjectionDelta_CarriesPlanTypedPayload()
    {
        var delta = new ProjectionDelta(
            new PlanProjectionPayload(
                new PlanProjection(
                    new WorkflowId("workflow-plan-010"),
                    new Plan(
                        "Contracts Plan",
                        [
                            new PlanStep(
                                0,
                                "Wire host plan scope",
                                "Keep the host plan projection available."),
                        ]))),
            new ProjectionCursor("cursor-plan-010"));

        var payload = Assert.IsType<PlanProjectionPayload>(delta.Payload);
        Assert.Equal("plan", payload.Kind);
        Assert.Equal("workflow-plan-010", payload.Projection.WorkflowId.Value);
        Assert.Equal("Contracts Plan", payload.Projection.Plan.Title);
        Assert.Equal("Wire host plan scope", Assert.Single(payload.Projection.Plan.Steps).Title);
    }

    [Fact]
    public void ProjectionDelta_CarriesTeamTypedPayload()
    {
        var delta = new ProjectionDelta(
            new TeamProjectionPayload(
                new TeamProjection(
                    new Team(
                        new TeamId("team-projection-010"),
                        "Contracts Team",
                        [new AgentId("agent-team-010")]),
                    [
                        new Agent(
                            new AgentId("agent-team-010"),
                            new ParticipantRef(new ParticipantId("participant-team-010"), ParticipantKind.Agent, "Contracts Agent"),
                            "Contracts Agent",
                            AgentRole.Implementer),
                    ])),
            new ProjectionCursor("cursor-team-010"));

        var payload = Assert.IsType<TeamProjectionPayload>(delta.Payload);
        Assert.Equal("team", payload.Kind);
        Assert.Equal("team-projection-010", payload.Projection.Team.Id.Value);
        Assert.Equal("Contracts Team", payload.Projection.Team.DisplayName);
        Assert.Equal("Contracts Agent", Assert.Single(payload.Projection.Members).DisplayName);
    }

    [Fact]
    public void ArtifactCollectionProjection_PreservesTypedItems()
    {
        var collaboration = new CollaborationSpaceRef(
            new CollaborationSpaceId("space-artifact-001"),
            "artifact-space",
            "Artifact Space");
        var participant = ParticipantRef.From(
            new ServiceParticipant(
                new ParticipantId("participant-artifact-001"),
                "artifact-bot",
                "publisher"));
        var projection = new ArtifactCollectionProjection(
            collaboration,
            new[]
            {
                new ArtifactCollectionItem(
                    new ArtifactId("artifact-collection-001"),
                    "summary.md",
                    ArtifactKind.Document,
                    ArtifactLifecycleState.Promoted,
                    participant,
                    new[] { "stable" },
                    new[] { new TaskId("task-artifact-001") },
                    new DateTimeOffset(2026, 4, 15, 9, 0, 0, TimeSpan.Zero)),
            });

        var item = Assert.Single(projection.Items);
        Assert.Equal(ArtifactKind.Document, item.Kind);
        Assert.Equal(ArtifactLifecycleState.Promoted, item.State);
        Assert.Equal("stable", Assert.Single(item.PromotionChannels));
        Assert.Equal("task-artifact-001", Assert.Single(item.AttachedTaskIds).Value);
        Assert.Equal("artifact-space", projection.CollaborationSpace.Key);
    }

    [Fact]
    public void ControlPlaneProjectionEvent_PreservesTypedEnvelope()
    {
        var streamEvent = new ControlPlaneProjectionEvent
        {
            Kind = "ApprovalRequested",
            Timestamp = new DateTimeOffset(2026, 4, 8, 10, 0, 0, TimeSpan.Zero),
            ThreadId = new ThreadId("thread-projection-001"),
            TurnId = new TurnId("turn-projection-001"),
            CallId = new CallId("call-projection-001"),
            ToolName = "shell",
            RequiresApproval = true,
            Payload = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["approvalKind"] = StructuredValue.FromString("shell_command"),
                ["summary"] = StructuredValue.FromString("需要批准"),
            }),
        };

        Assert.Equal("ApprovalRequested", streamEvent.Kind);
        Assert.Equal("thread-projection-001", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-projection-001", streamEvent.TurnId?.Value);
        Assert.Equal("call-projection-001", streamEvent.CallId?.Value);
        Assert.True(streamEvent.RequiresApproval);
        Assert.Equal("shell_command", streamEvent.Payload?.Properties["approvalKind"].StringValue);
    }

    [Fact]
    public void ControlPlaneProjectionEvent_PreservesTypedViewUpdatePayloads()
    {
        var collaboration = new CollaborationSpace(
            new CollaborationSpaceId("space-projection-001"),
            "host",
            "Host",
            new CollaborationSpaceProfile("host"),
            CollaborationDefaultSet.Empty);
        var delta = new ProjectionDelta(
            new ThreadProjectionPayload(
                new ThreadProjection(
                    new ThreadId("thread-projection-002"),
                    "thread",
                    CollaborationSpaceRef.From(collaboration))),
            new ProjectionCursor("cursor-delta-001"));
        var reset = new ProjectionReset(
            ProjectionScopeKind.Thread,
            "thread-projection-002",
            "resync",
            new ProjectionCursor("cursor-reset-001"));

        var streamEvent = new ControlPlaneProjectionEvent
        {
            Kind = "ProjectionDelta",
            Timestamp = DateTimeOffset.UtcNow,
            Delta = delta,
            Reset = reset,
        };

        Assert.Equal("thread", Assert.IsType<ThreadProjectionPayload>(streamEvent.Delta!.Payload).Kind);
        Assert.Equal("thread-projection-002", streamEvent.Reset!.ScopeKey);
        Assert.Equal("resync", streamEvent.Reset.Reason);
    }

    [Fact]
    public void ControlPlaneThreadSubscription_AllowsOptionalThreadFilter()
    {
        var filtered = new ControlPlaneThreadSubscription
        {
            ThreadId = new ThreadId("thread-filter-001"),
        };
        var unfiltered = new ControlPlaneThreadSubscription();

        Assert.Equal("thread-filter-001", filtered.ThreadId?.Value);
        Assert.Null(unfiltered.ThreadId);
    }
}
