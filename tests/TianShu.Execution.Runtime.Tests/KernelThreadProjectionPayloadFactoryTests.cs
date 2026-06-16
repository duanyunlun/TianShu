using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelThreadProjectionPayloadFactoryTests
{
    [Fact]
    public void ToSessionProjectionPayload_WhenOrchestrationExists_MapsRuntimeProtocolPayload()
    {
        var decidedAt = DateTimeOffset.Parse("2026-06-11T08:00:00Z");
        var startedAt = DateTimeOffset.Parse("2026-06-11T08:01:00Z");
        var completedAt = DateTimeOffset.Parse("2026-06-11T08:02:00Z");
        var sessionState = new KernelThreadSessionProjectionStateRecord
        {
            SessionId = "session-1",
            Title = "Session",
            CollaborationSpaceId = "space-1",
            CollaborationSpaceKey = "space-key",
            CollaborationSpaceDisplayName = "Space",
            SessionMode = "interactive",
            IsClosed = false,
            ActiveThreadId = "thread-1",
            HasActiveTurn = true,
            Orchestration = new KernelThreadOrchestrationStateRecord
            {
                CurrentStageId = "coding",
                LastDecision = new KernelThreadOrchestratorDecisionStateRecord
                {
                    DecisionId = "decision-1",
                    SelectedStageId = "coding",
                    CandidateStageIds = ["planning", "coding"],
                    ReasonCode = "requested",
                    PreviousStageId = "planning",
                    ContextProjectionReason = "selected",
                    PolicyHits = ["runtime-policy-context"],
                    DecidedAt = decidedAt,
                },
                LastContextPackage = new KernelThreadStageContextPackageStateRecord
                {
                    PackageId = "package-1",
                    StageId = "coding",
                    ProjectionMode = StageContextProjectionMode.SelectedSegments,
                    BudgetTokens = 1024,
                    SourceCheckpointIds = ["checkpoint-0"],
                    SegmentCount = 1,
                    ArtifactRefCount = 1,
                },
                ContextLedgerSegments =
                [
                    new KernelThreadStageContextSegmentStateRecord
                    {
                        Kind = "workspace_state",
                        Content = "cwd=D:/repo",
                        Source = new KernelThreadResourceRefStateRecord
                        {
                            Kind = "workspace",
                            Key = "D:/repo",
                        },
                        Required = true,
                        EstimatedTokens = 16,
                    },
                ],
                Checkpoints =
                [
                    new KernelThreadStageCheckpointStateRecord
                    {
                        CheckpointId = "checkpoint-1",
                        StageId = "coding",
                        State = StageExecutionState.Completed,
                        StartedAt = startedAt,
                        CompletedAt = completedAt,
                        Summary = "done",
                        ArtifactRefs =
                        [
                            new KernelThreadArtifactRefStateRecord
                            {
                                Id = "artifact-1",
                                Name = "result",
                                Kind = "markdown",
                            },
                        ],
                        ModelRouteSetId = "routes",
                        ModelRouteKind = "coding",
                        NextStageSuggestions = ["review"],
                    },
                ],
            },
        };

        var payload = KernelThreadProjectionPayloadFactory.ToSessionProjectionPayload(sessionState);

        Assert.NotNull(payload);
        Assert.Equal("session-1", payload.SessionId);
        Assert.Equal("thread-1", payload.ActiveThreadId);
        Assert.True(payload.HasActiveTurn);
        Assert.NotNull(payload.Orchestration);
        Assert.Equal("coding", payload.Orchestration.CurrentStageId);
        Assert.Equal("decision-1", payload.Orchestration.LastDecision?.DecisionId);
        Assert.Equal(["planning", "coding"], payload.Orchestration.LastDecision?.CandidateStageIds);
        Assert.Equal(decidedAt, payload.Orchestration.LastDecision?.DecidedAt);
        Assert.Equal("package-1", payload.Orchestration.LastContextPackage?.PackageId);
        Assert.Equal("SelectedSegments", payload.Orchestration.LastContextPackage?.ProjectionMode);
        var segment = Assert.Single(payload.Orchestration.ContextLedgerSegments);
        Assert.Equal("workspace_state", segment.Kind);
        Assert.Equal("workspace", segment.Source?.Kind);
        var checkpoint = Assert.Single(payload.Orchestration.Checkpoints);
        Assert.Equal("checkpoint-1", checkpoint.CheckpointId);
        Assert.Equal("Completed", checkpoint.State);
        Assert.Equal(completedAt, checkpoint.CompletedAt);
        var artifact = Assert.Single(checkpoint.ArtifactRefs);
        Assert.Equal("artifact-1", artifact.Id);
    }
}
