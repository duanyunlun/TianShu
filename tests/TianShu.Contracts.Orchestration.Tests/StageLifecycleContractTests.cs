using System.Text.Json;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Orchestration.Tests;

public sealed class StageLifecycleContractTests
{
    [Fact]
    public void BuiltInStageDefinitions_DefineCanonicalLifecycleRoutes()
    {
        var stages = BuiltInStageDefinitions.All;

        Assert.Equal(
            [
                BuiltInStageDefinitions.Default,
                BuiltInStageDefinitions.Planning,
                BuiltInStageDefinitions.LongContext,
                BuiltInStageDefinitions.Coding,
                BuiltInStageDefinitions.Review,
                BuiltInStageDefinitions.Summarization,
                BuiltInStageDefinitions.MemoryExtraction,
                BuiltInStageDefinitions.Fast,
            ],
            stages.Select(static stage => stage.Id).ToArray());
        Assert.Equal(ModelRouteKind.Coding, stages.Single(static stage => stage.Id == BuiltInStageDefinitions.Coding).ModelRouteKind);
        Assert.Contains(BuiltInStageDefinitions.Review, stages.Single(static stage => stage.Id == BuiltInStageDefinitions.Coding).AllowedNext);
        Assert.Equal(StageContextProjectionMode.ReferencesOnly, stages.Single(static stage => stage.Id == BuiltInStageDefinitions.LongContext).ContextProjectionMode);
    }

    [Fact]
    public void StageDefinition_RejectsInvalidLifecycleShape()
    {
        Assert.Throws<ArgumentException>(() => new StageDefinition(" ", "Coding", 1));
        Assert.Throws<ArgumentException>(() => new StageDefinition("coding", " ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StageDefinition("coding", "Coding", -1));
        Assert.Throws<ArgumentException>(() => new StageDefinition("coding", "Coding", 1, allowedNext: ["review", "review"]));
        Assert.Throws<ArgumentException>(() => new StageDefinition("coding", "Coding", 1, requiredCapabilities: ["tools", " "]));
    }

    [Fact]
    public void StageTransitionDefinition_RejectsInvalidEdges()
    {
        Assert.Throws<ArgumentException>(() => new StageTransitionDefinition(" ", "coding", "selected"));
        Assert.Throws<ArgumentException>(() => new StageTransitionDefinition("planning", " ", "selected"));
        Assert.Throws<ArgumentException>(() => new StageTransitionDefinition("planning", "coding", " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StageTransitionDefinition("planning", "coding", "selected", priority: -1));
    }

    [Fact]
    public void StageContextPackage_PreservesProjectedSegmentsAndRefs()
    {
        var package = new StageContextPackage(
            "ctx-1",
            BuiltInStageDefinitions.Review,
            new SessionId("session-1"),
            new ThreadId("thread-1"),
            segments:
            [
                new StageContextSegment("summary", "coding stage completed", required: true, estimatedTokens: 32),
            ],
            artifactRefs:
            [
                new ArtifactRef(new ArtifactId("artifact-1"), "diff.patch", "patch"),
            ],
            sourceCheckpointIds: ["checkpoint-coding"],
            budgetTokens: 4096);

        Assert.Equal("ctx-1", package.PackageId);
        Assert.Equal(BuiltInStageDefinitions.Review, package.StageId);
        Assert.Equal("summary", Assert.Single(package.Segments).Kind);
        Assert.Equal("artifact-1", Assert.Single(package.ArtifactRefs).Id.Value);
        Assert.Equal("checkpoint-coding", Assert.Single(package.SourceCheckpointIds));
        Assert.Equal(4096, package.BudgetTokens);
    }

    [Fact]
    public void StageCheckpoint_RejectsTimeTravelAndPreservesModelRoute()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-09T00:00:00Z");

        Assert.Throws<ArgumentOutOfRangeException>(() => new StageCheckpoint(
            "checkpoint-1",
            BuiltInStageDefinitions.Coding,
            StageExecutionState.Completed,
            startedAt,
            startedAt.AddSeconds(-1)));

        var checkpoint = new StageCheckpoint(
            "checkpoint-1",
            BuiltInStageDefinitions.Coding,
            StageExecutionState.Completed,
            startedAt,
            startedAt.AddMinutes(1),
            summary: "实现完成",
            modelRouteSetId: "default",
            modelRouteKind: ModelRouteKind.Coding,
            nextStageSuggestions: [BuiltInStageDefinitions.Review]);

        Assert.Equal("default", checkpoint.ModelRouteSetId);
        Assert.Equal(ModelRouteKind.Coding, checkpoint.ModelRouteKind);
        Assert.Equal(BuiltInStageDefinitions.Review, Assert.Single(checkpoint.NextStageSuggestions));
    }

    [Fact]
    public void StageExecutionRequest_RequiresConsistentDecisionContextAndRoute()
    {
        var sessionId = new SessionId("session-1");
        var threadId = new ThreadId("thread-1");
        var stage = BuiltInStageDefinitions.All.Single(static item => item.Id == BuiltInStageDefinitions.Review);
        var decision = new OrchestratorDecision(
            "decision-1",
            sessionId,
            threadId,
            BuiltInStageDefinitions.Review,
            [BuiltInStageDefinitions.Review],
            "requested-stage");
        var package = new StageContextPackage(
            "ctx-1",
            BuiltInStageDefinitions.Review,
            sessionId,
            threadId);
        var route = new ModelRouteResolutionResult(
            "default",
            ModelRouteKind.Review,
            "openai",
            "gpt-5",
            0,
            "openai_responses");

        var request = new StageExecutionRequest("execution-1", stage, decision, package, route);

        Assert.Equal("execution-1", request.ExecutionId);
        Assert.Equal(BuiltInStageDefinitions.Review, request.StageId);
        Assert.Equal(stage.ExecutorBinding, request.ExecutorBinding);
        Assert.Equal(route, request.ModelRoute);

        Assert.Throws<ArgumentException>(() => new StageExecutionRequest(
            "execution-2",
            stage,
            decision,
            package,
            new ModelRouteResolutionResult("default", ModelRouteKind.Coding, "openai", "gpt-5", 0)));
    }

    [Fact]
    public void OrchestratorDecision_RequiresSelectedStageInCandidates()
    {
        Assert.Throws<ArgumentException>(() => new OrchestratorDecision(
            "decision-1",
            new SessionId("session-1"),
            new ThreadId("thread-1"),
            BuiltInStageDefinitions.Review,
            [BuiltInStageDefinitions.Coding],
            "policy-selected"));

        var decision = new OrchestratorDecision(
            "decision-1",
            new SessionId("session-1"),
            new ThreadId("thread-1"),
            BuiltInStageDefinitions.Review,
            [BuiltInStageDefinitions.Coding, BuiltInStageDefinitions.Review],
            "policy-selected",
            previousStageId: BuiltInStageDefinitions.Coding,
            policyHits: ["review-after-coding"]);

        Assert.Equal(BuiltInStageDefinitions.Review, decision.SelectedStageId);
        Assert.Equal("review-after-coding", Assert.Single(decision.PolicyHits));
    }

    [Fact]
    public void StageContracts_RoundTripThroughSystemTextJson()
    {
        var package = new StageContextPackage(
            "ctx-1",
            BuiltInStageDefinitions.Coding,
            new SessionId("session-1"),
            new ThreadId("thread-1"),
            segments: [new StageContextSegment("user_goal", "fix the bug")],
            projectionReport: StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["mode"] = StructuredValue.FromString("selected"),
            }));

        var json = JsonSerializer.Serialize(package);
        var restored = JsonSerializer.Deserialize<StageContextPackage>(json);

        Assert.NotNull(restored);
        Assert.Equal(package.PackageId, restored!.PackageId);
        Assert.Equal(package.SessionId, restored.SessionId);
        Assert.Equal("selected", restored.ProjectionReport?.Properties["mode"].StringValue);
    }
}
