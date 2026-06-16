namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelThreadObservedStateProjectionFactoryTests
{
    [Fact]
    public void ProjectArtifactRefs_WhenStateExists_MapsContextPackageAndCheckpointArtifacts()
    {
        var state = new KernelThreadOrchestrationStateRecord
        {
            LastContextPackage = new KernelThreadStageContextPackageStateRecord
            {
                ArtifactRefs =
                [
                    new KernelThreadArtifactRefStateRecord
                    {
                        Id = " context-artifact ",
                        Name = " context.md ",
                        Kind = " markdown ",
                    },
                    new KernelThreadArtifactRefStateRecord
                    {
                        Id = " ",
                        Name = "ignored",
                    },
                ],
            },
            Checkpoints =
            [
                new KernelThreadStageCheckpointStateRecord
                {
                    ArtifactRefs =
                    [
                        new KernelThreadArtifactRefStateRecord
                        {
                            Id = " checkpoint-artifact ",
                            Name = " diff.patch ",
                            Kind = " patch ",
                        },
                    ],
                },
            ],
        };

        var artifactRefs = KernelThreadObservedStateProjectionFactory.ProjectArtifactRefs(state);

        Assert.Equal(2, artifactRefs.Count);
        Assert.Equal("context-artifact", artifactRefs[0].Id.Value);
        Assert.Equal("context.md", artifactRefs[0].Name);
        Assert.Equal("markdown", artifactRefs[0].Kind);
        Assert.Equal("checkpoint-artifact", artifactRefs[1].Id.Value);
        Assert.Equal("diff.patch", artifactRefs[1].Name);
        Assert.Equal("patch", artifactRefs[1].Kind);
    }

    [Fact]
    public void ProjectArtifactRefs_WhenStateIsNull_ReturnsEmptyRefs()
    {
        var artifactRefs = KernelThreadObservedStateProjectionFactory.ProjectArtifactRefs(null);

        Assert.Empty(artifactRefs);
    }
}
