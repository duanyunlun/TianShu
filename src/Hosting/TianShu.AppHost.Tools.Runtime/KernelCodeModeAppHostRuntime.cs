using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Interactions;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelCodeModeAppHostContext(
    string? Cwd,
    string? PersistedConfigText,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools,
    KernelResponsesNativeToolOptions NativeToolOptions,
    InteractionEnvelopeRef? InteractionEnvelope = null);

internal delegate Task<KernelToolResult> KernelNestedCodeModeToolExecutor(
    string toolName,
    string nestedItemId,
    JsonElement arguments,
    string? customInput,
    bool isCustomToolCall,
    CancellationToken cancellationToken);

internal sealed class KernelCodeModeAppHostRuntime : IAsyncDisposable
{
    private readonly KernelRolloutRecorder rolloutRecorder;
    private readonly KernelToolRegistry toolRegistry;
    private readonly ConcurrentDictionary<string, KernelCodeModeManager> codeModeManagers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> codeModeTurnsByThread = new(StringComparer.Ordinal);

    public KernelCodeModeAppHostRuntime(
        KernelRolloutRecorder rolloutRecorder,
        KernelToolRegistry toolRegistry)
    {
        this.rolloutRecorder = rolloutRecorder;
        this.toolRegistry = toolRegistry;
    }

    public async Task<KernelCodeModeOperationResult> ExecuteAsync(
        string threadId,
        string turnId,
        KernelCodeModeAppHostContext context,
        KernelCodeModeExecutionRequest request,
        KernelNestedCodeModeToolExecutor executeToolCallAsync,
        CancellationToken cancellationToken)
    {
        var executionRequest = KernelExecutionEnvelopeFactory.CreateCodeModeExecuteRequest(threadId, turnId, context.Cwd, request, context.InteractionEnvelope);
        await rolloutRecorder.AppendExecutionRequestAsync(threadId, executionRequest, cancellationToken).ConfigureAwait(false);
        await rolloutRecorder.AppendExecutionEventAsync(
            threadId,
            KernelExecutionEnvelopeFactory.CreateStartedEvent(executionRequest, "code mode execution started"),
            cancellationToken).ConfigureAwait(false);

        if (!context.NativeToolOptions.CodeModeEnabled)
        {
            var unavailableResult = new KernelCodeModeOperationResult(false, "exec is unavailable", Array.Empty<KernelToolOutputContentItem>());
            await rolloutRecorder.AppendExecutionEventAsync(
                threadId,
                KernelExecutionEnvelopeFactory.CreateFailedEvent(
                    executionRequest,
                    "code mode execution failed",
                    KernelExecutionEnvelopeFactory.CreateCodeModeData(handle: null, unavailableResult)),
                cancellationToken).ConfigureAwait(false);
            return unavailableResult;
        }

        try
        {
            var manager = GetOrCreateManager(threadId, context);
            codeModeTurnsByThread[turnId] = threadId;
            manager.ActivateTurn(
                turnId,
                (toolCall, innerCancellationToken) => ExecuteNestedToolCallAsync(
                    turnId,
                    context,
                    toolCall,
                    executeToolCallAsync,
                    innerCancellationToken),
                cancellationToken);

            var enabledTools = BuildEnabledTools(context.DynamicTools, context.NativeToolOptions);
            var result = await manager.ExecuteAsync(request, enabledTools, cancellationToken).ConfigureAwait(false);
            var handle = KernelExecutionEnvelopeFactory.CreateCodeModeHandle(executionRequest, result);
            await rolloutRecorder.AppendExecutionEventAsync(
                threadId,
                result.Success
                    ? KernelExecutionEnvelopeFactory.CreateCompletedEvent(
                        executionRequest,
                        "code mode execution completed",
                        KernelExecutionEnvelopeFactory.CreateCodeModeData(handle, result))
                    : KernelExecutionEnvelopeFactory.CreateFailedEvent(
                        executionRequest,
                        "code mode execution failed",
                        KernelExecutionEnvelopeFactory.CreateCodeModeData(handle, result)),
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

    public async Task<KernelCodeModeOperationResult> WaitAsync(
        string threadId,
        string turnId,
        KernelCodeModeAppHostContext context,
        KernelCodeModeWaitRequest request,
        KernelNestedCodeModeToolExecutor executeToolCallAsync,
        CancellationToken cancellationToken)
    {
        var executionRequest = KernelExecutionEnvelopeFactory.CreateCodeModeWaitRequest(threadId, turnId, context.Cwd, request, context.InteractionEnvelope);
        await rolloutRecorder.AppendExecutionRequestAsync(threadId, executionRequest, cancellationToken).ConfigureAwait(false);
        await rolloutRecorder.AppendExecutionEventAsync(
            threadId,
            KernelExecutionEnvelopeFactory.CreateStartedEvent(executionRequest, "code mode wait started"),
            cancellationToken).ConfigureAwait(false);

        if (!context.NativeToolOptions.CodeModeEnabled)
        {
            var unavailableResult = new KernelCodeModeOperationResult(false, "exec_wait is unavailable", Array.Empty<KernelToolOutputContentItem>());
            await rolloutRecorder.AppendExecutionEventAsync(
                threadId,
                KernelExecutionEnvelopeFactory.CreateFailedEvent(
                    executionRequest,
                    "code mode wait failed",
                    KernelExecutionEnvelopeFactory.CreateCodeModeData(handle: null, unavailableResult)),
                cancellationToken).ConfigureAwait(false);
            return unavailableResult;
        }

        try
        {
            var manager = GetOrCreateManager(threadId, context);
            codeModeTurnsByThread[turnId] = threadId;
            manager.ActivateTurn(
                turnId,
                (toolCall, innerCancellationToken) => ExecuteNestedToolCallAsync(
                    turnId,
                    context,
                    toolCall,
                    executeToolCallAsync,
                    innerCancellationToken),
                cancellationToken);

            var result = await manager.WaitAsync(request, cancellationToken).ConfigureAwait(false);
            var handle = KernelExecutionEnvelopeFactory.CreateCodeModeWaitHandle(executionRequest, request);
            await rolloutRecorder.AppendExecutionEventAsync(
                threadId,
                result.Success
                    ? KernelExecutionEnvelopeFactory.CreateCompletedEvent(
                        executionRequest,
                        request.Terminate ? "code mode termination completed" : "code mode wait completed",
                        KernelExecutionEnvelopeFactory.CreateCodeModeData(handle, result))
                    : KernelExecutionEnvelopeFactory.CreateFailedEvent(
                        executionRequest,
                        request.Terminate ? "code mode termination failed" : "code mode wait failed",
                        KernelExecutionEnvelopeFactory.CreateCodeModeData(handle, result)),
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

    public void DeactivateTurn(string turnId)
    {
        if (!codeModeTurnsByThread.TryRemove(turnId, out var threadId))
        {
            return;
        }

        if (codeModeManagers.TryGetValue(threadId, out var manager))
        {
            manager.DeactivateTurn(turnId);
        }
    }

    public IReadOnlyList<KernelCodeModeEnabledTool> BuildEnabledTools(
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        KernelResponsesNativeToolOptions nativeToolOptions)
    {
        var enabled = new List<KernelCodeModeEnabledTool>();
        foreach (var handler in toolRegistry.EnumerateHandlers())
        {
            if (!KernelCodeModeRuntimeHelpers.ShouldIncludeCodeModeNestedTool(handler, nativeToolOptions))
            {
                continue;
            }

            enabled.Add(KernelCodeModeRuntimeHelpers.BuildCodeModeEnabledTool(handler, nativeToolOptions));
        }

        if (dynamicTools is { Count: > 0 } descriptors)
        {
            foreach (var tool in descriptors)
            {
                var toolName = Normalize(tool.FullName);
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    continue;
                }

                var description = Normalize(tool.Description) ?? string.Empty;
                var inputSchema = tool.InputSchema
                    ?? JsonSerializer.SerializeToElement(new
                    {
                        type = "object",
                        additionalProperties = true,
                    });
                var outputSchema = tool.OutputSchema;
                var reference = KernelCodeModeRuntimeHelpers.ResolveCodeModeToolReference(toolName!);
                enabled.Add(new KernelCodeModeEnabledTool(
                    ToolName: toolName!,
                    GlobalName: KernelCodeModeDescriptionBuilder.NormalizeIdentifier(toolName!),
                    ModulePath: reference.ModulePath,
                    Namespace: reference.Namespace.ToArray(),
                    Name: KernelCodeModeDescriptionBuilder.NormalizeIdentifier(reference.ToolKey),
                    Description: KernelCodeModeDescriptionBuilder.BuildNestedToolDescription(
                        description,
                        toolName!,
                        isFreeform: false,
                        inputSchema,
                        outputSchema),
                    Kind: "function"));
            }
        }

        return enabled
            .OrderBy(static tool => tool.ToolName, StringComparer.Ordinal)
            .GroupBy(static tool => tool.ToolName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        var managers = codeModeManagers.ToArray();
        codeModeManagers.Clear();
        codeModeTurnsByThread.Clear();

        foreach (var pair in managers)
        {
            await pair.Value.DisposeAsync().ConfigureAwait(false);
        }
    }

    private KernelCodeModeManager GetOrCreateManager(string threadId, KernelCodeModeAppHostContext context)
    {
        return codeModeManagers.GetOrAdd(
            threadId,
            static (_, state) =>
            {
                var jsReplOptions = KernelJsReplRuntimeHelpers.ResolveJsReplOptions(
                    state.Cwd,
                    state.PersistedConfigText,
                    Environment.GetEnvironmentVariable("TIANSHU_JS_REPL_NODE_PATH"),
                    Environment.GetEnvironmentVariable("TIANSHU_JS_REPL_NODE_MODULE_DIRS"));
                return new KernelCodeModeManager(
                    new KernelCodeModeOptions(
                        jsReplOptions.NodePath,
                        jsReplOptions.WorkingDirectory));
            },
            context);
    }

    private async Task<JsonElement> ExecuteNestedToolCallAsync(
        string turnId,
        KernelCodeModeAppHostContext context,
        KernelCodeModeToolCall toolCall,
        KernelNestedCodeModeToolExecutor executeToolCallAsync,
        CancellationToken cancellationToken)
    {
        var normalizedToolName = Normalize(toolCall.ToolName);
        if (string.IsNullOrWhiteSpace(normalizedToolName))
        {
            return JsonSerializer.SerializeToElement("tool name is required");
        }

        if (string.Equals(normalizedToolName, "exec", StringComparison.Ordinal)
            || string.Equals(normalizedToolName, "exec_wait", StringComparison.Ordinal))
        {
            return JsonSerializer.SerializeToElement($"tool `{normalizedToolName}` is not enabled in exec");
        }

        var nestedItemId = KernelCodeModeRuntimeHelpers.BuildCodeModeNestedToolCallItemId(turnId, toolCall.RequestId, normalizedToolName!);
        if (toolRegistry.TryGet(normalizedToolName!, out var handler) && handler is not null)
        {
            if (string.Equals(handler.Name, "exec", StringComparison.Ordinal)
                || string.Equals(handler.Name, "exec_wait", StringComparison.Ordinal)
                || string.Equals(handler.Name, "js_repl", StringComparison.Ordinal)
                || string.Equals(handler.Name, "js_repl_reset", StringComparison.Ordinal)
                || string.Equals(handler.Name, KernelToolDiscoveryToolNames.Search, StringComparison.Ordinal))
            {
                return JsonSerializer.SerializeToElement($"tool `{normalizedToolName}` is not enabled in exec");
            }

            KernelToolResult result;
            if (handler is KernelCustomToolHandlerBase)
            {
                if (toolCall.Input is not { ValueKind: JsonValueKind.String } stringInput)
                {
                    return JsonSerializer.SerializeToElement($"tool `{normalizedToolName}` expects a string input");
                }

                result = await executeToolCallAsync(
                    normalizedToolName!,
                    nestedItemId,
                    JsonSerializer.SerializeToElement(new { }),
                    stringInput.GetString() ?? string.Empty,
                    isCustomToolCall: true,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (!KernelCodeModeRuntimeHelpers.TryBuildCodeModeFunctionArguments(normalizedToolName!, toolCall.Input, out var arguments, out var error))
                {
                    return JsonSerializer.SerializeToElement(error ?? $"tool `{normalizedToolName}` expects a JSON object for arguments");
                }

                result = await executeToolCallAsync(
                    normalizedToolName!,
                    nestedItemId,
                    arguments,
                    customInput: null,
                    isCustomToolCall: false,
                    cancellationToken).ConfigureAwait(false);
            }

            return KernelCodeModeRuntimeHelpers.ConvertKernelToolResultToCodeModeResult(normalizedToolName!, handler, result);
        }

        if (KernelToolRuntimeParsingHelpers.TryResolveDynamicToolSchema(context.DynamicTools, normalizedToolName!, out _))
        {
            if (!KernelCodeModeRuntimeHelpers.TryBuildCodeModeFunctionArguments(normalizedToolName!, toolCall.Input, out var arguments, out var error))
            {
                return JsonSerializer.SerializeToElement(error ?? $"tool `{normalizedToolName}` expects a JSON object for arguments");
            }

            var result = await executeToolCallAsync(
                normalizedToolName!,
                nestedItemId,
                arguments,
                customInput: null,
                isCustomToolCall: false,
                cancellationToken).ConfigureAwait(false);

            var outputSchema = KernelCodeModeRuntimeHelpers.TryFindDynamicToolSchema(context.DynamicTools, normalizedToolName!, "outputSchema", "output_schema");
            return KernelCodeModeRuntimeHelpers.ConvertToolResultToCodeModeResult(normalizedToolName!, outputSchema, result, isDynamicTool: true);
        }

        return JsonSerializer.SerializeToElement($"tool `{normalizedToolName}` is not enabled in exec");
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
