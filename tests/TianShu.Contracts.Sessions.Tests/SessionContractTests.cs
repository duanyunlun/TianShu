using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;

namespace TianShu.Contracts.Sessions.Tests;

public sealed class SessionContractTests
{
    [Fact]
    public void SessionProfile_RejectsBlankTitle()
    {
        Assert.Throws<ArgumentException>(() => new SessionProfile(" "));
    }

    [Fact]
    public void Session_PreservesCollaborationAndParticipants()
    {
        var space = new CollaborationSpace(
            new CollaborationSpaceId("space-session"),
            "design",
            "Design",
            new CollaborationSpaceProfile("架构"),
            CollaborationDefaultSet.Empty);
        var participant = new ServiceParticipant(
            new ParticipantId("participant-session"),
            "Coordinator",
            "owner");
        var session = new Session(
            new SessionId("session-001"),
            CollaborationSpaceRef.From(space),
            new SessionProfile("Contracts"),
            SessionMode.Planning,
            activeParticipants: new[] { ParticipantRef.From(participant) });

        Assert.Equal("Contracts", session.Profile.Title);
        Assert.Single(session.ActiveParticipants);
        Assert.Equal(SessionMode.Planning, session.Mode);
    }

    [Fact]
    public void ControlPlaneSessionSnapshot_PreservesRuntimeIndicators()
    {
        var snapshot = new ControlPlaneSessionSnapshot
        {
            RuntimeName = "tianshu",
            ActiveThreadId = new ThreadId("thread-session-001"),
            HasActiveTurn = true,
        };

        Assert.Equal("tianshu", snapshot.RuntimeName);
        Assert.Equal("thread-session-001", snapshot.ActiveThreadId?.Value);
        Assert.True(snapshot.HasActiveTurn);
    }

    [Fact]
    public void SessionOverviewProjection_PreservesFormalSessionState()
    {
        var projection = new SessionOverviewProjection(
            new SessionId("session-overview-001"),
            "架构收口",
            new CollaborationSpaceRef(
                new CollaborationSpaceId("space-overview-001"),
                "tianshu-runtime",
                "TianShu Runtime"),
            SessionMode.Review,
            new ThreadId("thread-overview-001"),
            HasActiveTurn: true,
            IsClosed: false);

        Assert.Equal("session-overview-001", projection.SessionId.Value);
        Assert.Equal("架构收口", projection.Title);
        Assert.Equal("space-overview-001", projection.CollaborationSpace.Id.Value);
        Assert.Equal("tianshu-runtime", projection.CollaborationSpace.Key);
        Assert.Equal(SessionMode.Review, projection.Mode);
        Assert.Equal("thread-overview-001", projection.ActiveThreadId?.Value);
        Assert.True(projection.HasActiveTurn);
        Assert.False(projection.IsClosed);
    }

    [Fact]
    public void ControlPlaneInitializeRuntimeCommand_PreservesTypedNorthboundFields()
    {
        var command = new ControlPlaneInitializeRuntimeCommand
        {
            WorkingDirectory = "/workspace/tianshu",
            ApprovalPolicy = "never",
            ServiceTier = "flex",
            SessionSource = ControlPlaneSessionSource.Cli,
            DynamicTools =
            [
                new ControlPlaneDynamicToolSpec
                {
                    Name = "mcp__calendar__find_events",
                    Description = "查询日历事件。",
                    InputSchema = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["type"] = StructuredValue.FromString("object"),
                    }),
                },
            ],
            OutputSchema = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["type"] = StructuredValue.FromString("object"),
            }),
        };

        Assert.Equal("/workspace/tianshu", command.WorkingDirectory);
        Assert.Equal("never", command.ApprovalPolicy);
        Assert.Equal("flex", command.ServiceTier);
        Assert.Equal(ControlPlaneSessionSource.Cli, command.SessionSource);
        Assert.Single(command.DynamicTools!);
        Assert.Equal("mcp__calendar__find_events", command.DynamicTools![0].Name);
        Assert.Equal("object", command.OutputSchema?.Properties["type"].StringValue);
    }
}
