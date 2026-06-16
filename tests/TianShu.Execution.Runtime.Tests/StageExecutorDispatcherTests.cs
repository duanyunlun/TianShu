using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class StageExecutorDispatcherTests
{
    [Fact]
    public void Dispatch_FromStageDefinitionsPinsDefaultModelTurnExecutor()
    {
        var request = CreateExecutionRequest(
            BuiltInStageDefinitions.Coding,
            ModelRouteKind.Coding);
        var runtimeContext = StageExecutorRuntimeContext.FromExecutionRequest(
            request,
            "route-correlation-001",
            DateTimeOffset.Parse("2026-06-09T00:00:00Z"));
        var dispatcher = StageExecutorDispatcher.FromStageDefinitions(BuiltInStageDefinitions.All);

        var plan = dispatcher.Dispatch(request, runtimeContext);

        Assert.Equal("coding", plan.Binding);
        Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, plan.ImplementationId);
        Assert.Equal(StageExecutorDispatchKind.ModelTurn, plan.DispatchKind);
        Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, plan.RuntimeContext.ExecutorImplementationId);
        Assert.Equal(StageExecutorDispatchKind.ModelTurn, plan.RuntimeContext.ExecutorDispatchKind);
        Assert.Equal("route-correlation-001", plan.RuntimeContext.ModelRouteDiagnosticsCorrelationId);
    }

    [Fact]
    public void DispatchPlanFactory_BindsFromStageDefinitions()
    {
        var request = CreateExecutionRequest(
            BuiltInStageDefinitions.Coding,
            ModelRouteKind.Coding);
        var runtimeContext = StageExecutorRuntimeContext.FromExecutionRequest(
            request,
            "route-correlation-001",
            DateTimeOffset.Parse("2026-06-09T00:00:00Z"));

        var plan = StageExecutorDispatchPlanFactory.Bind(
            BuiltInStageDefinitions.All,
            request,
            runtimeContext);

        Assert.Equal("coding", plan.Binding);
        Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, plan.ImplementationId);
        Assert.Equal(StageExecutorDispatchKind.ModelTurn, plan.DispatchKind);
        Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, plan.RuntimeContext.ExecutorImplementationId);
        Assert.Equal(StageExecutorDispatchKind.ModelTurn, plan.RuntimeContext.ExecutorDispatchKind);
    }

    [Fact]
    public void TurnExecutionDispatchContext_FromDispatchPlanProjectsGenericDispatchFields()
    {
        var request = CreateExecutionRequest(
            BuiltInStageDefinitions.Coding,
            ModelRouteKind.Coding);
        var runtimeContext = StageExecutorRuntimeContext.FromExecutionRequest(
            request,
            "route-correlation-001",
            DateTimeOffset.Parse("2026-06-09T00:00:00Z"));
        var plan = StageExecutorDispatchPlanFactory.Bind(
            BuiltInStageDefinitions.All,
            request,
            runtimeContext);

        var context = TurnExecutionDispatchContext.FromDispatchPlan(plan);

        Assert.Same(plan.RuntimeContext, context.RuntimeContext);
        Assert.Equal("coding", context.StageId);
        Assert.Equal("decision-coding", context.DecisionId);
        Assert.Equal("ctx-coding", context.ContextPackageId);
        Assert.Equal("execution-coding", context.ExecutionId);
        Assert.Equal("coding", context.Binding);
        Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, context.ImplementationId);
        Assert.Equal(StageExecutorDispatchKind.ModelTurn.ToString(), context.DispatchKind);
        Assert.Equal(DateTimeOffset.Parse("2026-06-09T00:00:00Z"), context.StartedAt);
    }

    [Fact]
    public void TurnExecutionDispatchContextFactory_BindsExecutionEntry()
    {
        var request = CreateExecutionRequest(
            BuiltInStageDefinitions.Coding,
            ModelRouteKind.Coding);
        var runtimeContext = StageExecutorRuntimeContext.FromExecutionRequest(
            request,
            "route-correlation-001",
            DateTimeOffset.Parse("2026-06-09T00:00:00Z"));

        var context = TurnExecutionDispatchContextFactory.FromExecutionEntry(
            BuiltInStageDefinitions.All,
            request,
            runtimeContext);

        Assert.Equal(runtimeContext.ExecutionId, context.RuntimeContext.ExecutionId);
        Assert.Equal(runtimeContext.ModelRouteDiagnosticsCorrelationId, context.RuntimeContext.ModelRouteDiagnosticsCorrelationId);
        Assert.Equal("coding", context.Binding);
        Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, context.ImplementationId);
        Assert.Equal(StageExecutorDispatchKind.ModelTurn.ToString(), context.DispatchKind);
        Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, context.RuntimeContext.ExecutorImplementationId);
        Assert.Equal(StageExecutorDispatchKind.ModelTurn, context.RuntimeContext.ExecutorDispatchKind);
    }

    [Fact]
    public void Dispatch_WhenBindingIsNotRegistered_Fails()
    {
        var stage = new StageDefinition(
            "triage",
            "Triage",
            1,
            new ModelRouteKind("triage"),
            executorBinding: "triage.executor");
        var request = CreateExecutionRequest(stage, new ModelRouteKind("triage"));
        var runtimeContext = StageExecutorRuntimeContext.FromExecutionRequest(request);
        var dispatcher = new StageExecutorDispatcher(
        [
            new StageExecutorBindingDescriptor("coding", "apphost.turn-runtime"),
        ]);

        var error = Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(request, runtimeContext));
        Assert.Contains("triage.executor", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_WhenBindingIsNotAuthorizedForStage_Fails()
    {
        var request = CreateExecutionRequest(
            BuiltInStageDefinitions.Review,
            ModelRouteKind.Review);
        var runtimeContext = StageExecutorRuntimeContext.FromExecutionRequest(request);
        var dispatcher = new StageExecutorDispatcher(
        [
            new StageExecutorBindingDescriptor(
                "review",
                "apphost.turn-runtime",
                StageExecutorDispatchKind.ModelTurn,
                [BuiltInStageDefinitions.Coding]),
        ]);

        var error = Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(request, runtimeContext));
        Assert.Contains("review", error.Message, StringComparison.Ordinal);
        Assert.Contains(BuiltInStageDefinitions.Review, error.Message, StringComparison.Ordinal);
    }

    private static StageExecutionRequest CreateExecutionRequest(
        string stageId,
        ModelRouteKind routeKind)
    {
        var stage = BuiltInStageDefinitions.All.Single(stage => stage.Id == stageId);
        return CreateExecutionRequest(stage, routeKind);
    }

    private static StageExecutionRequest CreateExecutionRequest(
        StageDefinition stage,
        ModelRouteKind routeKind)
    {
        var sessionId = new SessionId("session-001");
        var threadId = new ThreadId("thread-001");
        var decision = new OrchestratorDecision(
            $"decision-{stage.Id}",
            sessionId,
            threadId,
            stage.Id,
            [stage.Id],
            "requested-stage");
        var contextPackage = new StageContextPackage(
            $"ctx-{stage.Id}",
            stage.Id,
            sessionId,
            threadId);
        var route = new ModelRouteResolutionResult(
            "workbench",
            routeKind,
            "provider-001",
            "model-001",
            0,
            "openai_chat_completions");

        return new StageExecutionRequest(
            $"execution-{stage.Id}",
            stage,
            decision,
            contextPackage,
            route);
    }
}
