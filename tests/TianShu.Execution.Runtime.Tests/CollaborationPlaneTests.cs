using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime.Tests;

public sealed class CollaborationPlaneTests
{
    [Fact]
    public async Task TianShuExecutionRuntime_ShouldManageCollaborationSpacesWithoutDefaultNotSupported()
    {
        var sut = new TianShuExecutionRuntime();
        var created = await sut.CreateSpaceAsync(
            new CreateCollaborationSpace(
                new CollaborationSpaceId("space-design"),
                "design",
                "Design",
                new CollaborationSpaceProfile("设计协作"),
                CollaborationDefaultSet.Empty),
            CancellationToken.None);

        var configured = await sut.ConfigureSpaceAsync(
            new ConfigureCollaborationSpace(
                created.Id,
                DisplayName: "Design Updated"),
            CancellationToken.None);
        var overview = await sut.GetSpaceOverviewAsync(new GetCollaborationSpaceOverview(created.Id), CancellationToken.None);
        var projection = await sut.GetSpaceProjectionAsync(new GetCollaborationSpaceProjection(created.Id), CancellationToken.None);
        var activeSpaces = await sut.ListSpacesAsync(new ListCollaborationSpaces(), CancellationToken.None);
        var archived = await sut.ArchiveSpaceAsync(new ArchiveCollaborationSpace(created.Id), CancellationToken.None);
        var allSpaces = await sut.ListSpacesAsync(new ListCollaborationSpaces(IncludeArchived: true), CancellationToken.None);

        Assert.Equal("Design", created.DisplayName);
        Assert.Equal("Design Updated", configured.DisplayName);
        Assert.Equal("Design Updated", overview?.DisplayName);
        Assert.Equal("Design Updated", projection?.CollaborationSpace.DisplayName);
        Assert.Contains(activeSpaces, static item => item.SpaceId.Value == "space-design");
        Assert.True(archived);
        Assert.DoesNotContain(allSpaces.Where(static item => !item.IsArchived), static item => item.SpaceId.Value == "space-design");
        Assert.Contains(allSpaces, static item => item.SpaceId.Value == "space-design" && item.IsArchived);
    }

    [Fact]
    public async Task TianShuExecutionRuntime_ShouldProjectParticipantsInsideCurrentCollaborationSpace()
    {
        var sut = new TianShuExecutionRuntime();
        var spaceId = new CollaborationSpaceId("space-agents");

        await sut.CreateSpaceAsync(
            new CreateCollaborationSpace(
                spaceId,
                "agents",
                "Agents",
                new CollaborationSpaceProfile("代理协作"),
                CollaborationDefaultSet.Empty),
            CancellationToken.None);

        var sessionBound = await sut.BindParticipantToSessionAsync(
            new BindParticipantToSession(new SessionId("session-001"), new ParticipantId("agent-planner")),
            CancellationToken.None);
        var workflowBound = await sut.BindParticipantToWorkflowAsync(
            new BindParticipantToWorkflow(new WorkflowId("workflow-001"), new ParticipantId("human-reviewer")),
            CancellationToken.None);
        var roleUpdated = await sut.UpdateParticipantRoleAsync(
            new UpdateParticipantRole(new ParticipantId("agent-planner"), "owner"),
            CancellationToken.None);
        var participant = await sut.GetParticipantProjectionAsync(new GetParticipantProjection(new ParticipantId("agent-planner")), CancellationToken.None);
        var participantView = await sut.GetParticipantViewProjectionAsync(new GetParticipantViewProjection(new ParticipantId("agent-planner")), CancellationToken.None);
        var participants = await sut.ListParticipantsInScopeAsync(new ListParticipantsInScope(spaceId), CancellationToken.None);

        Assert.True(sessionBound);
        Assert.True(workflowBound);
        Assert.True(roleUpdated);
        Assert.NotNull(participant);
        Assert.Equal(ParticipantKind.Agent, participant!.Kind);
        Assert.Equal("owner", participant.Role);
        Assert.Equal("owner", participantView?.Role);
        Assert.Equal("participant", participantView?.ScopeKind);
        Assert.Contains(participants, static item => item.ParticipantId.Value == "agent-planner" && item.Role == "owner");
        Assert.Contains(participants, static item => item.ParticipantId.Value == "human-reviewer" && item.Kind == ParticipantKind.Human);
    }
}
