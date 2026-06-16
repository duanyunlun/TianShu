using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelThreadOrchestrationInputProjectionFactoryTests
{
    [Fact]
    public void Project_WhenStateExists_MapsCoreLoopInputContractValues()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-11T08:01:00Z");
        var completedAt = DateTimeOffset.Parse("2026-06-11T08:02:00Z");
        var diagnostics = StructuredValue.FromString("ok");
        var state = new KernelThreadOrchestrationStateRecord
        {
            CurrentStageId = " coding ",
            ContextLedgerSegments =
            [
                new KernelThreadStageContextSegmentStateRecord
                {
                    Kind = " workspace_state ",
                    Content = " cwd=D:/repo ",
                    Title = " Workspace ",
                    Source = new KernelThreadResourceRefStateRecord
                    {
                        Kind = " workspace ",
                        Key = " D:/repo ",
                    },
                    Required = true,
                    EstimatedTokens = 16,
                },
                new KernelThreadStageContextSegmentStateRecord
                {
                    Kind = " ",
                    Content = "ignored",
                },
            ],
            Checkpoints =
            [
                new KernelThreadStageCheckpointStateRecord
                {
                    CheckpointId = " checkpoint-1 ",
                    StageId = " planning ",
                    State = StageExecutionState.Completed,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    Summary = " done ",
                    ArtifactRefs =
                    [
                        new KernelThreadArtifactRefStateRecord
                        {
                            Id = " artifact-1 ",
                            Name = " result ",
                            Kind = " markdown ",
                        },
                        new KernelThreadArtifactRefStateRecord
                        {
                            Id = " ",
                            Name = "ignored",
                        },
                    ],
                    ModelRouteSetId = " routes ",
                    ModelRouteKind = " coding ",
                    Diagnostics = diagnostics,
                    NextStageSuggestions = ["review"],
                },
                new KernelThreadStageCheckpointStateRecord
                {
                    CheckpointId = "checkpoint-2",
                    StageId = "coding",
                    State = StageExecutionState.Running,
                    StartedAt = startedAt,
                },
                new KernelThreadStageCheckpointStateRecord
                {
                    CheckpointId = " ",
                    StageId = "ignored",
                },
            ],
        };

        var projection = KernelThreadOrchestrationInputProjectionFactory.Project(state);

        Assert.Equal("coding", projection.CurrentStageId);
        Assert.Equal("coding", projection.LatestCheckpointStageId);
        var segment = Assert.Single(projection.ContextLedgerSegments);
        Assert.Equal("workspace_state", segment.Kind);
        Assert.Equal("cwd=D:/repo", segment.Content);
        Assert.Equal("Workspace", segment.Title);
        Assert.True(segment.Required);
        Assert.Equal(16, segment.EstimatedTokens);
        Assert.Equal("workspace", segment.Source?.Kind);
        Assert.Equal("D:/repo", segment.Source?.Key);

        Assert.Equal(2, projection.Checkpoints.Count);
        var checkpoint = projection.Checkpoints[0];
        Assert.Equal("checkpoint-1", checkpoint.CheckpointId);
        Assert.Equal("planning", checkpoint.StageId);
        Assert.Equal(StageExecutionState.Completed, checkpoint.State);
        Assert.Equal(startedAt, checkpoint.StartedAt);
        Assert.Equal(completedAt, checkpoint.CompletedAt);
        Assert.Equal("done", checkpoint.Summary);
        Assert.Equal("routes", checkpoint.ModelRouteSetId);
        Assert.Equal("coding", checkpoint.ModelRouteKind?.Value);
        Assert.Same(diagnostics, checkpoint.Diagnostics);
        Assert.Equal(["review"], checkpoint.NextStageSuggestions);
        var artifact = Assert.Single(checkpoint.ArtifactRefs);
        Assert.Equal("artifact-1", artifact.Id.Value);
        Assert.Equal("result", artifact.Name);
        Assert.Equal("markdown", artifact.Kind);
    }

    [Fact]
    public void Project_WhenStateIsNull_ReturnsEmptyProjection()
    {
        var projection = KernelThreadOrchestrationInputProjectionFactory.Project(null);

        Assert.Null(projection.CurrentStageId);
        Assert.Null(projection.LatestCheckpointStageId);
        Assert.Empty(projection.Checkpoints);
        Assert.Empty(projection.ContextLedgerSegments);
    }
}
