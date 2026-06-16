using TianShu.Contracts.Execution;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Primitives;
using TianShu.AppHost.Tools;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelExecutionEnvelopeFactoryTests
{
    [Fact]
    public void CreateCodeModeHandle_ShouldCaptureRunningCellId()
    {
        var request = KernelExecutionEnvelopeFactory.CreateCodeModeExecuteRequest(
            "thread_exec_handle_001",
            "turn_exec_handle_001",
            "D:/Repo",
            new KernelCodeModeExecutionRequest("console.log(1);", 1000, 2048));
        var result = new KernelCodeModeOperationResult(
            true,
            "running",
            [
                new KernelToolOutputContentItem(
                    "input_text",
                    "Script running with cell ID cell_123. Poll with exec_wait to continue."),
            ]);

        var handle = KernelExecutionEnvelopeFactory.CreateCodeModeHandle(request, result);
        var data = Assert.IsType<Dictionary<string, object?>>(KernelExecutionEnvelopeFactory.CreateCodeModeData(handle, result).ToPlainObject());
        var handlePayload = Assert.IsType<Dictionary<string, object?>>(data["handle"]);

        Assert.Equal(request.ExecutionId, handle.ExecutionId);
        Assert.Equal("cell_123", handle.CurrentAttempt.NativeHandleId);
        Assert.Equal("running", data["status"]);
        Assert.Equal("cell_123", handlePayload["nativeHandleId"]);
        Assert.Equal("CodeMode", handlePayload["kind"]);
    }

    [Fact]
    public void CreateJsReplResetRequest_ShouldUseResetAction()
    {
        var request = KernelExecutionEnvelopeFactory.CreateJsReplResetRequest(
            "thread_js_repl_reset_001",
            "turn_js_repl_reset_001",
            "D:/Repo");

        Assert.Equal(ExecutionKind.EnvironmentAction, request.Kind);
        Assert.Equal("reset", request.Action);
        Assert.Equal("thread_js_repl_reset_001", request.Context.ThreadId?.ToString());
        Assert.Equal("turn_js_repl_reset_001", request.Context.TurnId?.ToString());
        Assert.Equal("D:/Repo", request.Context.WorkingDirectory);
        Assert.NotNull(request.Input);
        Assert.Equal(StructuredValueKind.Object, request.Input!.Kind);
        Assert.Empty(request.Input.Properties);
        Assert.True(request.Context.Metadata.TryGetValue("executionKind", out var executionKind));
        Assert.Equal("JsRepl", executionKind.StringValue);
    }

    [Fact]
    public void CreateCodeModeExecuteRequest_WhenInteractionEnvelopeProvided_ShouldReuseRealEnvelopeRef()
    {
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(1_746_100_000_000);
        var interactionEnvelope = new InteractionEnvelopeRef(
            new InteractionEnvelopeId("interaction_exec_context_001"),
            InteractionSourceKind.Host,
            "cli",
            createdAt);

        var request = KernelExecutionEnvelopeFactory.CreateCodeModeExecuteRequest(
            "thread_exec_envelope_001",
            "turn_exec_envelope_001",
            "D:/Repo",
            new KernelCodeModeExecutionRequest("console.log(1);", 1000, 2048),
            interactionEnvelope);

        Assert.Equal("interaction_exec_context_001", request.Context.InteractionEnvelope.Id.Value);
        Assert.Equal(InteractionSourceKind.Host, request.Context.InteractionEnvelope.SourceKind);
        Assert.Equal("cli", request.Context.InteractionEnvelope.Surface);
        Assert.Equal(createdAt, request.Context.InteractionEnvelope.CreatedAt);
    }
}
