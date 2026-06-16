using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn assistant execution 运行时，负责本轮 assistant 的执行路径选择与非流式 plan-mode 改写。
/// Runtime that chooses and runs the assistant execution path for a turn, including non-streamed plan-mode rewriting.
/// </summary>
internal sealed class KernelTurnAssistantExecutionRuntime
{
    private readonly Func<string, string, KernelReadinessFlag, string, JsonElement, TurnRequestContext, CancellationToken, Task<string>> executeInlineToolCallAsync;
    private readonly Func<TurnOperationState, TurnRequestContext, CancellationToken, Task<(string AssistantText, bool Streamed)>> executeAssistantFromProviderAsync;
    private readonly Func<string?, KernelProposedPlanExtraction> extractProposedPlanText;

    public KernelTurnAssistantExecutionRuntime(
        Func<string, string, KernelReadinessFlag, string, JsonElement, TurnRequestContext, CancellationToken, Task<string>> executeInlineToolCallAsync,
        Func<TurnOperationState, TurnRequestContext, CancellationToken, Task<(string AssistantText, bool Streamed)>> executeAssistantFromProviderAsync,
        Func<string?, KernelProposedPlanExtraction> extractProposedPlanText)
    {
        this.executeInlineToolCallAsync = executeInlineToolCallAsync ?? throw new ArgumentNullException(nameof(executeInlineToolCallAsync));
        this.executeAssistantFromProviderAsync = executeAssistantFromProviderAsync ?? throw new ArgumentNullException(nameof(executeAssistantFromProviderAsync));
        this.extractProposedPlanText = extractProposedPlanText ?? throw new ArgumentNullException(nameof(extractProposedPlanText));
    }

    public async Task ExecuteAsync(
        TurnOperationState state,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        if (KernelToolRuntimeParsingHelpers.TryParseInlineToolCall(state.EffectiveUserText, out var inlineToolName, out var inlineToolArgs))
        {
            state.AssistantText = await executeInlineToolCallAsync(
                state.ThreadId,
                state.TurnId,
                state.ToolCallGate,
                inlineToolName,
                inlineToolArgs,
                context,
                cancellationToken).ConfigureAwait(false);
            state.AssistantTextStreamed = false;
        }
        else
        {
            var (assistantText, streamed) = await executeAssistantFromProviderAsync(
                state,
                context,
                cancellationToken).ConfigureAwait(false);
            state.AssistantText = assistantText;
            state.AssistantTextStreamed = streamed;
        }

        if (state.IsPlanMode && !state.AssistantTextStreamed)
        {
            var extractedPlan = extractProposedPlanText(state.AssistantText);
            state.AssistantText = !string.IsNullOrWhiteSpace(extractedPlan.VisibleText) || string.IsNullOrWhiteSpace(extractedPlan.PlanText)
                ? extractedPlan.VisibleText
                : string.Empty;
            state.PlanText = extractedPlan.PlanText;
        }
    }
}
