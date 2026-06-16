namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn assistant output streaming 运行时，负责将非流式 assistant 输出投影为流式通知。
/// Runtime that projects non-streamed assistant output into stream-style notifications.
/// </summary>
internal sealed class KernelTurnAssistantOutputStreamingRuntime
{
    private readonly KernelResponsesStreamNotificationRuntime responsesStreamNotificationRuntime;

    public KernelTurnAssistantOutputStreamingRuntime(KernelResponsesStreamNotificationRuntime responsesStreamNotificationRuntime)
    {
        this.responsesStreamNotificationRuntime = responsesStreamNotificationRuntime ?? throw new ArgumentNullException(nameof(responsesStreamNotificationRuntime));
    }

    public async Task StreamAsync(
        TurnOperationState state,
        CancellationToken cancellationToken)
    {
        if (state.AssistantTextStreamed)
        {
            return;
        }

        var chunkIndex = 0;
        foreach (var chunk in SplitIntoChunks(state.AssistantText, 18))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (chunkIndex == 0)
            {
                await responsesStreamNotificationRuntime.EmitReasoningSummaryPartAddedAsync(state, 0).ConfigureAwait(false);
            }

            await responsesStreamNotificationRuntime.EmitReasoningTextDeltaAsync(state, chunk, chunkIndex).ConfigureAwait(false);

            if (chunkIndex < 3)
            {
                await responsesStreamNotificationRuntime.EmitReasoningSummaryTextDeltaAsync(state, chunk, 0).ConfigureAwait(false);
            }

            await responsesStreamNotificationRuntime.EmitAssistantDeltaAsync(state, chunk).ConfigureAwait(false);

            chunkIndex++;
            await Task.Delay(35, cancellationToken).ConfigureAwait(false);
        }

        if (state.IsPlanMode && !string.IsNullOrWhiteSpace(state.PlanText))
        {
            await responsesStreamNotificationRuntime.EmitPlanDeltaAsync(state, state.PlanText).ConfigureAwait(false);
        }
    }

    private static IEnumerable<string> SplitIntoChunks(string text, int chunkSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var size = Math.Clamp(chunkSize, 1, 128);
        for (var index = 0; index < text.Length; index += size)
        {
            var length = Math.Min(size, text.Length - index);
            yield return text.Substring(index, length);
        }
    }
}
