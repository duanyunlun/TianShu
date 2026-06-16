using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Kernel;
using Xunit;

namespace TianShu.ControlPlane.Tests;

public sealed class SessionExecutionArtifactFactoryTests
{
    [Fact]
    public void CheckpointFactory_PreservesSelectedStageAndRouteKind()
    {
        var step = CreateStep("turn-checkpoint");
        var factory = new SessionCheckpointFactory();

        var checkpoint = factory.Create(
            step,
            StageExecutionState.Completed,
            DateTimeOffset.Parse("2026-06-09T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-09T00:01:00Z"),
            summary: "review completed");

        Assert.Equal(BuiltInStageDefinitions.Review, checkpoint.StageId);
        Assert.Equal(ModelRouteKind.Review, checkpoint.ModelRouteKind);
        Assert.Equal("review completed", checkpoint.Summary);
        Assert.StartsWith("checkpoint-", checkpoint.CheckpointId, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionRequestFactory_BindsStepContextAndModelRoute()
    {
        var step = CreateStep("turn-request");
        var route = new ModelRouteResolutionResult(
            "workbench",
            ModelRouteKind.Review,
            "review-provider",
            "review-model",
            0,
            "openai_chat_completions");
        var factory = new SessionExecutionRequestFactory();

        var request = factory.Create(step, route);

        Assert.StartsWith("execution-", request.ExecutionId, StringComparison.Ordinal);
        Assert.Equal(step.Stage.Id, request.StageId);
        Assert.Equal(step.Decision.DecisionId, request.Decision.DecisionId);
        Assert.Equal(step.ContextPackage.PackageId, request.ContextPackage.PackageId);
        Assert.Equal(route, request.ModelRoute);
        Assert.Equal(step.Stage.ExecutorBinding, request.ExecutorBinding);
    }

    [Fact]
    public void SessionOrchestrator_DoesNotCreateExecutionArtifacts()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "TianShu.Kernel", "SessionOrchestrator.cs"));
        var entryPlannerSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "TianShu.Kernel", "SessionCoreLoopEntryPlanner.cs"));

        Assert.DoesNotContain("public StageExecutionRequest CreateExecutionRequest(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public StageCheckpoint CreateCheckpoint(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StageContextPackage", source, StringComparison.Ordinal);
        Assert.Contains("private readonly SessionExecutionRequestFactory executionRequestFactory;", entryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("executionRequestFactory.Create(", entryPlannerSource, StringComparison.Ordinal);
    }

    private static SessionOrchestrationStep CreateStep(string correlationId)
    {
        var planner = new SessionCoreLoopEntryPlanner(BuiltInStageDefinitions.All);
        return planner.PlanEntry(
            new SessionOrchestrationInput(
                new SessionId("session-001"),
                new ThreadId("thread-001"),
                correlationId,
                previousStageId: BuiltInStageDefinitions.Coding,
                requestedStageId: BuiltInStageDefinitions.Review),
            static request => new SessionCoreLoopRouteResult(new ModelRouteResolutionResult(
                "workbench",
                request.RouteKind,
                "review-provider",
                "review-model",
                0,
                "openai_chat_completions"))).OrchestrationStep;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
