using System.Text;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Responses stream processing 运行时，负责 provider stream event 解析、累积和结果归一。
/// Runtime that parses provider stream events, accumulates deltas, and normalizes stream results.
/// </summary>
internal sealed class KernelResponsesStreamProcessingRuntime
{
    private readonly KernelResponsesStreamNotificationRuntime responsesStreamNotificationRuntime;
    private readonly KernelResponsesStreamFailureRuntime responsesStreamFailureRuntime;

    public KernelResponsesStreamProcessingRuntime(
        KernelResponsesStreamNotificationRuntime responsesStreamNotificationRuntime,
        KernelResponsesStreamFailureRuntime responsesStreamFailureRuntime)
    {
        this.responsesStreamNotificationRuntime = responsesStreamNotificationRuntime ?? throw new ArgumentNullException(nameof(responsesStreamNotificationRuntime));
        this.responsesStreamFailureRuntime = responsesStreamFailureRuntime ?? throw new ArgumentNullException(nameof(responsesStreamFailureRuntime));
    }

    public async Task<ResponsesStreamResult> ProcessAsync(
        IAsyncEnumerable<string> events,
        TurnOperationState state,
        IProviderResponsesStreamChunkParser streamChunkParser,
        CancellationToken cancellationToken)
    {
        var outputItemsAdded = new List<JsonElement>();
        var outputItemsDone = new List<JsonElement>();
        var outputTextDeltas = new StringBuilder();
        var responseId = string.Empty;
        var anthropicMessageId = string.Empty;
        var chatToolCallAccumulators = new Dictionary<int, ChatCompletionsToolCallAccumulator>();
        var chatReasoningContent = new StringBuilder();
        var anthropicToolUseBlocks = new Dictionary<int, ProviderResponsesToolUseBlockAccumulator>();
        var anthropicThinkingBlockAccumulators = new Dictionary<int, ProviderResponsesThinkingBlockAccumulator>();
        var anthropicThinkingBlocks = new List<JsonElement>();
        var planParser = state.IsPlanMode
            ? state.ProposedPlanParser ??= new KernelProposedPlanStreamParser()
            : null;

        await foreach (var data in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(data.Trim(), "[DONE]", StringComparison.Ordinal))
            {
                responseId = "chat.completions.done";
                break;
            }

            using var doc = ParseEvent(data);
            var root = doc.RootElement;

            if (TryReadChatCompletionsDelta(
                root,
                out var chatDelta,
                out var chatReasoningDelta,
                out var chatToolCallDeltas,
                out var chatCompleted))
            {
                if (!string.IsNullOrEmpty(chatReasoningDelta))
                {
                    chatReasoningContent.Append(chatReasoningDelta);
                    await responsesStreamNotificationRuntime.EmitProviderReasoningDeltaAsync(state, chatReasoningDelta).ConfigureAwait(false);
                }

                foreach (var toolCallDelta in chatToolCallDeltas)
                {
                    var index = ReadInt(toolCallDelta, "index") ?? 0;
                    if (!chatToolCallAccumulators.TryGetValue(index, out var toolCall))
                    {
                        toolCall = new ChatCompletionsToolCallAccumulator();
                        chatToolCallAccumulators[index] = toolCall;
                    }

                    toolCall.Append(toolCallDelta);
                }

                if (!string.IsNullOrEmpty(chatDelta))
                {
                    var emittedDelta = await responsesStreamNotificationRuntime.EmitAssistantDeltaAsync(state, chatDelta).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(emittedDelta))
                    {
                        outputTextDeltas.Append(emittedDelta);
                    }
                }

                if (chatCompleted)
                {
                    foreach (var toolCall in chatToolCallAccumulators.OrderBy(static pair => pair.Key).Select(static pair => pair.Value))
                    {
                        var item = toolCall.ToFunctionCallJsonElement(chatReasoningContent.ToString());
                        if (item is not null)
                        {
                            outputItemsDone.Add(item.Value);
                        }
                    }

                    chatToolCallAccumulators.Clear();
                    chatReasoningContent.Clear();
                    responseId = Normalize(ReadString(root, "id")) ?? "chat.completions.done";
                }

                if (!string.IsNullOrWhiteSpace(responseId))
                {
                    break;
                }

                continue;
            }

            try
            {
                if (streamChunkParser.TryReadChunk(root, out var providerChunk))
                {
                    if (!string.IsNullOrEmpty(providerChunk.TextDelta))
                    {
                        var emittedDelta = await responsesStreamNotificationRuntime.EmitAssistantDeltaAsync(state, providerChunk.TextDelta).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(emittedDelta))
                        {
                            outputTextDeltas.Append(emittedDelta);
                        }
                    }

                    outputItemsDone.AddRange(providerChunk.FunctionCalls);
                    if (providerChunk.Completed)
                    {
                        responseId = Normalize(ReadString(root, "responseId")) ?? "provider.stream.done";
                    }

                    if (!string.IsNullOrWhiteSpace(responseId))
                    {
                        break;
                    }

                    continue;
                }
            }
            catch (ProviderResponsesStreamParseException ex)
            {
                throw new KernelResponsesStreamException(ex.Message, ex.IsRetryable);
            }

            var kind = Normalize(ReadString(root, "type"));
            if (string.IsNullOrWhiteSpace(kind))
            {
                continue;
            }

            if (string.Equals(kind, "message_start", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                {
                    anthropicMessageId = Normalize(ReadString(message, "id")) ?? string.Empty;
                }

                continue;
            }

            if (string.Equals(kind, "content_block_start", StringComparison.OrdinalIgnoreCase))
            {
                var index = ReadInt(root, "index");
                if (index is not null
                    && root.TryGetProperty("content_block", out var contentBlock)
                    && contentBlock.ValueKind == JsonValueKind.Object)
                {
                    var contentBlockType = ReadString(contentBlock, "type");
                    if (string.Equals(contentBlockType, "thinking", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(contentBlockType, "redacted_thinking", StringComparison.OrdinalIgnoreCase))
                    {
                        anthropicThinkingBlockAccumulators[index.Value] = new ProviderResponsesThinkingBlockAccumulator(contentBlock);
                        continue;
                    }

                    if (!string.Equals(contentBlockType, "tool_use", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var toolId = ReadString(contentBlock, "id");
                    var name = ReadString(contentBlock, "name");
                    if (!string.IsNullOrWhiteSpace(toolId) && !string.IsNullOrWhiteSpace(name))
                    {
                        anthropicToolUseBlocks[index.Value] = new ProviderResponsesToolUseBlockAccumulator(toolId!, name!);
                    }
                }

                continue;
            }

            if (string.Equals(kind, "content_block_delta", StringComparison.OrdinalIgnoreCase))
            {
                var index = ReadInt(root, "index");
                if (!root.TryGetProperty("delta", out var deltaObject) || deltaObject.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var deltaType = ReadString(deltaObject, "type");
                if (string.Equals(deltaType, "text_delta", StringComparison.OrdinalIgnoreCase))
                {
                    var delta = ReadString(deltaObject, "text");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        var emittedDelta = await responsesStreamNotificationRuntime.EmitAssistantDeltaAsync(state, delta).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(emittedDelta))
                        {
                            outputTextDeltas.Append(emittedDelta);
                        }
                    }

                    continue;
                }

                if (string.Equals(deltaType, "thinking_delta", StringComparison.OrdinalIgnoreCase))
                {
                    var delta = ReadString(deltaObject, "thinking");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        if (index is not null
                            && anthropicThinkingBlockAccumulators.TryGetValue(index.Value, out var thinkingBlock))
                        {
                            thinkingBlock.AppendThinking(delta);
                        }

                        await responsesStreamNotificationRuntime.EmitProviderReasoningDeltaAsync(state, delta).ConfigureAwait(false);
                    }

                    continue;
                }

                if (index is not null
                    && string.Equals(deltaType, "signature_delta", StringComparison.OrdinalIgnoreCase)
                    && anthropicThinkingBlockAccumulators.TryGetValue(index.Value, out var signatureThinkingBlock))
                {
                    signatureThinkingBlock.AppendSignature(ReadString(deltaObject, "signature"));
                    continue;
                }

                if (index is not null
                    && string.Equals(deltaType, "input_json_delta", StringComparison.OrdinalIgnoreCase)
                    && anthropicToolUseBlocks.TryGetValue(index.Value, out var toolUseBlock))
                {
                    toolUseBlock.AppendPartialJson(ReadString(deltaObject, "partial_json"));
                    continue;
                }

                continue;
            }

            if (string.Equals(kind, "content_block_stop", StringComparison.OrdinalIgnoreCase))
            {
                var index = ReadInt(root, "index");
                if (index is not null && anthropicThinkingBlockAccumulators.Remove(index.Value, out var completedThinkingBlock))
                {
                    anthropicThinkingBlocks.Add(completedThinkingBlock.ToJsonElement());
                    continue;
                }

                if (index is not null && anthropicToolUseBlocks.Remove(index.Value, out var toolUseBlock))
                {
                    outputItemsDone.Add(toolUseBlock.ToFunctionCallJsonElement(anthropicThinkingBlocks));
                    anthropicThinkingBlocks.Clear();
                }

                continue;
            }

            if (string.Equals(kind, "message_stop", StringComparison.OrdinalIgnoreCase))
            {
                responseId = string.IsNullOrWhiteSpace(anthropicMessageId)
                    ? "anthropic.messages.done"
                    : anthropicMessageId;
                break;
            }

            if (string.Equals(kind, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw responsesStreamFailureRuntime.CreateStreamException(kind, root);
            }

            switch (kind)
            {
                case "response.output_text.delta":
                    {
                        var delta = ReadString(root, "delta");
                        if (!string.IsNullOrEmpty(delta))
                        {
                            if (planParser is null)
                            {
                                var emittedDelta = await responsesStreamNotificationRuntime.EmitAssistantDeltaAsync(state, delta).ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(emittedDelta))
                                {
                                    outputTextDeltas.Append(emittedDelta);
                                }
                            }
                            else
                            {
                                foreach (var segment in planParser.Append(delta))
                                {
                                    if (segment.IsPlan)
                                    {
                                        await responsesStreamNotificationRuntime.EmitPlanDeltaAsync(state, segment.Text).ConfigureAwait(false);
                                        continue;
                                    }

                                    var emittedDelta = await responsesStreamNotificationRuntime.EmitAssistantDeltaAsync(state, segment.Text).ConfigureAwait(false);
                                    if (string.IsNullOrEmpty(emittedDelta))
                                    {
                                        continue;
                                    }

                                    outputTextDeltas.Append(emittedDelta);
                                }
                            }
                        }

                        break;
                    }

                case "response.reasoning_summary_part.added":
                    {
                        var summaryIndex = ReadInt(root, "summary_index");
                        if (summaryIndex is not null)
                        {
                            await responsesStreamNotificationRuntime.EmitReasoningSummaryPartAddedAsync(state, summaryIndex.Value).ConfigureAwait(false);
                        }

                        break;
                    }

                case "response.reasoning_summary_text.delta":
                    {
                        var delta = ReadString(root, "delta");
                        var summaryIndex = ReadInt(root, "summary_index");
                        if (!string.IsNullOrEmpty(delta) && summaryIndex is not null)
                        {
                            await responsesStreamNotificationRuntime.EmitReasoningSummaryTextDeltaAsync(state, delta, summaryIndex.Value).ConfigureAwait(false);
                        }

                        break;
                    }

                case "response.reasoning_text.delta":
                    {
                        var delta = ReadString(root, "delta");
                        var contentIndex = ReadInt(root, "content_index");
                        if (!string.IsNullOrEmpty(delta) && contentIndex is not null)
                        {
                            await responsesStreamNotificationRuntime.EmitReasoningTextDeltaAsync(state, delta, contentIndex.Value).ConfigureAwait(false);
                        }

                        break;
                    }

                case "response.output_item.added":
                    {
                        if (root.TryGetProperty("item", out var itemVal) && itemVal.ValueKind == JsonValueKind.Object)
                        {
                            outputItemsAdded.Add(itemVal.Clone());
                            await responsesStreamNotificationRuntime.EmitPresentableOutputItemNotificationAsync(
                                    state,
                                    "item/started",
                                    itemVal,
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                        }

                        break;
                    }

                case "response.output_item.done":
                    {
                        if (root.TryGetProperty("item", out var itemVal) && itemVal.ValueKind == JsonValueKind.Object)
                        {
                            outputItemsDone.Add(itemVal.Clone());
                            await responsesStreamNotificationRuntime.EmitPresentableOutputItemNotificationAsync(
                                    state,
                                    "item/completed",
                                    itemVal,
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                        }

                        break;
                    }

                case "response.completed":
                    {
                        if (root.TryGetProperty("response", out var responseVal) && responseVal.ValueKind == JsonValueKind.Object)
                        {
                            responseId = Normalize(ReadString(responseVal, "id")) ?? string.Empty;
                        }

                        break;
                    }

                case "response.failed":
                    throw responsesStreamFailureRuntime.CreateStreamException(kind, root);

                case "response.incomplete":
                    throw responsesStreamFailureRuntime.CreateStreamException(kind, root);
            }

            if (!string.IsNullOrWhiteSpace(responseId))
            {
                break;
            }
        }

        if (planParser is not null)
        {
            foreach (var segment in planParser.Flush())
            {
                if (segment.IsPlan)
                {
                    await responsesStreamNotificationRuntime.EmitPlanDeltaAsync(state, segment.Text).ConfigureAwait(false);
                    continue;
                }

                var emittedDelta = await responsesStreamNotificationRuntime.EmitAssistantDeltaAsync(state, segment.Text).ConfigureAwait(false);
                if (string.IsNullOrEmpty(emittedDelta))
                {
                    continue;
                }

                outputTextDeltas.Append(emittedDelta);
            }

            var parsed = planParser.Complete();
            if (string.IsNullOrWhiteSpace(state.PlanText))
            {
                state.PlanText = parsed.PlanText;
            }
        }

        if (string.IsNullOrWhiteSpace(responseId))
        {
            throw new KernelResponsesStreamException("stream closed before response.completed", isRetryable: true);
        }

        return new ResponsesStreamResult(responseId, outputItemsAdded, outputItemsDone, outputTextDeltas.ToString());
    }

    private static bool TryReadChatCompletionsDelta(
        JsonElement root,
        out string? delta,
        out string? reasoningDelta,
        out IReadOnlyList<JsonElement> toolCallDeltas,
        out bool completed)
    {
        delta = null;
        reasoningDelta = null;
        var toolCalls = new List<JsonElement>();
        completed = false;

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            toolCallDeltas = toolCalls;
            return false;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (choice.TryGetProperty("delta", out var deltaObject)
                && deltaObject.ValueKind == JsonValueKind.Object)
            {
                if (deltaObject.TryGetProperty("reasoning_content", out var reasoningContent)
                    && reasoningContent.ValueKind == JsonValueKind.String)
                {
                    reasoningDelta = reasoningContent.GetString();
                }
                else if (deltaObject.TryGetProperty("reasoning", out var reasoning)
                         && reasoning.ValueKind == JsonValueKind.String)
                {
                    reasoningDelta = reasoning.GetString();
                }

                if (deltaObject.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    delta = content.GetString();
                }

                if (deltaObject.TryGetProperty("tool_calls", out var toolCallsValue)
                    && toolCallsValue.ValueKind == JsonValueKind.Array)
                {
                    foreach (var toolCall in toolCallsValue.EnumerateArray())
                    {
                        if (toolCall.ValueKind == JsonValueKind.Object)
                        {
                            toolCalls.Add(toolCall.Clone());
                        }
                    }
                }
            }

            if (choice.TryGetProperty("finish_reason", out var finishReason)
                && finishReason.ValueKind != JsonValueKind.Null
                && finishReason.ValueKind != JsonValueKind.Undefined
                && !string.IsNullOrWhiteSpace(finishReason.ToString()))
            {
                completed = true;
            }
        }

        toolCallDeltas = toolCalls;
        return true;
    }

    private static JsonDocument ParseEvent(string data)
    {
        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException ex)
        {
            throw new KernelResponsesStreamException(
                $"responses stream emitted invalid JSON event: {ex.Message}; eventLength={data.Length}",
                isRetryable: true);
        }
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

    private static int? ReadInt(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out var value))
        {
            return value;
        }

        if (current.ValueKind == JsonValueKind.String && int.TryParse(current.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class ChatCompletionsToolCallAccumulator
    {
        private string? id;
        private string type = "function";
        private readonly StringBuilder name = new();
        private readonly StringBuilder arguments = new();

        public void Append(JsonElement delta)
        {
            id = ReadString(delta, "id") ?? id;
            type = ReadString(delta, "type") ?? type;
            if (!delta.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var nameDelta = ReadString(function, "name");
            if (!string.IsNullOrEmpty(nameDelta))
            {
                name.Append(nameDelta);
            }

            var argumentsDelta = ReadString(function, "arguments");
            if (!string.IsNullOrEmpty(argumentsDelta))
            {
                arguments.Append(argumentsDelta);
            }
        }

        public JsonElement? ToFunctionCallJsonElement(string? reasoningContent)
        {
            var callId = NormalizeText(id);
            var functionName = NormalizeText(name.ToString());
            if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(functionName))
            {
                return null;
            }

            var item = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "function_call",
                ["call_id"] = callId,
                ["name"] = functionName,
                ["arguments"] = NormalizeArguments(arguments.ToString()),
            };

            if (!string.IsNullOrWhiteSpace(reasoningContent))
            {
                item["reasoning_content"] = reasoningContent;
            }

            return JsonSerializer.SerializeToElement(item);
        }

        private static string NormalizeArguments(string value)
            => string.IsNullOrWhiteSpace(value) ? "{}" : value;

        private static string? NormalizeText(string? value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
    }
}
