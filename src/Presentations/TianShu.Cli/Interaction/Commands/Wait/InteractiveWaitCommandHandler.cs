using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Commands.Wait;

/// <summary>
/// Handles interactive wait commands that observe runtime events and idle state.
/// 处理观察 runtime 事件与 idle 状态的交互式等待命令。
/// </summary>
internal sealed class InteractiveWaitCommandHandler
{
    public async Task HandleWaitAsync(
        string rest,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeLine);

        if (!int.TryParse(Normalize(rest) ?? "500", out var milliseconds) || milliseconds < 0)
        {
            writeLine("用法：/wait [milliseconds]", true);
            return;
        }

        await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleWaitEventAsync(
        ConversationEventWaiter waiter,
        string rest,
        Func<ControlPlaneConversationStreamEvent, string> formatEvent,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(waiter);
        ArgumentNullException.ThrowIfNull(formatEvent);
        ArgumentNullException.ThrowIfNull(writeLine);

        var eventToken = ReadFirstToken(rest, out var timeoutToken);
        if (!TryParseEventKindToken(eventToken, out var eventKind))
        {
            writeLine("用法：/wait-event <event-kind> [timeout-seconds]", true);
            return;
        }

        var timeout = ParseWaitTimeoutSeconds(timeoutToken, defaultSeconds: 300);
        if (timeout is null)
        {
            writeLine("用法：/wait-event <event-kind> [timeout-seconds]", true);
            return;
        }

        var matchedEvent = await waiter.WaitForEventAsync(
                streamEvent => streamEvent.Kind == eventKind,
                timeout.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (matchedEvent is null)
        {
            writeLine($"等待事件超时：{eventKind}", true);
            return;
        }

        writeLine($"已等到事件：{formatEvent(matchedEvent)}", false);
    }

    public Task HandleWaitNextToolCallAsync(
        ConversationEventWaiter waiter,
        string rest,
        Func<ControlPlaneConversationStreamEvent, string> formatEvent,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(waiter);
        ArgumentNullException.ThrowIfNull(formatEvent);
        ArgumentNullException.ThrowIfNull(writeLine);

        var timeout = ParseWaitTimeoutSeconds(rest, defaultSeconds: 300);
        if (timeout is null)
        {
            writeLine("用法：/wait-next-tool-call [timeout-seconds]", true);
            return Task.CompletedTask;
        }

        return HandleWaitForSpecificEventAsync(
            waiter,
            streamEvent => streamEvent.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted
                           && !string.Equals(ReadToolCallPhase(streamEvent), "request_approval", StringComparison.OrdinalIgnoreCase),
            "ToolCallStarted",
            timeout.Value,
            formatEvent,
            writeLine,
            cancellationToken);
    }

    public async Task HandleWaitCompleteAsync(
        ConversationEventWaiter waiter,
        string rest,
        Func<CancellationToken, Task> refreshSnapshotAsync,
        Func<bool> isIdle,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(waiter);
        ArgumentNullException.ThrowIfNull(refreshSnapshotAsync);
        ArgumentNullException.ThrowIfNull(isIdle);
        ArgumentNullException.ThrowIfNull(writeLine);

        var timeout = ParseWaitTimeoutSeconds(rest, defaultSeconds: 300);
        if (timeout is null)
        {
            writeLine("用法：/wait-complete [timeout-seconds]", true);
            return;
        }

        var completed = await WaitForIdleAsync(waiter, refreshSnapshotAsync, isIdle, timeout.Value, cancellationToken).ConfigureAwait(false);
        writeLine(completed ? "当前没有运行中的回合。" : "等待回合结束超时。", !completed);
    }

    public async Task<bool> WaitForIdleAsync(
        ConversationEventWaiter waiter,
        Func<CancellationToken, Task> refreshSnapshotAsync,
        Func<bool> isIdle,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(waiter);
        ArgumentNullException.ThrowIfNull(refreshSnapshotAsync);
        ArgumentNullException.ThrowIfNull(isIdle);
        return await waiter.WaitForIdleAsync(refreshSnapshotAsync, isIdle, timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task HandleWaitForSpecificEventAsync(
        ConversationEventWaiter waiter,
        Func<ControlPlaneConversationStreamEvent, bool> predicate,
        string displayName,
        TimeSpan timeout,
        Func<ControlPlaneConversationStreamEvent, string> formatEvent,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        var matchedEvent = await waiter.WaitForEventAsync(predicate, timeout, cancellationToken).ConfigureAwait(false);
        if (matchedEvent is null)
        {
            writeLine($"等待事件超时：{displayName}", true);
            return;
        }

        writeLine($"已等到事件：{formatEvent(matchedEvent)}", false);
    }

    private static string ReadFirstToken(string text, out string remainder)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            remainder = string.Empty;
            return string.Empty;
        }

        var index = text.IndexOf(' ', StringComparison.Ordinal);
        if (index < 0)
        {
            remainder = string.Empty;
            return text;
        }

        remainder = text[(index + 1)..].Trim();
        return text[..index];
    }

    private static TimeSpan? ParseWaitTimeoutSeconds(string? rawValue, int defaultSeconds)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return TimeSpan.FromSeconds(defaultSeconds);
        }

        return int.TryParse(rawValue.Trim(), out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static bool TryParseEventKindToken(string? token, out ControlPlaneConversationStreamEventKind eventKind)
    {
        eventKind = default;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalizedToken = NormalizeEventKindToken(token);
        foreach (ControlPlaneConversationStreamEventKind candidate in Enum.GetValues<ControlPlaneConversationStreamEventKind>())
        {
            if (!string.Equals(NormalizeEventKindToken(candidate.ToString()), normalizedToken, StringComparison.Ordinal))
            {
                continue;
            }

            eventKind = candidate;
            return true;
        }

        return false;
    }

    private static string NormalizeEventKindToken(string value)
        => value.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadToolCallPhase(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (streamEvent.PayloadKind != ControlPlaneConversationStreamPayloadKind.ToolCall
            || streamEvent.Payload is null
            || streamEvent.Payload.Kind != StructuredValueKind.Object)
        {
            return null;
        }

        if (!TryReadObjectPropertyIgnoreCase(streamEvent.Payload, "phase", out var phaseValue))
        {
            return null;
        }

        return phaseValue.Kind == StructuredValueKind.String
            ? phaseValue.StringValue
            : null;
    }

    private static bool TryReadObjectPropertyIgnoreCase(StructuredValue value, string propertyName, out StructuredValue propertyValue)
    {
        if (value.Kind != StructuredValueKind.Object)
        {
            propertyValue = StructuredValue.Null;
            return false;
        }

        if (value.Properties.TryGetValue(propertyName, out var directMatch)
            && directMatch is not null)
        {
            propertyValue = directMatch;
            return true;
        }

        foreach (var (candidateName, candidateValue) in value.Properties)
        {
            if (!string.Equals(candidateName, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            propertyValue = candidateValue;
            return true;
        }

        propertyValue = StructuredValue.Null;
        return false;
    }
}
