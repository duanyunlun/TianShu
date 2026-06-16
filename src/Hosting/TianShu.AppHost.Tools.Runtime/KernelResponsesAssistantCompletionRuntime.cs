using System.Text;
using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Responses assistant completion 运行时，负责无工具调用时的 assistant 文本、计划提取和空响应修复。
/// Runtime that resolves assistant completion text, plan extraction, and empty response repair when no tool call is present.
/// </summary>
internal sealed class KernelResponsesAssistantCompletionRuntime
{
    private readonly Func<string?, string?> normalize;
    private readonly Func<string?, KernelProposedPlanExtraction> extractProposedPlanText;
    private readonly Func<string, string, string, object> createResponsesMessage;
    private readonly Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync;

    public KernelResponsesAssistantCompletionRuntime(
        Func<string?, string?> normalize,
        Func<string?, KernelProposedPlanExtraction> extractProposedPlanText,
        Func<string, string, string, object> createResponsesMessage,
        Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync)
    {
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
        this.extractProposedPlanText = extractProposedPlanText ?? throw new ArgumentNullException(nameof(extractProposedPlanText));
        this.createResponsesMessage = createResponsesMessage ?? throw new ArgumentNullException(nameof(createResponsesMessage));
        this.persistTurnLogAsync = persistTurnLogAsync ?? throw new ArgumentNullException(nameof(persistTurnLogAsync));
    }

    public async Task<KernelResponsesAssistantCompletionDecision> EvaluateNoToolCallAsync(
        TurnOperationState state,
        TurnRequestContext context,
        IReadOnlyList<JsonElement> outputItemsDone,
        string outputTextDeltas,
        IReadOnlyList<object> requestInput,
        int requestSequence,
        string model,
        int emptyAssistantRepairAttempts,
        CancellationToken cancellationToken)
    {
        var assistant = Normalize(ExtractAssistantTextFromOutputItems(outputItemsDone))
                        ?? Normalize(outputTextDeltas);
        if (state.IsPlanMode)
        {
            var extractedPlan = extractProposedPlanText(assistant);
            assistant = extractedPlan.VisibleText;
            if (string.IsNullOrWhiteSpace(state.PlanText))
            {
                state.PlanText = extractedPlan.PlanText;
            }
        }

        if (!string.IsNullOrWhiteSpace(assistant))
        {
            return KernelResponsesAssistantCompletionDecision.Complete(assistant, emptyAssistantRepairAttempts);
        }

        if (ContainsImageGenerationCall(outputItemsDone)
            || (state.IsPlanMode && !string.IsNullOrWhiteSpace(state.PlanText)))
        {
            return KernelResponsesAssistantCompletionDecision.Complete(string.Empty, emptyAssistantRepairAttempts);
        }

        if (emptyAssistantRepairAttempts == 0)
        {
            emptyAssistantRepairAttempts++;
            await persistTurnLogAsync(
                state.ThreadId,
                state.TurnId,
                "turn.responses.empty_assistant_repair",
                "inProgress",
                $"empty assistant repair #{emptyAssistantRepairAttempts}",
                new
                {
                    threadId = state.ThreadId,
                    turnId = state.TurnId,
                    requestSequence,
                    model,
                    provider = context.ModelProvider,
                    providerWireApi = context.ProviderWireApi,
                },
                cancellationToken).ConfigureAwait(false);

            return KernelResponsesAssistantCompletionDecision.Repair(
                BuildEmptyAssistantRepairInput(requestInput),
                emptyAssistantRepairAttempts);
        }

        throw new InvalidOperationException("模型返回成功，但响应中未提取到 assistant 文本。");
    }

    private string? ExtractAssistantTextFromOutputItems(IEnumerable<JsonElement> outputItemsDone)
    {
        var builder = new StringBuilder();
        foreach (var item in outputItemsDone)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = Normalize(ReadString(item, "type"));
            if (!string.Equals(type, "message", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var role = Normalize(ReadString(item, "role"));
            if (!string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                var contentType = Normalize(ReadString(contentItem, "type"));
                if (!string.Equals(contentType, "output_text", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = ReadString(contentItem, "text");
                if (!string.IsNullOrEmpty(text))
                {
                    builder.Append(text);
                }
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private bool ContainsImageGenerationCall(IEnumerable<JsonElement> outputItemsDone)
    {
        foreach (var item in outputItemsDone)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = Normalize(ReadString(item, "type"));
            if (string.Equals(type, "image_generation_call", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private List<object> BuildEmptyAssistantRepairInput(IReadOnlyList<object> requestInput)
    {
        var updatedInput = new List<object>(requestInput.Count + 1);
        updatedInput.AddRange(requestInput);
        updatedInput.Add(createResponsesMessage(
            "user",
            "input_text",
            "上一轮模型响应已经成功结束，但没有提供可展示的 assistant 文本，也没有发起新的工具调用。请基于当前任务和已有上下文继续执行：如果还需要工具，请发起工具调用；否则请用中文输出可展示的进展或最终结果。"));
        return updatedInput;
    }

    private string? Normalize(string? value)
        => normalize(value);

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

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };
    }
}

internal enum KernelResponsesAssistantCompletionDecisionKind
{
    Complete,
    Repair,
}

internal sealed record KernelResponsesAssistantCompletionDecision(
    KernelResponsesAssistantCompletionDecisionKind Kind,
    string AssistantText,
    IReadOnlyList<object>? RepairRequestInput,
    int EmptyAssistantRepairAttempts)
{
    public static KernelResponsesAssistantCompletionDecision Complete(string assistantText, int emptyAssistantRepairAttempts)
        => new(
            KernelResponsesAssistantCompletionDecisionKind.Complete,
            assistantText,
            null,
            emptyAssistantRepairAttempts);

    public static KernelResponsesAssistantCompletionDecision Repair(
        IReadOnlyList<object> repairRequestInput,
        int emptyAssistantRepairAttempts)
        => new(
            KernelResponsesAssistantCompletionDecisionKind.Repair,
            string.Empty,
            repairRequestInput,
            emptyAssistantRepairAttempts);
}
