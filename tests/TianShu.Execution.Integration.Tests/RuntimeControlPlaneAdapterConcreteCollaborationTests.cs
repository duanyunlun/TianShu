using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime.ControlPlane;

namespace TianShu.Execution.Integration.Tests;

public sealed class RuntimeControlPlaneAdapterConcreteCollaborationTests
{
    [Fact]
    public async Task RuntimeControlPlaneAdapter_WithConcreteRuntime_ShouldExposeCollaborationSurface()
    {
        var runtime = new TianShuExecutionRuntime();
        var sut = new RuntimeControlPlaneAdapter(runtime);
        var spaceId = new CollaborationSpaceId("space-runtime-adapter");

        var created = await sut.CreateSpaceAsync(
            new CreateCollaborationSpace(
                spaceId,
                "runtime-adapter",
                "Runtime Adapter",
                new CollaborationSpaceProfile("运行时协作"),
                CollaborationDefaultSet.Empty),
            CancellationToken.None);
        var configured = await sut.ConfigureSpaceAsync(
            new ConfigureCollaborationSpace(spaceId, DisplayName: "Runtime Adapter Updated"),
            CancellationToken.None);
        var sessionBound = await sut.BindParticipantToSessionAsync(
            new BindParticipantToSession(new SessionId("session-adapter-001"), new ParticipantId("agent-runtime")),
            CancellationToken.None);
        var roleUpdated = await sut.UpdateParticipantRoleAsync(
            new UpdateParticipantRole(new ParticipantId("agent-runtime"), "facilitator"),
            CancellationToken.None);
        var participant = await sut.GetParticipantProjectionAsync(
            new GetParticipantProjection(new ParticipantId("agent-runtime")),
            CancellationToken.None);
        var participantView = await sut.GetParticipantViewProjectionAsync(
            new GetParticipantViewProjection(new ParticipantId("agent-runtime")),
            CancellationToken.None);
        var participants = await sut.ListParticipantsInScopeAsync(new ListParticipantsInScope(spaceId), CancellationToken.None);
        var overview = await sut.GetSpaceOverviewAsync(new GetCollaborationSpaceOverview(spaceId), CancellationToken.None);
        var projection = await sut.GetSpaceProjectionAsync(new GetCollaborationSpaceProjection(spaceId), CancellationToken.None);

        Assert.Equal("Runtime Adapter", created.DisplayName);
        Assert.Equal("Runtime Adapter Updated", configured.DisplayName);
        Assert.True(sessionBound);
        Assert.True(roleUpdated);
        Assert.Equal("facilitator", participant?.Role);
        Assert.Equal("facilitator", participantView?.Role);
        Assert.Contains(participants, static item => item.ParticipantId.Value == "agent-runtime" && item.Role == "facilitator");
        Assert.Equal("Runtime Adapter Updated", overview?.DisplayName);
        Assert.Equal("Runtime Adapter Updated", projection?.CollaborationSpace.DisplayName);
    }
}
