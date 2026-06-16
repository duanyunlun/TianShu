using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class StageExecutorCheckpointBuilderTests
{
    [Fact]
    public void FromExecutionRequest_PreservesStageExecutorRuntimeContext()
    {
        var request = CreateExecutionRequest();
        var startedAt = DateTimeOffset.Parse("2026-06-09T00:00:00Z");

        var context = StageExecutorRuntimeContext.FromExecutionRequest(
            request,
            "model-route-correlation-001",
            startedAt)
            .WithExecutorDispatch(
                StageExecutorDispatcher.DefaultModelTurnImplementationId,
                StageExecutorDispatchKind.ModelTurn);

        Assert.Equal("execution-001", context.ExecutionId);
        Assert.Equal(BuiltInStageDefinitions.Coding, context.StageId);
        Assert.Equal("coding", context.ExecutorBinding);
        Assert.Equal("decision-001", context.DecisionId);
        Assert.Equal("ctx-001", context.ContextPackageId);
        Assert.Equal("workbench", context.ModelRouteSetId);
        Assert.Equal(ModelRouteKind.Coding, context.ModelRouteKind);
        Assert.Equal("model-route-correlation-001", context.ModelRouteDiagnosticsCorrelationId);
        Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, context.ExecutorImplementationId);
        Assert.Equal(StageExecutorDispatchKind.ModelTurn, context.ExecutorDispatchKind);
        Assert.Equal(startedAt, context.StartedAt);
    }

    [Fact]
    public void Complete_CreatesCheckpointWithExecutionDiagnosticsAndSummary()
    {
        var request = CreateExecutionRequest();
        var startedAt = DateTimeOffset.Parse("2026-06-09T00:00:00Z");
        var completedAt = startedAt.AddMinutes(1);
        var context = StageExecutorRuntimeContext.FromExecutionRequest(
            request,
            "model-route-correlation-001",
            startedAt)
            .WithExecutorDispatch(
                StageExecutorDispatcher.DefaultModelTurnImplementationId,
                StageExecutorDispatchKind.ModelTurn);
        var completion = new StageExecutorTurnCompletion(
            "turn-001",
            "completed",
            StageExecutionState.Completed,
            completedAt,
            effectiveUserText: "请完成实现",
            finalAssistantText: "实现完成");

        var checkpoint = StageExecutorCheckpointBuilder.Instance.Complete(context, completion);

        Assert.Equal("checkpoint-decision-001-coding", checkpoint.CheckpointId);
        Assert.Equal(BuiltInStageDefinitions.Coding, checkpoint.StageId);
        Assert.Equal(StageExecutionState.Completed, checkpoint.State);
        Assert.Equal(startedAt, checkpoint.StartedAt);
        Assert.Equal(completedAt, checkpoint.CompletedAt);
        Assert.Equal("实现完成", checkpoint.Summary);
        Assert.Equal("workbench", checkpoint.ModelRouteSetId);
        Assert.Equal(ModelRouteKind.Coding, checkpoint.ModelRouteKind);
        Assert.Equal("turn-001", checkpoint.Diagnostics?.Properties["turnId"].StringValue);
        Assert.Equal("completed", checkpoint.Diagnostics?.Properties["status"].StringValue);
        Assert.Equal("decision-001", checkpoint.Diagnostics?.Properties["decisionId"].StringValue);
        Assert.Equal("ctx-001", checkpoint.Diagnostics?.Properties["contextPackageId"].StringValue);
        Assert.Equal("execution-001", checkpoint.Diagnostics?.Properties["executionRequestId"].StringValue);
        Assert.Equal("coding", checkpoint.Diagnostics?.Properties["executorBinding"].StringValue);
        Assert.Equal(
            StageExecutorDispatcher.DefaultModelTurnImplementationId,
            checkpoint.Diagnostics?.Properties["executorImplementationId"].StringValue);
        Assert.Equal(
            StageExecutorDispatchKind.ModelTurn.ToString(),
            checkpoint.Diagnostics?.Properties["executorDispatchKind"].StringValue);
        Assert.Equal(
            "model-route-correlation-001",
            checkpoint.Diagnostics?.Properties["modelRouteDiagnosticsCorrelationId"].StringValue);
    }

    [Fact]
    public void Complete_ClampsCompletedAtAndUsesErrorSummaryForFailedTurn()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-09T00:00:00Z");
        var context = new StageExecutorRuntimeContext(
            "execution-002",
            BuiltInStageDefinitions.Review,
            "review",
            startedAt,
            decisionId: "decision-002",
            modelRouteSetId: "workbench",
            modelRouteKind: ModelRouteKind.Review);
        var completion = new StageExecutorTurnCompletion(
            "turn-002",
            "failed",
            StageExecutionState.Failed,
            startedAt.AddMinutes(-1),
            effectiveUserText: "请审查",
            errorMessage: "模型调用失败",
            errorDetails: "500");

        var checkpoint = StageExecutorCheckpointBuilder.Instance.Complete(context, completion);

        Assert.Equal(startedAt, checkpoint.CompletedAt);
        Assert.Equal("模型调用失败", checkpoint.Summary);
        Assert.Equal("500", checkpoint.Diagnostics?.Properties["errorDetails"].StringValue);
    }

    [Fact]
    public void CompleteTerminalTurn_ProjectsCompletionInsideExecutionRuntime()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-09T00:00:00Z");
        var context = new StageExecutorRuntimeContext(
            "execution-002",
            BuiltInStageDefinitions.Review,
            "review",
            startedAt,
            decisionId: "decision-002",
            modelRouteSetId: "workbench",
            modelRouteKind: ModelRouteKind.Review);

        var checkpoint = StageExecutorCheckpointBuilder.Instance.CompleteTerminalTurn(
            context,
            "turn-002",
            "failed",
            startedAt.AddMinutes(-1),
            effectiveUserText: "请审查",
            errorMessage: "模型调用失败",
            errorDetails: "500");

        Assert.Equal(startedAt, checkpoint.CompletedAt);
        Assert.Equal(StageExecutionState.Failed, checkpoint.State);
        Assert.Equal("模型调用失败", checkpoint.Summary);
        Assert.Equal("turn-002", checkpoint.Diagnostics?.Properties["turnId"].StringValue);
        Assert.Equal("failed", checkpoint.Diagnostics?.Properties["status"].StringValue);
        Assert.Equal("500", checkpoint.Diagnostics?.Properties["errorDetails"].StringValue);
    }

    [Fact]
    public void TurnTerminalProjectionCheckpointBuilder_UsesGenericDispatchContext()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-09T00:00:00Z");
        var runtimeContext = new StageExecutorRuntimeContext(
            "execution-004",
            BuiltInStageDefinitions.Review,
            "review",
            startedAt,
            decisionId: "decision-004",
            modelRouteSetId: "workbench",
            modelRouteKind: ModelRouteKind.Review)
            .WithExecutorDispatch(
                StageExecutorDispatcher.DefaultModelTurnImplementationId,
                StageExecutorDispatchKind.ModelTurn);
        var dispatchContext = new TurnExecutionDispatchContext(runtimeContext);

        var checkpoint = TurnTerminalProjectionCheckpointBuilder.Instance.CompleteTerminalTurn(
            dispatchContext,
            "turn-004",
            "completed",
            startedAt.AddMinutes(1),
            effectiveUserText: "请审查",
            finalAssistantText: "审查完成");

        Assert.Equal("checkpoint-decision-004-review", checkpoint.CheckpointId);
        Assert.Equal(BuiltInStageDefinitions.Review, checkpoint.StageId);
        Assert.Equal(StageExecutionState.Completed, checkpoint.State);
        Assert.Equal("审查完成", checkpoint.Summary);
        Assert.Equal("execution-004", checkpoint.Diagnostics?.Properties["executionRequestId"].StringValue);
        Assert.Equal("review", checkpoint.Diagnostics?.Properties["executorBinding"].StringValue);
        Assert.Equal(
            StageExecutorDispatcher.DefaultModelTurnImplementationId,
            checkpoint.Diagnostics?.Properties["executorImplementationId"].StringValue);
        Assert.Equal(
            StageExecutorDispatchKind.ModelTurn.ToString(),
            checkpoint.Diagnostics?.Properties["executorDispatchKind"].StringValue);
    }

    [Fact]
    public void Complete_PreservesExplicitOutputAndArtifactRefs()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-09T00:00:00Z");
        var context = new StageExecutorRuntimeContext(
            "execution-003",
            BuiltInStageDefinitions.Coding,
            "coding",
            startedAt,
            decisionId: "decision-003");
        var output = StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["changedFiles"] = new[] { "src/Foo.cs" },
        });
        var completion = new StageExecutorTurnCompletion(
            "turn-003",
            "completed",
            StageExecutionState.Completed,
            startedAt.AddMinutes(2),
            finalAssistantText: "实现完成",
            output: output,
            artifactRefs:
            [
                new ArtifactRef(new ArtifactId("artifact-003"), "diff.patch", "patch"),
            ]);

        var checkpoint = StageExecutorCheckpointBuilder.Instance.Complete(context, completion);

        Assert.Same(output, checkpoint.Output);
        var artifact = Assert.Single(checkpoint.ArtifactRefs);
        Assert.Equal("artifact-003", artifact.Id.Value);
        Assert.Equal("diff.patch", artifact.Name);
        Assert.Equal("patch", artifact.Kind);
    }

    [Theory]
    [InlineData("completed", StageExecutionState.Completed)]
    [InlineData("failed", StageExecutionState.Failed)]
    [InlineData("interrupted", StageExecutionState.Blocked)]
    [InlineData("unknown", StageExecutionState.Failed)]
    public void ResolveState_MapsTurnStatusToStageExecutionState(
        string status,
        StageExecutionState expected)
    {
        Assert.Equal(expected, StageExecutorTurnCompletion.ResolveState(status));
    }

    private static StageExecutionRequest CreateExecutionRequest()
    {
        var sessionId = new SessionId("session-001");
        var threadId = new ThreadId("thread-001");
        var stage = BuiltInStageDefinitions.All.Single(static item => item.Id == BuiltInStageDefinitions.Coding);
        var decision = new OrchestratorDecision(
            "decision-001",
            sessionId,
            threadId,
            BuiltInStageDefinitions.Coding,
            [BuiltInStageDefinitions.Coding],
            "requested-stage");
        var contextPackage = new StageContextPackage(
            "ctx-001",
            BuiltInStageDefinitions.Coding,
            sessionId,
            threadId);
        var route = new ModelRouteResolutionResult(
            "workbench",
            ModelRouteKind.Coding,
            "coding-provider",
            "coding-model",
            0,
            "openai_chat_completions");

        return new StageExecutionRequest("execution-001", stage, decision, contextPackage, route);
    }
}
