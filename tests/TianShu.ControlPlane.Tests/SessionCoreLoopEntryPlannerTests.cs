using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Kernel;
using Xunit;

namespace TianShu.ControlPlane.Tests;

public sealed class SessionCoreLoopEntryPlannerTests
{
    [Fact]
    public void PlanEntry_BindsOrchestrationRouteExecutionRequestAndExecutorContext()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-09T00:00:00Z");
        var planner = new SessionCoreLoopEntryPlanner(BuiltInStageDefinitions.All);
        var routeRequests = new List<SessionCoreLoopRouteRequest>();
        var input = new SessionOrchestrationInput(
            new SessionId("session-001"),
            new ThreadId("thread-001"),
            "turn-001",
            previousStageId: BuiltInStageDefinitions.Coding,
            requestedStageId: BuiltInStageDefinitions.Review,
            checkpoints:
            [
                new StageCheckpoint(
                    "checkpoint-coding",
                    BuiltInStageDefinitions.Coding,
                    StageExecutionState.Completed,
                    DateTimeOffset.Parse("2026-06-09T00:00:00Z")),
            ],
            contextLedgerSegments:
            [
                new StageContextSegment("summary", "coding completed", required: true, estimatedTokens: 32),
                new StageContextSegment("trace", "large trace", estimatedTokens: 64),
            ],
            contextBudgetTokens: 40);

        var plan = planner.PlanEntry(
            input,
            request =>
            {
                routeRequests.Add(request);
                return new SessionCoreLoopRouteResult(
                    new ModelRouteResolutionResult(
                        "workbench",
                        request.RouteKind,
                        "review-provider",
                        "review-model",
                        0,
                        "openai_chat_completions"),
                    "model-route-correlation-001");
            },
            startedAt);

        var routeRequest = Assert.Single(routeRequests);
        Assert.Equal(BuiltInStageDefinitions.Review, routeRequest.Stage.Id);
        Assert.Equal(ModelRouteKind.Review, routeRequest.RouteKind);
        Assert.Contains(routeRequest.RegisteredRouteKinds, static routeKind => routeKind == ModelRouteKind.Review);
        Assert.Equal(BuiltInStageDefinitions.Review, plan.OrchestrationStep.Stage.Id);
        Assert.Equal(plan.OrchestrationStep.Decision.DecisionId, plan.ExecutionRequest.Decision.DecisionId);
        Assert.Equal(plan.OrchestrationStep.ContextPackage.PackageId, plan.ExecutionRequest.ContextPackage.PackageId);
        Assert.Equal("checkpoint-coding", Assert.Single(plan.OrchestrationStep.ContextPackage.SourceCheckpointIds));
        var segment = Assert.Single(plan.OrchestrationStep.ContextPackage.Segments);
        Assert.Equal("summary", segment.Kind);
        Assert.True(segment.Required);
        Assert.Equal(plan.ModelRoute, plan.ExecutionRequest.ModelRoute);
        Assert.Equal(plan.ExecutionRequest.ExecutionId, plan.ExecutorRuntimeContext.ExecutionId);
        Assert.Equal(plan.ExecutionRequest.ExecutorBinding, plan.ExecutorRuntimeContext.ExecutorBinding);
        Assert.Equal(plan.ExecutionRequest.StageId, plan.ExecutorRuntimeContext.StageId);
        Assert.Equal(plan.OrchestrationStep.Decision.DecisionId, plan.ExecutorRuntimeContext.DecisionId);
        Assert.Equal(plan.OrchestrationStep.ContextPackage.PackageId, plan.ExecutorRuntimeContext.ContextPackageId);
        Assert.Equal("workbench", plan.ExecutorRuntimeContext.ModelRouteSetId);
        Assert.Equal(ModelRouteKind.Review, plan.ExecutorRuntimeContext.ModelRouteKind);
        Assert.Equal("model-route-correlation-001", plan.ExecutorRuntimeContext.ModelRouteDiagnosticsCorrelationId);
        Assert.Equal(startedAt, plan.ExecutorRuntimeContext.StartedAt);
    }

    [Fact]
    public void PlanEntry_RejectsRouteResultThatDoesNotMatchSelectedStage()
    {
        var planner = new SessionCoreLoopEntryPlanner(BuiltInStageDefinitions.All);
        var input = new SessionOrchestrationInput(
            new SessionId("session-001"),
            new ThreadId("thread-001"),
            "turn-002",
            previousStageId: BuiltInStageDefinitions.Coding,
            requestedStageId: BuiltInStageDefinitions.Review);

        Assert.Throws<ArgumentException>(() => planner.PlanEntry(
            input,
            _ => new SessionCoreLoopRouteResult(new ModelRouteResolutionResult(
                "workbench",
                ModelRouteKind.Coding,
                "coding-provider",
                "coding-model",
                0,
                "openai_chat_completions"))));
    }
}
