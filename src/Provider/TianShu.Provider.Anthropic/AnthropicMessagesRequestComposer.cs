using System.Text;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Anthropic;

/// <summary>
/// Anthropic Messages 请求组合器。
/// Request composer for Anthropic Messages.
/// </summary>
public sealed class AnthropicMessagesRequestComposer : IProviderResponsesRequestComposer
{
    private const int DefaultMaxTokens = 4096;

    /// <inheritdoc />
    public string WireApi => ProviderWireApi.AnthropicMessages;

    /// <inheritdoc />
    public ProviderResponsesRequestComposition Compose(ProviderResponsesRequestComposerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Model);
        ArgumentNullException.ThrowIfNull(context.Input);

        var systemParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Instructions))
        {
            systemParts.Add(context.Instructions);
        }

        var messages = BuildMessages(context.Input, systemParts, context.Model);
        var thinkingBudgetTokens = ResolveThinkingBudgetTokens(context);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = context.Model,
            ["max_tokens"] = thinkingBudgetTokens is > 0
                ? Math.Max(DefaultMaxTokens, thinkingBudgetTokens.Value + 1024)
                : DefaultMaxTokens,
            ["messages"] = messages.Count == 0
                ? [BuildTextMessage("user", string.Empty)]
                : messages,
            ["stream"] = context.Stream ?? true,
        };

        if (systemParts.Count > 0)
        {
            payload["system"] = string.Join(Environment.NewLine + Environment.NewLine, systemParts);
        }

        if (context.Tools.Count > 0)
        {
            payload["tools"] = CloneJsonElements(context.Tools);
        }

        if (!string.IsNullOrWhiteSpace(context.ToolChoice))
        {
            payload["tool_choice"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = string.Equals(context.ToolChoice, "auto", StringComparison.OrdinalIgnoreCase)
                    ? "auto"
                    : "any",
            };
        }

        if (thinkingBudgetTokens is > 0)
        {
            payload["thinking"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "enabled",
                ["budget_tokens"] = thinkingBudgetTokens.Value,
                ["display"] = "summarized",
            };
        }

        return new ProviderResponsesRequestComposition(payload, Array.Empty<JsonElement>(), InputPropertyName: null);
    }

    private static int? ResolveThinkingBudgetTokens(ProviderResponsesRequestComposerContext context)
    {
        if (context.ReasoningEnabled == false || !IsClaudeModel(context.Model))
        {
            return null;
        }

        if (context.ReasoningBudgetTokens is > 0)
        {
            return Math.Max(1024, context.ReasoningBudgetTokens.Value);
        }

        return context.ReasoningEffort?.Trim().ToLowerInvariant() switch
        {
            "low" => 1024,
            "high" => 8192,
            "xhigh" => 16384,
            null or "" => null,
            _ => 4096,
        };
    }

    private static bool IsClaudeModel(string model)
        => model.Contains("claude", StringComparison.OrdinalIgnoreCase)
           || model.Contains("anthropic", StringComparison.OrdinalIgnoreCase);

    private static List<Dictionary<string, object?>> BuildMessages(
        IReadOnlyList<JsonElement> input,
        List<string> systemParts,
        string model)
    {
        var messages = new List<Dictionary<string, object?>>();
        for (var index = 0; index < input.Count;)
        {
            var item = input[index];
            if (item.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }

            var type = ReadString(item, "type");
            if (string.Equals(type, "function_call", StringComparison.OrdinalIgnoreCase))
            {
                if (TryBuildFlattenedNonClaudeToolResultMessage(input, index, model, out var flattenedToolResultMessage, out var nextIndex))
                {
                    messages.Add(flattenedToolResultMessage);
                    index = nextIndex;
                    continue;
                }

                if (TryBuildToolUseMessage(input, ref index, model, out var toolUseMessage))
                {
                    messages.Add(toolUseMessage);
                }

                continue;
            }

            if (string.Equals(type, "function_call_output", StringComparison.OrdinalIgnoreCase))
            {
                if (TryBuildToolResultMessage(input, ref index, out var toolResultMessage))
                {
                    messages.Add(toolResultMessage);
                }

                continue;
            }

            if (!string.Equals(type, "message", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            var role = NormalizeRole(ReadString(item, "role"));
            var contentBlocks = BuildContentBlocks(item);
            if (contentBlocks.Count == 0)
            {
                index++;
                continue;
            }

            if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
            {
                systemParts.AddRange(contentBlocks.Select(static block => block.TryGetValue("text", out var text) ? text as string : null)
                    .Where(static text => !string.IsNullOrWhiteSpace(text))!);
                index++;
                continue;
            }

            messages.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["role"] = role,
                ["content"] = contentBlocks,
            });
            index++;
        }

        return messages;
    }

    private static Dictionary<string, object?> BuildTextMessage(string role, string text)
        => new(StringComparer.Ordinal)
        {
            ["role"] = role,
            ["content"] = new[]
            {
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "text",
                    ["text"] = text,
                },
            },
        };

    private static bool TryBuildToolUseMessage(
        IReadOnlyList<JsonElement> input,
        ref int index,
        string model,
        out Dictionary<string, object?> message)
    {
        message = new Dictionary<string, object?>(StringComparer.Ordinal);
        var contentBlocks = new List<object?>();
        var reasoningArtifacts = new List<string>();
        var validToolUseCount = 0;

        while (index < input.Count)
        {
            var item = input[index];
            if (item.ValueKind != JsonValueKind.Object
                || !string.Equals(ReadString(item, "type"), "function_call", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var reasoningContent = ReadString(item, "reasoning_content");
            if (!string.IsNullOrWhiteSpace(reasoningContent)
                && !reasoningArtifacts.Contains(reasoningContent!, StringComparer.Ordinal))
            {
                reasoningArtifacts.Add(reasoningContent!);
            }

            foreach (var thinkingArtifact in ReadThinkingBlockReasoningArtifacts(item))
            {
                if (!reasoningArtifacts.Contains(thinkingArtifact, StringComparer.Ordinal))
                {
                    reasoningArtifacts.Add(thinkingArtifact);
                }
            }

            if (TryAppendToolUseBlock(item, contentBlocks))
            {
                validToolUseCount++;
            }

            index++;
        }

        if (validToolUseCount == 0)
        {
            return false;
        }

        var reasoningArtifact = BuildReasoningArtifact(reasoningArtifacts);
        if (!HasThinkingBlock(contentBlocks) && !string.IsNullOrWhiteSpace(reasoningArtifact))
        {
            contentBlocks.Insert(0, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "thinking",
                ["thinking"] = reasoningArtifact,
            });
        }

        message["role"] = "assistant";
        AppendReasoningContentCompatibilityField(reasoningArtifact, message, model);
        message["content"] = contentBlocks;
        return true;
    }

    private static bool TryAppendToolUseBlock(JsonElement item, List<object?> contentBlocks)
    {
        var callId = ReadString(item, "call_id");
        var name = ReadString(item, "name");
        var arguments = ReadString(item, "arguments") ?? "{}";
        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        AppendThinkingBlocks(item, contentBlocks);
        contentBlocks.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "tool_use",
            ["id"] = callId,
            ["name"] = name,
            ["input"] = DeserializeJsonObject(arguments),
        });

        return true;
    }

    private static void AppendReasoningContentCompatibilityField(
        string? reasoningContent,
        Dictionary<string, object?> message,
        string model)
    {
        if (IsClaudeModel(model))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(reasoningContent))
        {
            return;
        }

        // 兼容端点可能把 Anthropic Messages 再转回 DeepSeek Chat Completions，
        // 此时后端仍要求 assistant message 原样携带 reasoning_content。
        message["reasoning_content"] = reasoningContent;
    }

    private static string? BuildReasoningArtifact(IReadOnlyList<string> reasoningArtifacts)
    {
        if (reasoningArtifacts.Count == 0)
        {
            return null;
        }

        if (reasoningArtifacts.Count == 1)
        {
            return reasoningArtifacts[0];
        }

        return string.Join(Environment.NewLine, reasoningArtifacts);
    }

    private static IEnumerable<string> ReadThinkingBlockReasoningArtifacts(JsonElement item)
    {
        if (!item.TryGetProperty("thinking_blocks", out var thinkingBlocks)
            || thinkingBlocks.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var block in thinkingBlocks.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = ReadString(block, "type");
            if (!string.Equals(type, "thinking", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var thinking = ReadString(block, "thinking");
            if (!string.IsNullOrWhiteSpace(thinking))
            {
                yield return thinking!;
            }
        }
    }

    private static bool HasThinkingBlock(List<object?> contentBlocks)
    {
        foreach (var block in contentBlocks)
        {
            string? typeText = null;
            if (block is Dictionary<string, object?> dictionary
                && dictionary.TryGetValue("type", out var dictionaryType)
                && dictionaryType is string dictionaryTypeText)
            {
                typeText = dictionaryTypeText;
            }
            else if (block is JsonElement element
                     && element.ValueKind == JsonValueKind.Object)
            {
                typeText = ReadString(element, "type");
            }

            if (string.IsNullOrWhiteSpace(typeText))
            {
                continue;
            }

            if (string.Equals(typeText, "thinking", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeText, "redacted_thinking", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendThinkingBlocks(JsonElement item, List<object?> contentBlocks)
    {
        if (!item.TryGetProperty("thinking_blocks", out var thinkingBlocks)
            || thinkingBlocks.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var block in thinkingBlocks.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object
                || !block.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            contentBlocks.Add(JsonSerializer.Deserialize<object?>(block.GetRawText()));
        }
    }

    private static bool TryBuildToolResultMessage(
        IReadOnlyList<JsonElement> input,
        ref int index,
        out Dictionary<string, object?> message)
    {
        message = new Dictionary<string, object?>(StringComparer.Ordinal);
        var contentBlocks = new List<Dictionary<string, object?>>();

        while (index < input.Count)
        {
            var item = input[index];
            if (item.ValueKind != JsonValueKind.Object
                || !string.Equals(ReadString(item, "type"), "function_call_output", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var callId = ReadString(item, "call_id");
            if (!string.IsNullOrWhiteSpace(callId))
            {
                contentBlocks.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = callId,
                    ["content"] = ReadToolOutput(item),
                });
            }

            index++;
        }

        if (contentBlocks.Count == 0)
        {
            return false;
        }

        message["role"] = "user";
        message["content"] = contentBlocks;
        return true;
    }

    private static bool TryBuildFlattenedNonClaudeToolResultMessage(
        IReadOnlyList<JsonElement> input,
        int index,
        string model,
        out Dictionary<string, object?> message,
        out int nextIndex)
    {
        message = new Dictionary<string, object?>(StringComparer.Ordinal);
        nextIndex = index;

        if (IsClaudeModel(model))
        {
            return false;
        }

        var calls = new List<ToolReplayCall>();
        var hasReasoningArtifact = false;
        var cursor = index;
        while (cursor < input.Count)
        {
            var item = input[cursor];
            if (item.ValueKind != JsonValueKind.Object
                || !string.Equals(ReadString(item, "type"), "function_call", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var reasoningContent = ReadString(item, "reasoning_content");
            hasReasoningArtifact |= !string.IsNullOrWhiteSpace(reasoningContent);
            hasReasoningArtifact |= ReadThinkingBlockReasoningArtifacts(item).Any();
            calls.Add(new ToolReplayCall(
                ReadString(item, "call_id"),
                ReadString(item, "name"),
                ReadString(item, "arguments") ?? "{}"));
            cursor++;
        }

        if (calls.Count == 0 || !hasReasoningArtifact)
        {
            return false;
        }

        var outputs = new List<ToolReplayOutput>();
        while (cursor < input.Count)
        {
            var item = input[cursor];
            if (item.ValueKind != JsonValueKind.Object
                || !string.Equals(ReadString(item, "type"), "function_call_output", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            outputs.Add(new ToolReplayOutput(ReadString(item, "call_id"), ReadToolOutput(item)));
            cursor++;
        }

        if (outputs.Count == 0)
        {
            return false;
        }

        message = BuildTextMessage("user", BuildFlattenedToolResultText(calls, outputs));
        nextIndex = cursor;
        return true;
    }

    private static string BuildFlattenedToolResultText(
        IReadOnlyList<ToolReplayCall> calls,
        IReadOnlyList<ToolReplayOutput> outputs)
    {
        var outputsByCallId = outputs
            .Where(static output => !string.IsNullOrWhiteSpace(output.CallId))
            .GroupBy(static output => output.CallId!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last().Output, StringComparer.Ordinal);
        var unmatchedOutputs = new Queue<ToolReplayOutput>(outputs.Where(static output => string.IsNullOrWhiteSpace(output.CallId)));
        var builder = new StringBuilder();
        builder.AppendLine("工具执行结果如下，请基于这些结果继续完成用户请求。");

        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i];
            var output = ResolveFlattenedToolOutput(call, outputsByCallId, unmatchedOutputs);
            builder.Append(i + 1).Append(". 工具：").Append(string.IsNullOrWhiteSpace(call.Name) ? "unknown" : call.Name).AppendLine();
            if (!string.IsNullOrWhiteSpace(call.Arguments))
            {
                builder.Append("参数：").AppendLine(call.Arguments);
            }

            builder.AppendLine("输出：");
            builder.AppendLine(output);
        }

        while (unmatchedOutputs.Count > 0)
        {
            var output = unmatchedOutputs.Dequeue();
            builder.Append(calls.Count + 1).Append(". 未匹配工具输出：").AppendLine();
            builder.AppendLine(output.Output);
        }

        return builder.ToString().TrimEnd();
    }

    private static string ResolveFlattenedToolOutput(
        ToolReplayCall call,
        Dictionary<string, string> outputsByCallId,
        Queue<ToolReplayOutput> unmatchedOutputs)
    {
        if (!string.IsNullOrWhiteSpace(call.CallId)
            && outputsByCallId.Remove(call.CallId!, out var matchedOutput))
        {
            return matchedOutput;
        }

        return unmatchedOutputs.Count > 0
            ? unmatchedOutputs.Dequeue().Output
            : string.Empty;
    }

    private static IReadOnlyList<Dictionary<string, object?>> BuildContentBlocks(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return [];
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            return string.IsNullOrWhiteSpace(text) ? [] : [BuildTextBlock(text!)];
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var blocks = new List<Dictionary<string, object?>>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var text = ReadString(part, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                blocks.Add(BuildTextBlock(text!));
            }
        }

        return blocks;
    }

    private static Dictionary<string, object?> BuildTextBlock(string text)
        => new(StringComparer.Ordinal)
        {
            ["type"] = "text",
            ["text"] = text,
        };

    private static string NormalizeRole(string? role)
        => role?.Trim().ToLowerInvariant() switch
        {
            "assistant" => "assistant",
            "developer" or "system" => "system",
            _ => "user",
        };

    private static object? DeserializeJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = json,
            };
        }
    }

    private static string ReadToolOutput(JsonElement item)
    {
        if (!item.TryGetProperty("output", out var output))
        {
            return string.Empty;
        }

        return output.ValueKind == JsonValueKind.String
            ? output.GetString() ?? string.Empty
            : output.GetRawText();
    }

    private static IReadOnlyList<object?> CloneJsonElements(IReadOnlyList<JsonElement> elements)
        => elements
            .Select(static element => JsonSerializer.Deserialize<object?>(element.GetRawText()))
            .ToArray();

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record ToolReplayCall(string? CallId, string? Name, string Arguments);

    private sealed record ToolReplayOutput(string? CallId, string Output);
}
