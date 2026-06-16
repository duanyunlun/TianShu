using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class StageExecutorRunnerTests
{
    [Fact]
    public void StageExecutorRunRequestFactory_Create_ShouldBindRuntimeContext()
    {
        var runtimeContext = CreateRuntimeContext().WithExecutorDispatch(
            StageExecutorDispatcher.DefaultModelTurnImplementationId,
            StageExecutorDispatchKind.ModelTurn);

        var request = StageExecutorRunRequestFactory.Create(
            "thread-001",
            "turn-001",
            "请执行",
            "host-context",
            persistExtendedHistory: true,
            runtimeContext);

        Assert.Equal("thread-001", request.ThreadId);
        Assert.Equal("turn-001", request.TurnId);
        Assert.Equal("请执行", request.UserText);
        Assert.Equal("host-context", request.Context);
        Assert.True(request.PersistExtendedHistory);
        Assert.Same(runtimeContext, request.RuntimeContext);
    }

    [Fact]
    public void TurnExecutionRunRequestFactory_Create_ShouldBindDispatchContext()
    {
        var runtimeContext = CreateRuntimeContext().WithExecutorDispatch(
            StageExecutorDispatcher.DefaultModelTurnImplementationId,
            StageExecutorDispatchKind.ModelTurn);
        var dispatchContext = new TurnExecutionDispatchContext(runtimeContext);

        var request = TurnExecutionRunRequestFactory.Create(
            "thread-001",
            "turn-001",
            "请执行",
            "host-context",
            persistExtendedHistory: true,
            dispatchContext);

        Assert.Equal("thread-001", request.ThreadId);
        Assert.Equal("turn-001", request.TurnId);
        Assert.Equal("请执行", request.UserText);
        Assert.Equal("host-context", request.Context);
        Assert.True(request.PersistExtendedHistory);
        Assert.Same(dispatchContext, request.DispatchContext);
    }

    [Fact]
    public async Task RunAsync_UsesPinnedImplementationAndPassesRuntimeContext()
    {
        StageExecutorRunRequest<string>? observedRequest = null;
        var runner = new StageExecutorRunner<string>(
        [
            new StageExecutorImplementation<string>(
                StageExecutorDispatcher.DefaultModelTurnImplementationId,
                StageExecutorDispatchKind.ModelTurn,
                (request, _) =>
                {
                    observedRequest = request;
                    return Task.CompletedTask;
                }),
        ]);
        var runtimeContext = CreateRuntimeContext().WithExecutorDispatch(
            StageExecutorDispatcher.DefaultModelTurnImplementationId,
            StageExecutorDispatchKind.ModelTurn);
        var runRequest = new StageExecutorRunRequest<string>(
            "thread-001",
            "turn-001",
            "请执行",
            "host-context",
            persistExtendedHistory: true,
            runtimeContext);

        await runner.RunAsync(runRequest, CancellationToken.None);

        Assert.Same(runRequest, observedRequest);
        Assert.Equal("host-context", observedRequest!.Context);
        Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, observedRequest.RuntimeContext.ExecutorImplementationId);
    }

    [Fact]
    public async Task CreateDefaultModelTurnRunner_BindsDefaultModelTurnImplementation()
    {
        StageExecutorRunRequest<string>? observedRequest = null;
        var runner = StageExecutorRunnerFactory.CreateDefaultModelTurnRunner<string>((request, _) =>
        {
            observedRequest = request;
            return Task.CompletedTask;
        });
        var runtimeContext = CreateRuntimeContext().WithExecutorDispatch(
            StageExecutorDispatcher.DefaultModelTurnImplementationId,
            StageExecutorDispatchKind.ModelTurn);
        var runRequest = new StageExecutorRunRequest<string>(
            "thread-001",
            "turn-001",
            "请执行",
            "host-context",
            persistExtendedHistory: true,
            runtimeContext);

        await runner.RunAsync(runRequest, CancellationToken.None);

        Assert.Same(runRequest, observedRequest);
    }

    [Fact]
    public async Task TurnExecutionRunner_BindsDefaultModelTurnImplementation()
    {
        TurnExecutionRunRequest<string>? observedRequest = null;
        var runner = TurnExecutionRunnerFactory.CreateDefaultModelTurnRunner<string>((request, _) =>
        {
            observedRequest = request;
            return Task.CompletedTask;
        });
        var runtimeContext = CreateRuntimeContext().WithExecutorDispatch(
            StageExecutorDispatcher.DefaultModelTurnImplementationId,
            StageExecutorDispatchKind.ModelTurn);
        var dispatchContext = new TurnExecutionDispatchContext(runtimeContext);
        var runRequest = new TurnExecutionRunRequest<string>(
            "thread-001",
            "turn-001",
            "请执行",
            "host-context",
            persistExtendedHistory: true,
            dispatchContext);

        await runner.RunAsync(runRequest, CancellationToken.None);

        Assert.NotNull(observedRequest);
        Assert.Equal(runRequest.ThreadId, observedRequest!.ThreadId);
        Assert.Equal(runRequest.TurnId, observedRequest.TurnId);
        Assert.Equal(runRequest.UserText, observedRequest.UserText);
        Assert.Equal(runRequest.Context, observedRequest.Context);
        Assert.True(observedRequest.PersistExtendedHistory);
        Assert.Same(runtimeContext, observedRequest.DispatchContext.RuntimeContext);
    }

    [Fact]
    public async Task RunAsync_WhenImplementationIdMissing_Fails()
    {
        var runner = new StageExecutorRunner<string>(
        [
            new StageExecutorImplementation<string>(
                StageExecutorDispatcher.DefaultModelTurnImplementationId,
                StageExecutorDispatchKind.ModelTurn,
                (_, _) => Task.CompletedTask),
        ]);
        var runRequest = new StageExecutorRunRequest<string>(
            "thread-001",
            "turn-001",
            "请执行",
            "host-context",
            persistExtendedHistory: false,
            CreateRuntimeContext());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(runRequest, CancellationToken.None));
        Assert.Contains("implementation id", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_WhenImplementationIsUnknown_Fails()
    {
        var runner = new StageExecutorRunner<string>(
        [
            new StageExecutorImplementation<string>(
                StageExecutorDispatcher.DefaultModelTurnImplementationId,
                StageExecutorDispatchKind.ModelTurn,
                (_, _) => Task.CompletedTask),
        ]);
        var runRequest = new StageExecutorRunRequest<string>(
            "thread-001",
            "turn-001",
            "请执行",
            "host-context",
            persistExtendedHistory: false,
            CreateRuntimeContext().WithExecutorDispatch(
                "external.executor",
                StageExecutorDispatchKind.ExternalExecutor));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(runRequest, CancellationToken.None));
        Assert.Contains("external.executor", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenDispatchKindMismatchesImplementation_Fails()
    {
        var runner = new StageExecutorRunner<string>(
        [
            new StageExecutorImplementation<string>(
                StageExecutorDispatcher.DefaultModelTurnImplementationId,
                StageExecutorDispatchKind.ModelTurn,
                (_, _) => Task.CompletedTask),
        ]);
        var runRequest = new StageExecutorRunRequest<string>(
            "thread-001",
            "turn-001",
            "请执行",
            "host-context",
            persistExtendedHistory: false,
            CreateRuntimeContext().WithExecutorDispatch(
                StageExecutorDispatcher.DefaultModelTurnImplementationId,
                StageExecutorDispatchKind.ToolRuntime));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(runRequest, CancellationToken.None));
        Assert.Contains("dispatch kind", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static StageExecutorRuntimeContext CreateRuntimeContext()
    {
        var stage = BuiltInStageDefinitions.All.Single(static item => item.Id == BuiltInStageDefinitions.Coding);
        var sessionId = new SessionId("session-001");
        var threadId = new ThreadId("thread-001");
        var decision = new OrchestratorDecision(
            "decision-001",
            sessionId,
            threadId,
            stage.Id,
            [stage.Id],
            "requested-stage");
        var contextPackage = new StageContextPackage(
            "ctx-001",
            stage.Id,
            sessionId,
            threadId);
        var route = new ModelRouteResolutionResult(
            "workbench",
            ModelRouteKind.Coding,
            "provider-001",
            "model-001",
            0,
            "openai_chat_completions");
        var request = new StageExecutionRequest(
            "execution-001",
            stage,
            decision,
            contextPackage,
            route);
        return StageExecutorRuntimeContext.FromExecutionRequest(
            request,
            "route-correlation-001",
            DateTimeOffset.Parse("2026-06-09T00:00:00Z"));
    }
}
