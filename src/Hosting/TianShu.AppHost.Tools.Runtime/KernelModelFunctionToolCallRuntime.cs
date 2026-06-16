using System.Diagnostics;
using System.Text.Json;
using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal delegate Task<KernelToolResult> KernelModelFunctionToolExecutor(
    string threadId,
    string turnId,
    string itemId,
    string toolName,
    JsonElement arguments,
    TurnRequestContext context,
    KernelReadinessFlag? toolCallGate,
    CancellationToken cancellationToken,
    string? customInput = null,
    bool isCustomToolCall = false,
    string? externalCallId = null);

internal delegate Task KernelModelFunctionToolTurnLogWriter(
    string threadId,
    string turnId,
    string phase,
    string status,
    string? summary,
    object payload,
    CancellationToken cancellationToken);

/// <summary>
/// Provider 模型工具调用运行时，负责把模型 function/tool call 转换为工具执行和 follow-up 输入项。
/// Runtime that converts provider model function/tool calls into tool execution and follow-up input items.
/// </summary>
internal sealed class KernelModelFunctionToolCallRuntime
{
    private readonly KernelModelFunctionToolTurnLogWriter persistTurnLogAsync;
    private readonly KernelModelFunctionToolExecutor executeToolCallAsync;

    public KernelModelFunctionToolCallRuntime(
        KernelModelFunctionToolTurnLogWriter persistTurnLogAsync,
        KernelModelFunctionToolExecutor executeToolCallAsync)
    {
        this.persistTurnLogAsync = persistTurnLogAsync ?? throw new ArgumentNullException(nameof(persistTurnLogAsync));
        this.executeToolCallAsync = executeToolCallAsync ?? throw new ArgumentNullException(nameof(executeToolCallAsync));
    }

    public async Task<object> ExecuteAsync(
        ModelFunctionCall call,
        TurnOperationState state,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        var resolvedToolName = ResolveModelFunctionToolName(call, context.DynamicTools);

        await persistTurnLogAsync(
            state.ThreadId,
            state.TurnId,
            phase: "turn.function_call.received",
            status: "inProgress",
            summary: $"{call.Name} -> {resolvedToolName}",
            payload: new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                callId = call.CallId,
                originalToolName = call.Name,
                resolvedToolName,
                call.Namespace,
                call.IsCustom,
                call.IsToolSearch,
                rawArguments = call.Arguments,
                rawInput = call.Input,
            },
            cancellationToken).ConfigureAwait(false);

        var result = call.IsCustom
            ? await ExecuteCustomCallAsync(call, state, context, resolvedToolName, cancellationToken).ConfigureAwait(false)
            : await ExecuteFunctionCallAsync(call, state, context, resolvedToolName, cancellationToken).ConfigureAwait(false);

        return BuildFollowUpItem(call, result);
    }

    public async Task<object> ExecuteWithParallelLockAsync(
        ModelFunctionCall call,
        bool supportsParallelToolCalls,
        KernelAsyncReadWriteLock parallelExecution,
        TurnOperationState state,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var _ = supportsParallelToolCalls
                ? await parallelExecution.AcquireReadAsync(cancellationToken).ConfigureAwait(false)
                : await parallelExecution.AcquireWriteAsync(cancellationToken).ConfigureAwait(false);

            return await ExecuteAsync(call, state, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var secs = (double)(Stopwatch.GetTimestamp() - started) / Stopwatch.Frequency;
            if (call.IsToolSearch)
            {
                return ToolUseFollowUpItemProjector.BuildToolSearchOutputItem(call.CallId, Array.Empty<JsonElement>());
            }

            return ToolUseFollowUpItemProjector.BuildCancelledFunctionCallOutputItem(
                call.CallId,
                call.Name,
                call.IsCustom,
                secs);
        }
    }

    public static string ResolveModelFunctionToolName(
        ModelFunctionCall call,
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools)
    {
        if (call.IsToolSearch)
        {
            return KernelToolDiscoveryToolNames.Search;
        }

        return call.IsCustom
            ? call.Name
            : KernelDynamicToolResolver.ResolveFullToolName(dynamicTools, call.Name, call.Namespace) ?? call.Name;
    }

    private async Task<KernelToolResult> ExecuteCustomCallAsync(
        ModelFunctionCall call,
        TurnOperationState state,
        TurnRequestContext context,
        string resolvedToolName,
        CancellationToken cancellationToken)
    {
        await persistTurnLogAsync(
            state.ThreadId,
            state.TurnId,
            phase: "turn.function_call.parsed",
            status: "completed",
            summary: resolvedToolName,
            payload: new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                callId = call.CallId,
                originalToolName = call.Name,
                resolvedToolName,
                input = call.Input,
                isCustom = true,
            },
            cancellationToken).ConfigureAwait(false);

        return await executeToolCallAsync(
            state.ThreadId,
            state.TurnId,
            ToolUseFollowUpItemProjector.BuildModelToolCallItemId(call.CallId, call.Name),
            call.Name,
            JsonSerializer.SerializeToElement(new { }),
            context,
            state.ToolCallGate,
            cancellationToken,
            customInput: call.Input ?? string.Empty,
            isCustomToolCall: true,
            externalCallId: call.CallId).ConfigureAwait(false);
    }

    private async Task<KernelToolResult> ExecuteFunctionCallAsync(
        ModelFunctionCall call,
        TurnOperationState state,
        TurnRequestContext context,
        string resolvedToolName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(call.Arguments ?? "{}");
            var argsElement = doc.RootElement.Clone();
            if (argsElement.ValueKind != JsonValueKind.Object)
            {
                await persistTurnLogAsync(
                    state.ThreadId,
                    state.TurnId,
                    phase: "turn.function_call.parsed",
                    status: "failed",
                    summary: "function arguments must be a JSON object",
                    payload: new
                    {
                        threadId = state.ThreadId,
                        turnId = state.TurnId,
                        callId = call.CallId,
                        originalToolName = call.Name,
                        resolvedToolName,
                        rawArguments = call.Arguments,
                        error = "failed to parse function arguments: expected JSON object",
                    },
                    cancellationToken).ConfigureAwait(false);

                return new KernelToolResult(false, "failed to parse function arguments: expected JSON object");
            }

            await persistTurnLogAsync(
                state.ThreadId,
                state.TurnId,
                phase: "turn.function_call.parsed",
                status: "completed",
                summary: $"{call.Name} -> {resolvedToolName}",
                payload: new
                {
                    threadId = state.ThreadId,
                    turnId = state.TurnId,
                    callId = call.CallId,
                    originalToolName = call.Name,
                    resolvedToolName,
                    parsedArguments = argsElement,
                    call.Namespace,
                    call.IsToolSearch,
                },
                cancellationToken).ConfigureAwait(false);

            return await executeToolCallAsync(
                state.ThreadId,
                state.TurnId,
                ToolUseFollowUpItemProjector.BuildModelToolCallItemId(call.CallId, resolvedToolName),
                resolvedToolName,
                argsElement,
                context,
                state.ToolCallGate,
                cancellationToken,
                externalCallId: call.CallId).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            await persistTurnLogAsync(
                state.ThreadId,
                state.TurnId,
                phase: "turn.function_call.parsed",
                status: "failed",
                summary: ex.Message,
                payload: new
                {
                    threadId = state.ThreadId,
                    turnId = state.TurnId,
                    callId = call.CallId,
                    originalToolName = call.Name,
                    resolvedToolName,
                    rawArguments = call.Arguments,
                    error = ex.Message,
                },
                cancellationToken).ConfigureAwait(false);

            return new KernelToolResult(false, $"failed to parse function arguments: {ex.Message}");
        }
    }

    private static object BuildFollowUpItem(ModelFunctionCall call, KernelToolResult result)
    {
        if (call.IsToolSearch)
        {
            return ToolUseFollowUpItemProjector.BuildToolSearchOutputItem(
                call.CallId,
                ToolUseFollowUpItemProjector.ExtractToolSearchOutputTools(
                    result.Success,
                    result.OutputText,
                    result.StructuredOutput));
        }

        return ToolUseFollowUpItemProjector.BuildFunctionCallOutputItem(
            call.CallId,
            call.IsCustom,
            result.BuildFunctionCallOutputPayload());
    }
}
