using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Responses tool continuation 运行时，负责从 output items 提取工具调用并生成下一轮 tool output items。
/// Runtime that extracts tool calls from response output items and builds the next turn tool output items.
/// </summary>
internal sealed class KernelResponsesToolContinuationRuntime
{
    private readonly KernelToolRegistry toolRegistry;
    private readonly Func<
        ModelFunctionCall,
        bool,
        KernelAsyncReadWriteLock,
        TurnOperationState,
        TurnRequestContext,
        CancellationToken,
        Task<object>> executeModelFunctionCallWithParallelLockAsync;

    public KernelResponsesToolContinuationRuntime(
        KernelToolRegistry toolRegistry,
        Func<
            ModelFunctionCall,
            bool,
            KernelAsyncReadWriteLock,
            TurnOperationState,
            TurnRequestContext,
            CancellationToken,
            Task<object>> executeModelFunctionCallWithParallelLockAsync)
    {
        this.toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        this.executeModelFunctionCallWithParallelLockAsync = executeModelFunctionCallWithParallelLockAsync
                                                            ?? throw new ArgumentNullException(nameof(executeModelFunctionCallWithParallelLockAsync));
    }

    public IReadOnlyList<object> BuildFollowUpResponseItems(
        IReadOnlyList<JsonElement> outputItemsAdded,
        IReadOnlyList<JsonElement> outputItemsDone)
    {
        var source = outputItemsDone.Count > 0 ? outputItemsDone : outputItemsAdded;
        var items = new List<object>(source.Count);
        foreach (var item in source)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(item.Clone());
        }

        return items;
    }

    public List<ModelFunctionCall> ExtractFunctionCalls(IEnumerable<JsonElement> outputItemsDone)
    {
        var calls = new List<ModelFunctionCall>();
        foreach (var item in outputItemsDone)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = Normalize(ReadString(item, "type"));
            var callId = ReadString(item, "call_id");
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            if (string.Equals(type, "function_call", StringComparison.OrdinalIgnoreCase))
            {
                var name = ReadString(item, "name");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(callId))
                {
                    continue;
                }

                var arguments = ReadString(item, "arguments");
                if (arguments is null)
                {
                    continue;
                }

                var toolNamespace = ReadString(item, "namespace");
                calls.Add(new ModelFunctionCall(name, callId, arguments, null, false, toolNamespace, false));
                continue;
            }

            if (string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase))
            {
                var name = ReadString(item, "name");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(callId))
                {
                    continue;
                }

                var input = ReadString(item, "input") ?? string.Empty;
                calls.Add(new ModelFunctionCall(name, callId, null, input, true, null, false));
                continue;
            }

            if (string.Equals(type, "tool_search_call", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(callId))
                {
                    continue;
                }

                var execution = Normalize(ReadString(item, "execution"));
                if (string.Equals(execution, "server", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!item.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                calls.Add(new ModelFunctionCall(
                    KernelToolDiscoveryToolNames.Search,
                    callId!,
                    arguments.GetRawText(),
                    null,
                    false,
                    null,
                    true));
            }
        }

        return calls;
    }

    public async Task<List<object>> ExecuteFunctionCallsAsync(
        IReadOnlyList<ModelFunctionCall> functionCalls,
        KernelAsyncReadWriteLock parallelExecutionLock,
        TurnOperationState state,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        var toolTasks = new List<Task<object>>(functionCalls.Count);
        foreach (var call in functionCalls)
        {
            var supportsParallel = toolRegistry.ToolSupportsParallelToolCalls(call.Name);
            toolTasks.Add(executeModelFunctionCallWithParallelLockAsync(
                call,
                supportsParallel,
                parallelExecutionLock,
                state,
                context,
                cancellationToken));
        }

        var nextInput = new List<object>(toolTasks.Count);
        foreach (var task in toolTasks)
        {
            var outputItem = await task.ConfigureAwait(false);
            nextInput.Add(outputItem);
        }

        return nextInput;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadString(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : current.ToString();
    }
}
