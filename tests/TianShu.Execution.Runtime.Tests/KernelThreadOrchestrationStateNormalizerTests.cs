using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelThreadOrchestrationStateNormalizerTests
{
    [Fact]
    public void ApplyOrchestrationStep_MergesLedgerAndPersistsContextPackageDetails()
    {
        var sessionId = new SessionId("session-001");
        var threadId = new ThreadId("thread-001");
        var source = new KernelThreadOrchestrationStateRecord
        {
            ContextLedgerSegments =
            [
                new KernelThreadStageContextSegmentStateRecord
                {
                    Kind = "stage_checkpoint",
                    Title = "Coding checkpoint",
                    Content = "coding completed",
                    Source = new KernelThreadResourceRefStateRecord
                    {
                        Kind = "stage_checkpoint",
                        Key = "checkpoint-coding",
                    },
                    Required = true,
                    EstimatedTokens = 16,
                },
            ],
        };
        var decision = new OrchestratorDecision(
            "decision-review",
            sessionId,
            threadId,
            BuiltInStageDefinitions.Review,
            [BuiltInStageDefinitions.Review],
            "checkpoint-suggestion",
            previousStageId: BuiltInStageDefinitions.Coding);
        var package = new StageContextPackage(
            "ctx-review",
            BuiltInStageDefinitions.Review,
            sessionId,
            threadId,
            segments:
            [
                new StageContextSegment(
                    "decision",
                    "review the implementation",
                    title: "Review input",
                    source: new ResourceRef("turn", "turn-review"),
                    required: true,
                    estimatedTokens: 24),
            ],
            artifactRefs:
            [
                new ArtifactRef(new ArtifactId("artifact-001"), "diff.patch", "patch"),
            ],
            sourceCheckpointIds: ["checkpoint-coding"],
            projectionReport: StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["includedSegmentCount"] = 1,
            }),
            metadata: new MetadataBag(new Dictionary<string, StructuredValue>
            {
                ["materializationMode"] = StructuredValue.FromString("full"),
            }));

        var next = KernelThreadOrchestrationStateNormalizer.ApplyOrchestrationStep(source, decision, package);

        Assert.Equal(2, next.ContextLedgerSegments.Count);
        Assert.Contains(
            next.ContextLedgerSegments,
            static segment => segment.Source?.Kind == "stage_checkpoint"
                              && segment.Source.Key == "checkpoint-coding");
        Assert.Contains(
            next.ContextLedgerSegments,
            static segment => segment.Source?.Kind == "turn"
                              && segment.Source.Key == "turn-review");

        Assert.NotNull(next.LastContextPackage);
        Assert.Equal("ctx-review", next.LastContextPackage!.PackageId);
        Assert.Equal(1, next.LastContextPackage.SegmentCount);
        Assert.Equal(1, next.LastContextPackage.ArtifactRefCount);
        Assert.Equal("turn-review", Assert.Single(next.LastContextPackage.Segments).Source?.Key);
        Assert.Equal("artifact-001", Assert.Single(next.LastContextPackage.ArtifactRefs).Id);
        Assert.Equal("1", next.LastContextPackage.ProjectionReport?.Properties["includedSegmentCount"].NumberValue);
        Assert.Equal("full", next.LastContextPackage.Metadata?.Properties["materializationMode"].StringValue);
    }
}
