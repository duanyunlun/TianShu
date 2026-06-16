using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAICompatible;

/// <summary>
/// OpenAI-compatible Chat Completions request composer。
/// Request composer for OpenAI-compatible Chat Completions.
/// </summary>
public sealed class OpenAiChatCompletionsRequestComposer : IProviderResponsesRequestComposer
{
    /// <inheritdoc />
    public string WireApi => ProviderWireApi.OpenAiChatCompletions;

    /// <inheritdoc />
    public ProviderResponsesRequestComposition Compose(ProviderResponsesRequestComposerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Model);
        ArgumentNullException.ThrowIfNull(context.Input);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = context.Model,
            ["messages"] = BuildMessages(context.Instructions, context.Input),
            ["stream"] = context.Stream ?? true,
        };

        if (context.Tools.Count > 0)
        {
            payload["tools"] = context.Tools.Select(static tool => JsonSerializer.Deserialize<object?>(tool.GetRawText())).ToArray();
        }

        if (!string.IsNullOrWhiteSpace(context.ToolChoice))
        {
            payload["tool_choice"] = context.ToolChoice;
        }

        if (context.ParallelToolCalls is not null)
        {
            payload["parallel_tool_calls"] = context.ParallelToolCalls.Value;
        }

        if (!string.IsNullOrWhiteSpace(context.ServiceTier))
        {
            payload["service_tier"] = context.ServiceTier;
        }

        AddReasoningOptions(payload, context);

        return new ProviderResponsesRequestComposition(payload, Array.Empty<JsonElement>(), InputPropertyName: null);
    }

    private static void AddReasoningOptions(
        IDictionary<string, object?> payload,
        ProviderResponsesRequestComposerContext context)
    {
        if (context.ReasoningEnabled == false)
        {
            return;
        }

        if (IsQwenThinkingModel(context.Model))
        {
            payload["enable_thinking"] = true;
            if (context.ReasoningBudgetTokens is > 0)
            {
                payload["thinking_budget"] = context.ReasoningBudgetTokens.Value;
            }

            return;
        }

        if (IsOpenAiChatReasoningModel(context.Model) && !string.IsNullOrWhiteSpace(context.ReasoningEffort))
        {
            payload["reasoning_effort"] = NormalizeEffort(context.ReasoningEffort);
        }
    }

    private static bool IsQwenThinkingModel(string model)
        => model.Contains("qwen", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenAiChatReasoningModel(string model)
        => model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase)
           || model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
           || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
           || model.StartsWith("o4", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeEffort(string? effort)
        => string.Equals(effort, "xhigh", StringComparison.OrdinalIgnoreCase) ? "high" : effort ?? "medium";

    private static IReadOnlyList<Dictionary<string, object?>> BuildMessages(
        string? instructions,
        IReadOnlyList<JsonElement> input)
    {
        List<Dictionary<string, object?>> messages = [];
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            messages.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["role"] = "system",
                ["content"] = instructions,
            });
        }

        for (var index = 0; index < input.Count; index++)
        {
            if (TryBuildFunctionCallMessages(input, ref index, out var functionCallMessages))
            {
                messages.AddRange(functionCallMessages);
                continue;
            }

            var item = input[index];
            if (IsTypedObject(item, "function_call_output"))
            {
                continue;
            }

            if (TryBuildMessage(item, out var message))
            {
                messages.Add(message);
            }
        }

        return messages.Count == 0
            ? [new Dictionary<string, object?>(StringComparer.Ordinal) { ["role"] = "user", ["content"] = string.Empty }]
            : messages;
    }

    private static bool TryBuildFunctionCallMessages(
        IReadOnlyList<JsonElement> input,
        ref int index,
        out IReadOnlyList<Dictionary<string, object?>> messages)
    {
        messages = [];
        List<FunctionToolCallCandidate> toolCalls = [];

        var currentIndex = index;
        while (currentIndex < input.Count
               && TryBuildFunctionToolCall(
                   input[currentIndex],
                   out var callId,
                   out var toolCall,
                   out var itemReasoningContent,
                   out var itemContent))
        {
            toolCalls.Add(new FunctionToolCallCandidate(callId, toolCall, itemReasoningContent, itemContent));
            currentIndex++;
        }

        if (toolCalls.Count == 0)
        {
            return false;
        }

        List<FunctionToolOutputCandidate> toolOutputs = [];
        while (currentIndex < input.Count
               && TryBuildFunctionCallOutputMessage(
                   input[currentIndex],
                   out var outputCallId,
                   out var outputMessage))
        {
            toolOutputs.Add(new FunctionToolOutputCandidate(outputCallId, outputMessage));
            currentIndex++;
        }

        var outputCallIds = toolOutputs
            .Select(static output => output.CallId)
            .ToHashSet(StringComparer.Ordinal);
        var matchedToolCalls = toolCalls
            .Where(call => outputCallIds.Contains(call.CallId))
            .ToArray();
        if (matchedToolCalls.Length == 0)
        {
            index = currentIndex - 1;
            return true;
        }

        List<string> contentParts = [];
        string? reasoningContent = null;
        foreach (var candidate in matchedToolCalls)
        {
            if (string.IsNullOrWhiteSpace(reasoningContent)
                && !string.IsNullOrWhiteSpace(candidate.ReasoningContent))
            {
                reasoningContent = candidate.ReasoningContent;
            }

            if (!string.IsNullOrWhiteSpace(candidate.Content))
            {
                contentParts.Add(candidate.Content);
            }
        }

        var assistantMessage = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["role"] = "assistant",
            ["content"] = contentParts.Count == 0 ? string.Empty : string.Join(Environment.NewLine, contentParts),
            ["tool_calls"] = matchedToolCalls.Select(static call => call.ToolCall).ToArray(),
        };
        if (!string.IsNullOrWhiteSpace(reasoningContent))
        {
            assistantMessage["reasoning_content"] = reasoningContent;
        }

        var outputByCallId = toolOutputs
            .GroupBy(static output => output.CallId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().Message, StringComparer.Ordinal);
        var blockMessages = new List<Dictionary<string, object?>>(matchedToolCalls.Length + 1)
        {
            assistantMessage,
        };
        foreach (var call in matchedToolCalls)
        {
            if (outputByCallId.TryGetValue(call.CallId, out var output))
            {
                blockMessages.Add(output);
            }
        }

        messages = blockMessages;
        index = currentIndex - 1;
        return true;
    }

    private static bool TryBuildFunctionToolCall(
        JsonElement item,
        out string callId,
        out Dictionary<string, object?> toolCall,
        out string? reasoningContent,
        out string? content)
    {
        callId = string.Empty;
        toolCall = new Dictionary<string, object?>(StringComparer.Ordinal);
        reasoningContent = null;
        content = null;
        if (!IsTypedObject(item, "function_call"))
        {
            return false;
        }

        callId = ReadString(item, "call_id") ?? string.Empty;
        var name = ReadString(item, "name");
        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var arguments = ReadString(item, "arguments") ?? "{}";
        reasoningContent = ReadString(item, "reasoning_content")
                           ?? ReadString(item, "reasoning");
        content = ReadString(item, "content");
        toolCall = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = callId,
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["arguments"] = string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments,
            },
        };
        return true;
    }

    private static bool TryBuildFunctionCallOutputMessage(
        JsonElement item,
        out string callId,
        out Dictionary<string, object?> message)
    {
        callId = string.Empty;
        message = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!IsTypedObject(item, "function_call_output"))
        {
            return false;
        }

        callId = ReadString(item, "call_id") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(callId))
        {
            return false;
        }

        message["role"] = "tool";
        message["tool_call_id"] = callId;
        message["content"] = ReadFunctionCallOutputContent(item);
        return true;
    }

    private static bool TryBuildMessage(JsonElement item, out Dictionary<string, object?> message)
    {
        message = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var role = ReadString(item, "role");
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        role = string.Equals(role, "developer", StringComparison.OrdinalIgnoreCase) ? "system" : role;
        var reasoningContent = ReadString(item, "reasoning_content")
                               ?? ReadString(item, "reasoning");
        var content = ReadContentText(item);
        if (string.IsNullOrWhiteSpace(content)
            && (string.IsNullOrWhiteSpace(reasoningContent)
                || !string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        message["role"] = role;
        message["content"] = content ?? string.Empty;
        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(reasoningContent))
        {
            message["reasoning_content"] = reasoningContent;
        }

        return true;
    }

    private static bool IsTypedObject(JsonElement item, string type)
        => item.ValueKind == JsonValueKind.Object
           && string.Equals(ReadString(item, "type"), type, StringComparison.OrdinalIgnoreCase);

    private static string ReadFunctionCallOutputContent(JsonElement item)
    {
        if (!item.TryGetProperty("output", out var output))
        {
            return string.Empty;
        }

        return output.ValueKind == JsonValueKind.String
            ? output.GetString() ?? string.Empty
            : output.GetRawText();
    }

    private static string? ReadContentText(JsonElement item)
    {
        if (!item.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<string> parts = [];
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var text = ReadString(part, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record FunctionToolCallCandidate(
        string CallId,
        Dictionary<string, object?> ToolCall,
        string? ReasoningContent,
        string? Content);

    private sealed record FunctionToolOutputCandidate(
        string CallId,
        Dictionary<string, object?> Message);
}
