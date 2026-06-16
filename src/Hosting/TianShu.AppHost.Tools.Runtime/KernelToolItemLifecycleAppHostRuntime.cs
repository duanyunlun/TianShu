using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelToolItemLifecycleAppHostRuntime
{
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;

    public KernelToolItemLifecycleAppHostRuntime(Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.writeNotificationAsync = writeNotificationAsync;
    }

    public async Task EmitCollabToolCallStartedNotificationAsync(
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!KernelCollaborationLifecycleHelpers.TryCreateCollabLifecycleDescriptor(toolName, arguments, out var descriptor))
        {
            return;
        }

        await writeNotificationAsync("item/started", new
        {
            threadId,
            turnId,
            item = KernelCollaborationLifecycleHelpers.CreateCollabToolCallItem(
                itemId,
                descriptor.Tool,
                status: "inProgress",
                senderThreadId: threadId,
                receiverThreadIds: descriptor.ReceiverThreadIds,
                prompt: descriptor.Prompt,
                model: descriptor.Model,
                reasoningEffort: descriptor.ReasoningEffort,
                agentsStates: new Dictionary<string, object?>(StringComparer.Ordinal)),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task EmitCollabToolCallCompletedNotificationAsync(
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        JsonElement arguments,
        KernelToolResult result,
        CancellationToken cancellationToken)
    {
        if (!KernelCollaborationLifecycleHelpers.TryCreateCollabLifecycleDescriptor(toolName, arguments, out var descriptor))
        {
            return;
        }

        var completedState = KernelCollaborationLifecycleHelpers.BuildCollabCompletedState(toolName, arguments, result, descriptor);
        await writeNotificationAsync("item/completed", new
        {
            threadId,
            turnId,
            item = KernelCollaborationLifecycleHelpers.CreateCollabToolCallItem(
                itemId,
                descriptor.Tool,
                completedState.Status,
                senderThreadId: threadId,
                receiverThreadIds: completedState.ReceiverThreadIds,
                prompt: descriptor.Prompt,
                model: descriptor.Model,
                reasoningEffort: descriptor.ReasoningEffort,
                agentsStates: completedState.AgentsStates),
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task EmitDynamicToolCallStartedNotificationAsync(
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
        => writeNotificationAsync("item/started", new
        {
            threadId,
            turnId,
            item = new
            {
                id = itemId,
                type = "dynamicToolCall",
                tool = toolName,
                arguments,
                status = "inProgress",
                contentItems = (object[]?)null,
                success = (bool?)null,
                durationMs = (long?)null,
            },
        }, cancellationToken);

    public Task EmitDynamicToolCallCompletedNotificationAsync(
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        JsonElement arguments,
        KernelToolResult result,
        TimeSpan duration,
        CancellationToken cancellationToken)
        => writeNotificationAsync("item/completed", new
        {
            threadId,
            turnId,
            item = new
            {
                id = itemId,
                type = "dynamicToolCall",
                tool = toolName,
                arguments,
                status = result.Success ? "completed" : "failed",
                contentItems = KernelToolItemLifecycleHelpers.BuildDynamicToolContentItems(result),
                success = result.Success,
                durationMs = (long)Math.Max(0, duration.TotalMilliseconds),
            },
        }, cancellationToken);

    public async Task EmitFileChangeStartedNotificationAsync(
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        JsonElement arguments,
        string cwd,
        CancellationToken cancellationToken)
    {
        var changes = KernelToolItemLifecycleHelpers.BuildFileChangeChanges(toolName, arguments, cwd);
        if (changes.Length == 0)
        {
            return;
        }

        await writeNotificationAsync("item/started", new
        {
            threadId,
            turnId,
            item = new
            {
                id = itemId,
                type = "fileChange",
                changes,
                status = "inProgress",
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task EmitFileChangeCompletedNotificationAsync(
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        JsonElement arguments,
        string cwd,
        string status,
        CancellationToken cancellationToken)
    {
        var changes = KernelToolItemLifecycleHelpers.BuildFileChangeChanges(toolName, arguments, cwd);
        if (changes.Length == 0)
        {
            return;
        }

        await writeNotificationAsync("item/completed", new
        {
            threadId,
            turnId,
            item = new
            {
                id = itemId,
                type = "fileChange",
                changes,
                status,
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task EmitImageViewLifecycleNotificationsAsync(
        string threadId,
        string turnId,
        string itemId,
        JsonElement arguments,
        string cwd,
        CancellationToken cancellationToken)
    {
        var path = KernelToolItemLifecycleHelpers.ResolveImageViewPath(arguments, cwd);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var item = new
        {
            id = itemId,
            type = "imageView",
            path,
        };

        await writeNotificationAsync("item/started", new
        {
            threadId,
            turnId,
            item,
        }, cancellationToken).ConfigureAwait(false);

        await writeNotificationAsync("item/completed", new
        {
            threadId,
            turnId,
            item,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task EmitMcpToolCallStartedNotificationAsync(
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        JsonElement arguments,
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        CancellationToken cancellationToken)
    {
        if (!KernelToolItemLifecycleHelpers.TryCreateMcpToolLifecycleDescriptor(dynamicTools, toolName, out var descriptor))
        {
            return;
        }

        await writeNotificationAsync("item/started", new
        {
            threadId,
            turnId,
            item = KernelToolItemLifecycleHelpers.CreateMcpToolCallItem(
                itemId,
                descriptor.Server,
                descriptor.Tool,
                status: "inProgress",
                arguments,
                resultPayload: null,
                errorPayload: null,
                durationMs: null),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task EmitMcpToolCallCompletedNotificationAsync(
        string threadId,
        string turnId,
        string itemId,
        string toolName,
        JsonElement arguments,
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        KernelToolResult result,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (!KernelToolItemLifecycleHelpers.TryCreateMcpToolLifecycleDescriptor(dynamicTools, toolName, out var descriptor))
        {
            return;
        }

        var durationMs = duration < TimeSpan.Zero ? 0L : (long?)Math.Max(0, Math.Round(duration.TotalMilliseconds));
        var status = result.Success ? "completed" : "failed";
        var resultPayload = result.Success ? KernelToolItemLifecycleHelpers.CreateMcpToolCallResultPayload(result) : null;
        var errorPayload = result.Success
            ? null
            : new
            {
                message = Normalize(result.OutputText) ?? "mcp_tool_call_failed",
            };

        await writeNotificationAsync("item/completed", new
        {
            threadId,
            turnId,
            item = KernelToolItemLifecycleHelpers.CreateMcpToolCallItem(
                itemId,
                descriptor.Server,
                descriptor.Tool,
                status,
                arguments,
                resultPayload,
                errorPayload,
                durationMs),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task EmitWebSearchOutputItemNotificationsAsync(
        TurnOperationState state,
        IEnumerable<JsonElement> outputItemsAdded,
        IEnumerable<JsonElement> outputItemsDone,
        CancellationToken cancellationToken)
    {
        var startedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var observation in KernelToolItemLifecycleHelpers.CaptureWebSearchOutputItems(outputItemsAdded))
        {
            await writeNotificationAsync("item/started", new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                item = KernelToolItemLifecycleHelpers.BuildWebSearchNotificationItem(observation),
            }, CancellationToken.None).ConfigureAwait(false);
            startedIds.Add(observation.CallId);
        }

        foreach (var observation in KernelToolItemLifecycleHelpers.CaptureWebSearchOutputItems(outputItemsDone))
        {
            if (startedIds.Add(observation.CallId))
            {
                await writeNotificationAsync("item/started", new
                {
                    threadId = state.ThreadId,
                    turnId = state.TurnId,
                    item = KernelToolItemLifecycleHelpers.BuildWebSearchNotificationItem(observation),
                }, CancellationToken.None).ConfigureAwait(false);
            }

            await writeNotificationAsync("item/completed", new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                item = KernelToolItemLifecycleHelpers.BuildWebSearchNotificationItem(observation),
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task EmitImageGenerationOutputItemNotificationsAsync(
        TurnOperationState state,
        IEnumerable<JsonElement> outputItemsDone,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var observations = await KernelToolItemLifecycleHelpers.CaptureImageGenerationOutputItemsAsync(outputItemsDone, cwd, cancellationToken)
            .ConfigureAwait(false);
        foreach (var observation in observations)
        {
            await writeNotificationAsync("item/started", new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                item = new
                {
                    type = "imageGeneration",
                    id = observation.CallId,
                    status = "in_progress",
                    revisedPrompt = (string?)null,
                    result = string.Empty,
                },
            }, CancellationToken.None).ConfigureAwait(false);

            await writeNotificationAsync("item/completed", new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                item = new
                {
                    type = "imageGeneration",
                    id = observation.CallId,
                    status = observation.Status,
                    revisedPrompt = observation.RevisedPrompt,
                    result = observation.Result,
                    savedPath = observation.SavedPath,
                },
            }, CancellationToken.None).ConfigureAwait(false);

            await writeNotificationAsync("rawResponseItem/completed", new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                item = observation.RawItem,
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public Task EmitCommandExecutionStartedNotificationAsync(
        string threadId,
        string turnId,
        string itemId,
        string command,
        string cwd,
        string? processId,
        CancellationToken cancellationToken)
        => writeNotificationAsync(
            "item/started",
            new
            {
                threadId,
                turnId,
                item = KernelToolItemLifecycleHelpers.BuildCommandExecutionItemPayload(
                    itemId,
                    command,
                    cwd,
                    processId,
                    status: "inProgress",
                    aggregatedOutput: null,
                    exitCode: null,
                    durationMs: null),
            },
            cancellationToken);

    public Task EmitCommandExecutionCompletedNotificationAsync(
        string threadId,
        string turnId,
        string itemId,
        string command,
        string cwd,
        string? processId,
        string status,
        string? aggregatedOutput,
        int? exitCode,
        long? durationMs,
        CancellationToken cancellationToken)
        => writeNotificationAsync(
            "item/completed",
            new
            {
                threadId,
                turnId,
                item = KernelToolItemLifecycleHelpers.BuildCommandExecutionItemPayload(
                    itemId,
                    command,
                    cwd,
                    processId,
                    status,
                    aggregatedOutput,
                    exitCode,
                    durationMs),
            },
            cancellationToken);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
