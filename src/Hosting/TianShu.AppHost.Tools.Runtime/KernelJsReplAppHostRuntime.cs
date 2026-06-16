using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelJsReplAppHostContext(
    string? Cwd,
    string? PersistedConfigText,
    InteractionEnvelopeRef? InteractionEnvelope = null);

internal delegate Task<KernelToolResult> KernelNestedJsReplToolExecutor(
    string toolName,
    string nestedItemId,
    JsonElement arguments,
    CancellationToken cancellationToken);

internal sealed class KernelJsReplAppHostRuntime
{
    private readonly KernelRolloutRecorder rolloutRecorder;
    private readonly ConcurrentDictionary<string, KernelJsReplManager> jsReplManagers = new(StringComparer.Ordinal);

    public KernelJsReplAppHostRuntime(KernelRolloutRecorder rolloutRecorder)
    {
        this.rolloutRecorder = rolloutRecorder;
    }

    public async Task<KernelJsReplExecutionResult> ExecuteAsync(
        string threadId,
        string turnId,
        KernelJsReplAppHostContext context,
        KernelJsReplExecutionRequest request,
        KernelNestedJsReplToolExecutor executeToolCallAsync,
        CancellationToken cancellationToken)
    {
        var executionRequest = KernelExecutionEnvelopeFactory.CreateJsReplRequest(threadId, turnId, context.Cwd, request, context.InteractionEnvelope);
        await rolloutRecorder.AppendExecutionRequestAsync(threadId, executionRequest, cancellationToken).ConfigureAwait(false);
        await rolloutRecorder.AppendExecutionEventAsync(
            threadId,
            KernelExecutionEnvelopeFactory.CreateStartedEvent(executionRequest, "js repl execution started"),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var manager = GetOrCreateManager(turnId, context);
            var result = await manager.ExecuteAsync(
                request,
                (toolCall, innerCancellationToken) => ExecuteNestedToolCallAsync(
                    turnId,
                    toolCall,
                    executeToolCallAsync,
                    innerCancellationToken),
                cancellationToken).ConfigureAwait(false);
            await rolloutRecorder.AppendExecutionEventAsync(
                threadId,
                result.Success
                    ? KernelExecutionEnvelopeFactory.CreateCompletedEvent(
                        executionRequest,
                        "js repl execution completed",
                        KernelExecutionEnvelopeFactory.CreateJsReplData(result))
                    : KernelExecutionEnvelopeFactory.CreateFailedEvent(
                        executionRequest,
                        "js repl execution failed",
                        KernelExecutionEnvelopeFactory.CreateJsReplData(result)),
                cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await rolloutRecorder.AppendExecutionEventAsync(
                threadId,
                KernelExecutionEnvelopeFactory.CreateFailedEvent(
                    executionRequest,
                    ex.Message,
                    KernelExecutionEnvelopeFactory.CreateFailureData(ex.Message)),
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task ResetAsync(
        string threadId,
        string turnId,
        KernelJsReplAppHostContext context,
        CancellationToken cancellationToken)
    {
        var executionRequest = KernelExecutionEnvelopeFactory.CreateJsReplResetRequest(threadId, turnId, context.Cwd, context.InteractionEnvelope);
        await rolloutRecorder.AppendExecutionRequestAsync(threadId, executionRequest, cancellationToken).ConfigureAwait(false);
        await rolloutRecorder.AppendExecutionEventAsync(
            threadId,
            KernelExecutionEnvelopeFactory.CreateStartedEvent(executionRequest, "js repl reset started"),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var manager = GetOrCreateManager(turnId, context);
            await manager.ResetAsync(cancellationToken).ConfigureAwait(false);
            await rolloutRecorder.AppendExecutionEventAsync(
                threadId,
                KernelExecutionEnvelopeFactory.CreateCompletedEvent(executionRequest, "js repl reset completed"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await rolloutRecorder.AppendExecutionEventAsync(
                threadId,
                KernelExecutionEnvelopeFactory.CreateFailedEvent(
                    executionRequest,
                    ex.Message,
                    KernelExecutionEnvelopeFactory.CreateFailureData(ex.Message)),
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeManagerAsync(string turnId)
    {
        if (!jsReplManagers.TryRemove(turnId, out var manager))
        {
            return;
        }

        await manager.DisposeAsync().ConfigureAwait(false);
    }

    private KernelJsReplManager GetOrCreateManager(string turnId, KernelJsReplAppHostContext context)
    {
        return jsReplManagers.GetOrAdd(turnId, static (_, state) =>
        {
            var options = KernelJsReplRuntimeHelpers.ResolveJsReplOptions(
                state.Cwd,
                state.PersistedConfigText,
                Environment.GetEnvironmentVariable("TIANSHU_JS_REPL_NODE_PATH"),
                Environment.GetEnvironmentVariable("TIANSHU_JS_REPL_NODE_MODULE_DIRS"));
            return new KernelJsReplManager(options);
        }, context);
    }

    private async Task<KernelJsReplHostToolResponse> ExecuteNestedToolCallAsync(
        string turnId,
        KernelJsReplToolCall toolCall,
        KernelNestedJsReplToolExecutor executeToolCallAsync,
        CancellationToken cancellationToken)
    {
        if (string.Equals(toolCall.ToolName, "js_repl", StringComparison.Ordinal)
            || string.Equals(toolCall.ToolName, "js_repl_reset", StringComparison.Ordinal))
        {
            return new KernelJsReplHostToolResponse(false, null, "js_repl cannot invoke itself");
        }

        var arguments = toolCall.Arguments.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => JsonSerializer.SerializeToElement(new { }),
            _ => toolCall.Arguments,
        };
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return new KernelJsReplHostToolResponse(false, null, $"js_repl tianshu.tool expects JSON object arguments for {toolCall.ToolName}");
        }

        var nestedItemId = KernelJsReplRuntimeHelpers.BuildJsReplNestedToolCallItemId(turnId, toolCall.RequestId, toolCall.ToolName);
        var result = await executeToolCallAsync(
            toolCall.ToolName,
            nestedItemId,
            arguments,
            cancellationToken).ConfigureAwait(false);

        var envelopePayload = ToolUseFollowUpItemProjector.BuildFunctionCallOutputItem(
                toolCall.RequestId,
                isCustomToolCall: false,
                output: result.BuildFunctionCallOutputPayload())
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);
        envelopePayload["success"] = result.Success;
        var envelope = JsonSerializer.SerializeToElement(envelopePayload);

        return new KernelJsReplHostToolResponse(result.Success, envelope, result.Success ? null : result.OutputText);
    }
}
