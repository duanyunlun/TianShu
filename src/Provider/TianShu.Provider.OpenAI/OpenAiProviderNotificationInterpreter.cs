using TianShu.Contracts.Provider;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI provider 原始通知解释器。
/// Raw-notification interpreter for the OpenAI provider.
/// </summary>
public sealed class OpenAiProviderNotificationInterpreter : IProviderNotificationInterpreter
{
    private static readonly IProviderToolEventFactory ToolEventFactory = new OpenAiProviderToolEventFactory();

    /// <inheritdoc />
    public ProviderNotificationProjection? InterpretRawResponseItemCompleted(ProviderRawResponseItemCompletedNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!IsAssistantCompletionItem(notification.Item?.Type))
        {
            return null;
        }

        return new ProviderNotificationProjection(
            notification.Item?.Id,
            notification.Item?.Type,
            notification.Item?.Phase ?? "final_answer",
            input =>
            {
                var outputText = Normalize(input.PreferredText)
                                 ?? Normalize(notification.Item?.Text)
                                 ?? Normalize(notification.Item?.OutputText);
                if (string.IsNullOrWhiteSpace(outputText))
                {
                    return Array.Empty<ProviderStreamEvent>();
                }

                return
                [
                    new ProviderCompletionEvent(
                        new ProviderCompletion(outputText))
                ];
            });
    }

    /// <inheritdoc />
    public ProviderNotificationProjection? InterpretError(ProviderErrorNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var message = Normalize(notification.Message)
                      ?? Normalize(notification.Error?.Message)
                      ?? "error";
        var additionalDetails = Normalize(notification.Error?.AdditionalDetails);
        var willRetry = notification.WillRetry ?? false;

        return new ProviderNotificationProjection(
            itemId: null,
            status: willRetry ? "retrying" : "error",
            phase: null,
            _ =>
            [
                new ProviderFailureEvent(
                    new ProviderFailure(
                        code: "error",
                        message: message,
                        isRetryable: willRetry,
                        additionalDetails: additionalDetails))
            ]);
    }

    /// <inheritdoc />
    public ProviderNotificationProjection? InterpretItem(ProviderItemNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var method = notification.Method;
        var methodSuffix = method[(method.LastIndexOf('/') + 1)..];
        var itemId = ResolveItemId(notification);
        var itemType = ResolveItemType(notification);
        var eventPhase = ResolveItemPhase(notification);
        var itemStatus = ResolveItemStatus(notification);
        var toolName = ResolveItemToolName(notification);
        var callId = ResolveItemCallId(notification);
        if (string.IsNullOrWhiteSpace(callId)
            && !string.IsNullOrWhiteSpace(itemId)
            && (!string.IsNullOrWhiteSpace(toolName)
                || method.StartsWith("item/tool/", StringComparison.Ordinal)
                || method.StartsWith("item/commandExecution/", StringComparison.Ordinal)
                || method.StartsWith("item/fileChange/", StringComparison.Ordinal)
                || method.StartsWith("item/mcpToolCall/", StringComparison.Ordinal)
                || string.Equals(itemType, "contextCompaction", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(itemType) && itemType.Contains("tool", StringComparison.OrdinalIgnoreCase))
                || string.Equals(itemType, "commandExecution", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemType, "fileChange", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemType, "mcpToolCall", StringComparison.OrdinalIgnoreCase)))
        {
            callId = itemId;
        }

        var delta = ResolveItemDelta(notification);
        if (!string.IsNullOrWhiteSpace(delta) && IsCommentaryDelta(method, itemType))
        {
            return new ProviderNotificationProjection(
                itemId,
                itemType,
                eventPhase ?? "commentary",
                _ => [new ProviderReasoningDeltaEvent(delta)]);
        }

        var reasoningText = ResolveReasoningText(notification);
        if (IsReasoningDelta(method, itemType) && !string.IsNullOrWhiteSpace(reasoningText))
        {
            return new ProviderNotificationProjection(
                itemId,
                itemStatus ?? itemType ?? "reasoning",
                eventPhase ?? "reasoning",
                _ => [new ProviderReasoningDeltaEvent(reasoningText)],
                summaryIndex: notification.SummaryIndex,
                contentIndex: notification.ContentIndex);
        }

        if (!string.IsNullOrEmpty(delta) && IsAssistantTextDelta(method, itemType))
        {
            return new ProviderNotificationProjection(
                itemId,
                itemType,
                eventPhase ?? "final_answer",
                _ => [new ProviderTextDeltaEvent(delta)]);
        }

        var toolOutput = ResolveItemOutput(notification);
        var toolArgs = ResolveItemArguments(notification);
        var requiresApproval = ResolveItemRequiresApproval(notification);
        var looksLikeTool = IsToolNotification(method, itemType, callId, toolName);

        if (string.Equals(method, "item/commandExecution/terminalInteraction", StringComparison.Ordinal))
        {
            var outputText = $"terminal<{notification.ProcessId}> {notification.Stdin}".Trim();
            return new ProviderNotificationProjection(
                itemId,
                methodSuffix,
                eventPhase,
                _ => [ToolEventFactory.CreateToolOutputDelta(new ProviderToolOutputDeltaRequest(callId, itemId, "commandExecution", itemType, notification.Stdin, outputText, requiresApproval))],
                callId: callId ?? itemId,
                toolName: "commandExecution");
        }

        if (string.Equals(method, "item/commandExecution/outputDelta", StringComparison.Ordinal)
            || string.Equals(method, "item/fileChange/outputDelta", StringComparison.Ordinal))
        {
            var resolvedToolName = string.Equals(method, "item/commandExecution/outputDelta", StringComparison.Ordinal)
                ? "commandExecution"
                : "fileChange";
            var outputText = delta ?? toolOutput;
            return new ProviderNotificationProjection(
                itemId,
                methodSuffix,
                eventPhase,
                _ => [ToolEventFactory.CreateToolOutputDelta(new ProviderToolOutputDeltaRequest(callId, itemId, resolvedToolName, itemType, toolArgs, outputText, requiresApproval))],
                callId: callId ?? itemId,
                toolName: resolvedToolName);
        }

        if (looksLikeTool
            && requiresApproval is true
            && !string.IsNullOrWhiteSpace(callId)
            && string.Equals(method, "item/tool/requestApproval", StringComparison.Ordinal))
        {
            var resolvedToolName = toolName ?? itemType ?? "tool";
            return new ProviderNotificationProjection(
                itemId,
                itemStatus ?? methodSuffix,
                eventPhase,
                _ => [ToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, itemId, resolvedToolName, toolArgs, true))],
                callId: callId,
                toolName: resolvedToolName);
        }

        if (string.Equals(method, "item/mcpToolCall/progress", StringComparison.Ordinal))
        {
            return new ProviderNotificationProjection(
                itemId,
                methodSuffix,
                eventPhase,
                _ => [ToolEventFactory.CreateToolOutputDelta(new ProviderToolOutputDeltaRequest(callId, itemId, "mcpToolCall", itemType, toolArgs, notification.Message, requiresApproval))],
                callId: callId ?? itemId,
                toolName: "mcpToolCall");
        }

        if (string.Equals(method, "item/started", StringComparison.Ordinal)
            || string.Equals(method, "item/completed", StringComparison.Ordinal))
        {
            if (IsAssistantCompletionItem(itemType))
            {
                return new ProviderNotificationProjection(
                    itemId,
                    itemStatus ?? itemType ?? methodSuffix,
                    eventPhase,
                    _ => Array.Empty<ProviderStreamEvent>());
            }

            if (looksLikeTool)
            {
                var resolvedToolName = toolName ?? itemType ?? "tool";
                if (string.Equals(method, "item/started", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(callId ?? itemId))
                {
                    return new ProviderNotificationProjection(
                        itemId,
                        itemStatus ?? itemType ?? methodSuffix,
                        eventPhase,
                        _ => [ToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, itemId, resolvedToolName, toolArgs, false))],
                        callId: callId ?? itemId,
                        toolName: resolvedToolName);
                }

                return new ProviderNotificationProjection(
                    itemId,
                    itemStatus ?? itemType ?? methodSuffix,
                    eventPhase,
                    _ => [ToolEventFactory.CreateToolResult(new ProviderToolResultRequest(callId, itemId, toolName, itemType, toolArgs, toolOutput, requiresApproval))],
                    callId: callId ?? itemId,
                    toolName: resolvedToolName);
            }

            return null;
        }

        if (looksLikeTool)
        {
            var lifecycleStatus = itemStatus ?? methodSuffix;
            var resolvedToolName = toolName ?? itemType ?? "tool";
            if (string.Equals(lifecycleStatus, "delta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lifecycleStatus, "updated", StringComparison.OrdinalIgnoreCase))
            {
                return new ProviderNotificationProjection(
                    itemId,
                    lifecycleStatus,
                    eventPhase,
                    _ => [ToolEventFactory.CreateToolOutputDelta(new ProviderToolOutputDeltaRequest(callId, itemId, toolName, itemType, toolArgs, toolOutput ?? delta, requiresApproval))],
                    callId: callId ?? itemId,
                    toolName: resolvedToolName);
            }

            if (string.Equals(lifecycleStatus, "completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lifecycleStatus, "finished", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lifecycleStatus, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lifecycleStatus, "errored", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lifecycleStatus, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return new ProviderNotificationProjection(
                    itemId,
                    lifecycleStatus,
                    eventPhase,
                    _ => [ToolEventFactory.CreateToolResult(new ProviderToolResultRequest(callId, itemId, toolName, itemType, toolArgs, toolOutput, requiresApproval))],
                    callId: callId ?? itemId,
                    toolName: resolvedToolName);
            }

            if (!string.IsNullOrWhiteSpace(callId ?? itemId))
            {
                return new ProviderNotificationProjection(
                    itemId,
                    lifecycleStatus,
                    eventPhase,
                    _ => [ToolEventFactory.CreateToolDirective(new ProviderToolDirectiveRequest(callId, itemId, resolvedToolName, toolArgs, requiresApproval ?? false))],
                    callId: callId ?? itemId,
                    toolName: resolvedToolName);
            }
        }

        return null;
    }

    private static bool IsAssistantCompletionItem(string? itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType))
        {
            return false;
        }

        return itemType.Contains("assistant", StringComparison.OrdinalIgnoreCase)
               || string.Equals(itemType, "agentMessage", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? ResolveItemId(ProviderItemNotification notification)
        => notification.Item?.Id ?? notification.ItemId;

    private static string? ResolveItemType(ProviderItemNotification notification)
        => notification.Item?.Type ?? notification.Type;

    private static string? ResolveItemPhase(ProviderItemNotification notification)
        => notification.Item?.Phase ?? notification.Phase;

    private static string? ResolveItemStatus(ProviderItemNotification notification)
        => notification.Item?.Status ?? notification.Status;

    private static string? ResolveItemCallId(ProviderItemNotification notification)
        => notification.CallId ?? notification.ToolCallId ?? notification.Item?.CallId;

    private static string? ResolveItemToolName(ProviderItemNotification notification)
        => notification.ToolName ?? notification.Name ?? notification.Item?.ToolName ?? notification.Item?.Name;

    private static string? ResolveItemDelta(ProviderItemNotification notification)
        => notification.Delta ?? notification.Item?.Delta;

    private static string? ResolveItemOutput(ProviderItemNotification notification)
        => notification.Output ?? notification.Item?.Output;

    private static string? ResolveItemArguments(ProviderItemNotification notification)
        => notification.Arguments
           ?? notification.Input
           ?? notification.Item?.Arguments
           ?? notification.Item?.Input;

    private static string? ResolveReasoningText(ProviderItemNotification notification)
        => ResolveItemDelta(notification)
           ?? notification.Message
           ?? notification.Item?.Text
           ?? notification.Item?.OutputText
           ?? notification.Output;

    private static bool? ResolveItemRequiresApproval(ProviderItemNotification notification)
    {
        if (notification.RequiresApproval.HasValue)
        {
            return notification.RequiresApproval;
        }

        if (notification.ApprovalRequired.HasValue)
        {
            return notification.ApprovalRequired;
        }

        return notification.ApprovalStateRequired;
    }

    private static bool IsReasoningDelta(string method, string? itemType)
    {
        if (string.Equals(method, "item/reasoning/summaryTextDelta", StringComparison.Ordinal)
            || string.Equals(method, "item/reasoning/textDelta", StringComparison.Ordinal)
            || string.Equals(method, "item/reasoning/summaryPartAdded", StringComparison.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(itemType)
               && itemType.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
               && string.Equals(method, "item/delta", StringComparison.Ordinal);
    }

    private static bool IsAssistantTextDelta(string method, string? itemType)
    {
        if (!string.Equals(method, "item/delta", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(itemType))
        {
            return false;
        }

        return !itemType.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
               && (itemType.Contains("assistant", StringComparison.OrdinalIgnoreCase)
                   || itemType.Contains("agent", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCommentaryDelta(string method, string? itemType)
        => string.Equals(method, "item/agentMessage/delta", StringComparison.Ordinal)
           && IsAssistantCompletionItem(itemType);

    private static bool IsToolNotification(string method, string? itemType, string? callId, string? toolName)
    {
        var looksLikeTool = (!string.IsNullOrWhiteSpace(itemType) && itemType.Contains("tool", StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrWhiteSpace(callId)
            || !string.IsNullOrWhiteSpace(toolName);
        if (!looksLikeTool
            && (method.StartsWith("item/commandExecution/", StringComparison.Ordinal)
                || method.StartsWith("item/fileChange/", StringComparison.Ordinal)
                || method.StartsWith("item/mcpToolCall/", StringComparison.Ordinal)
                || string.Equals(itemType, "contextCompaction", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemType, "commandExecution", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemType, "fileChange", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemType, "mcpToolCall", StringComparison.OrdinalIgnoreCase)))
        {
            looksLikeTool = true;
        }

        return looksLikeTool;
    }
}
