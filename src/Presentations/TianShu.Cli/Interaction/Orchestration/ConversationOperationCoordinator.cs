using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction.Orchestration;

/// <summary>
/// Coordinates asynchronous conversation turn/follow-up operations.
/// 编排异步 conversation turn / follow-up 操作。
/// </summary>
internal sealed class ConversationOperationCoordinator
{
    public void Start(
        ConversationOperationCoordinatorContext context,
        string label,
        Func<Task<ControlPlaneTurnSubmissionResult>> action,
        CancellationToken cancellationToken,
        Action<ControlPlaneTurnSubmissionResult>? onResult = null,
        Action? onCancelled = null,
        Action<Exception>? onException = null,
        Action? onFinally = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        context.TouchConversationActivity();
        context.RecordSyntheticEvent(
            "CliConversationRequested",
            context.CurrentSessionThreadId,
            context.LastObservedTurnId,
            $"{label} 已提交。",
            text: null,
            status: null);
        if (context.ConversationActivity.StartOperation())
        {
            context.StartWorkingDockTimer();
        }

        _ = Task.Run(
            () => ExecuteAsync(context, label, action, cancellationToken, onResult, onCancelled, onException, onFinally),
            cancellationToken);
    }

    internal async Task ExecuteAsync(
        ConversationOperationCoordinatorContext context,
        string label,
        Func<Task<ControlPlaneTurnSubmissionResult>> action,
        CancellationToken cancellationToken,
        Action<ControlPlaneTurnSubmissionResult>? onResult = null,
        Action? onCancelled = null,
        Action<Exception>? onException = null,
        Action? onFinally = null)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            await context.RefreshSessionSnapshotAsync(cancellationToken).ConfigureAwait(false);
            context.TouchConversationActivity();
            context.ApplyTurnResult(result.TurnId?.Value, result.TurnStatus);
            using (context.BeginControlOutputScope?.Invoke())
            {
                if (!result.Accepted)
                {
                    HandleRejectedResult(context, label, result);
                }
                else if (!string.IsNullOrWhiteSpace(result.TurnStatus))
                {
                    HandleAcceptedResult(context, label, result);
                }

                onResult?.Invoke(result);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            onCancelled?.Invoke();
        }
        catch (Exception ex)
        {
            context.TouchConversationActivity();
            context.RecordSyntheticEvent(
                "CliConversationException",
                context.CurrentSessionThreadId,
                context.LastObservedTurnId,
                ex.Message,
                text: null,
                status: null);
            using (context.BeginControlOutputScope?.Invoke())
            {
                CloseAssistantLineIfOpen(context);
                context.WriteErrorLineOnce($"{label} 执行失败：{ex.Message}");
                onException?.Invoke(ex);
            }
        }
        finally
        {
            if (context.ConversationActivity.CompleteOperation())
            {
                context.StopWorkingDockTimer();
            }

            context.RefreshInlineTailPrompt();
            onFinally?.Invoke();
        }
    }

    private static void HandleRejectedResult(
        ConversationOperationCoordinatorContext context,
        string label,
        ControlPlaneTurnSubmissionResult result)
    {
        if (context.TryConsumeUserRequestedInterrupt(result.TurnId?.Value, result.TurnStatus))
        {
            var correlationSuffix = BuildCorrelationSuffix(result.CorrelationId);
            context.RecordSyntheticEvent(
                "CliConversationInterrupted",
                context.CurrentSessionThreadId,
                result.TurnId?.Value,
                result.Message,
                text: null,
                status: result.TurnStatus);
            context.WriteVerboseOrImportant(
                $"{label} 已按请求中断：status={result.TurnStatus ?? "interrupted"}{correlationSuffix}",
                false);
            return;
        }

        if (IsExpectedPendingFollowUpMutationRejection(label, result))
        {
            var promoted = IsPromotedPendingFollowUpRejection(result);
            context.RecordSyntheticEvent(
                promoted ? "CliPendingFollowUpPromoted" : "CliPendingFollowUpDropped",
                context.CurrentSessionThreadId,
                result.TurnId?.Value,
                result.Message,
                text: null,
                status: result.TurnStatus);
            context.WriteVerboseOrImportant(
                promoted
                    ? "follow-up 原排队调度已结束：待发送项已转为引导。"
                    : "follow-up 原排队调度已结束：待发送项已删除。",
                false);
            return;
        }

        context.RecordSyntheticEvent(
            "CliConversationFailed",
            context.CurrentSessionThreadId,
            result.TurnId?.Value,
            result.Message,
            text: null,
            status: result.TurnStatus);
        CloseAssistantLineIfOpen(context);
        context.WriteErrorLineOnce($"{label} 执行失败：{result.Message}");
    }

    private static void HandleAcceptedResult(
        ConversationOperationCoordinatorContext context,
        string label,
        ControlPlaneTurnSubmissionResult result)
    {
        var isTerminal = context.IsTerminalTurnStatus(result.TurnStatus);
        var correlationSuffix = BuildCorrelationSuffix(result.CorrelationId);
        context.RecordSyntheticEvent(
            isTerminal ? "CliConversationCompleted" : "CliConversationAccepted",
            context.CurrentSessionThreadId,
            result.TurnId?.Value,
            isTerminal ? $"{label} 完成。" : $"{label} 已接受，等待回合继续。",
            text: null,
            status: result.TurnStatus);
        if (isTerminal)
        {
            context.ConversationActivity.MarkTerminalTurn();
        }

        if (context.IsFailedTurnStatus(result.TurnStatus))
        {
            context.MarkFailure();
        }

        context.WriteVerboseOrImportant(
            isTerminal
                ? $"{label} 完成：status={result.TurnStatus}{correlationSuffix}"
                : $"{label} 已接受：status={result.TurnStatus}{correlationSuffix}",
            false);
    }

    private static void CloseAssistantLineIfOpen(ConversationOperationCoordinatorContext context)
    {
        if (!context.AssistantLineOpen)
        {
            return;
        }

        context.WriteLine(string.Empty);
        context.AssistantLineOpen = false;
    }

    private static string BuildCorrelationSuffix(string? correlationId)
        => string.IsNullOrWhiteSpace(correlationId)
            ? string.Empty
            : $", correlation={correlationId}";

    private static bool IsExpectedPendingFollowUpMutationRejection(string label, ControlPlaneTurnSubmissionResult result)
        => IsDroppedPendingFollowUpRejection(label, result)
           || (string.Equals(label, "follow-up", StringComparison.OrdinalIgnoreCase)
               && !result.Accepted
               && IsPromotedPendingFollowUpRejection(result));

    private static bool IsDroppedPendingFollowUpRejection(string label, ControlPlaneTurnSubmissionResult result)
        => string.Equals(label, "follow-up", StringComparison.OrdinalIgnoreCase)
           && !result.Accepted
           && (result.Message.Contains("删除", StringComparison.Ordinal)
               || result.Message.Contains("deleted", StringComparison.OrdinalIgnoreCase));

    private static bool IsPromotedPendingFollowUpRejection(ControlPlaneTurnSubmissionResult result)
        => result.Message.Contains("转换为引导", StringComparison.Ordinal)
           || result.Message.Contains("转为引导", StringComparison.Ordinal)
           || result.Message.Contains("promoted", StringComparison.OrdinalIgnoreCase);
}

internal sealed class ConversationOperationCoordinatorContext
{
    public required ConversationActivityTracker ConversationActivity { get; init; }

    public required string? CurrentSessionThreadId { get; init; }

    public required string? LastObservedTurnId { get; init; }

    public required Action TouchConversationActivity { get; init; }

    public required ConversationOperationSyntheticEventRecorder RecordSyntheticEvent { get; init; }

    public required Action StartWorkingDockTimer { get; init; }

    public required Action StopWorkingDockTimer { get; init; }

    public required Func<CancellationToken, Task> RefreshSessionSnapshotAsync { get; init; }

    public required Action<string?, string?> ApplyTurnResult { get; init; }

    public required Func<string?, string?, bool> TryConsumeUserRequestedInterrupt { get; init; }

    public required Action<string, bool> WriteVerboseOrImportant { get; init; }

    public required Func<string?, bool> IsTerminalTurnStatus { get; init; }

    public required Func<string?, bool> IsFailedTurnStatus { get; init; }

    public required Action MarkFailure { get; init; }

    public required Action<string> WriteLine { get; init; }

    public required Action<string> WriteErrorLineOnce { get; init; }

    public Func<IDisposable>? BeginControlOutputScope { get; init; }

    public required Action RefreshInlineTailPrompt { get; init; }

    public required Func<bool> GetAssistantLineOpen { get; init; }

    public required Action<bool> SetAssistantLineOpen { get; init; }

    public bool AssistantLineOpen
    {
        get => GetAssistantLineOpen();
        set => SetAssistantLineOpen(value);
    }
}

internal delegate void ConversationOperationSyntheticEventRecorder(
    string kind,
    string? threadId,
    string? turnId,
    string? message,
    string? text = null,
    string? status = null);
