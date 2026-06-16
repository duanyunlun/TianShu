using System.Text.Json;
using TianShu.Cli.Interaction.Host;
using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Commands.FollowUp;

/// <summary>
/// Handles interactive follow-up commands and restored follow-up draft actions.
/// 处理交互式 follow-up 命令与恢复草稿动作。
/// </summary>
internal sealed class InteractiveFollowUpCommandHandler
{
    public Task HandleFollowUpAsync(
        InteractiveFollowUpCommandContext context,
        string rest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var modeToken = ReadFirstToken(rest, out var message);
        if (IsQueuedFollowUpMutationToken(modeToken))
        {
            return HandleQueuedFollowUpMutationAsync(context, modeToken, message, cancellationToken);
        }

        if (!Enum.TryParse<ControlPlaneFollowUpMode>(modeToken, ignoreCase: true, out var mode)
            || string.IsNullOrWhiteSpace(message))
        {
            context.WriteLine("用法：/follow-up <queue|steer|interrupt> <text>，或 /follow-up <promote|drop> <编号>", isError: true);
            return Task.CompletedTask;
        }

        var correlationId = Guid.NewGuid().ToString("N");
        if (mode == ControlPlaneFollowUpMode.Interrupt && context.HasRunningConversation())
        {
            context.RememberUserRequestedInterrupt(context.LastObservedTurnId);
        }

        var userInputs = CliConversationInputUtilities.BuildStructuredInputsFromText(message);
        context.WriteLine(BuildFollowUpSubmittedMessage(mode, context.HasRunningConversation()));
        if (mode == ControlPlaneFollowUpMode.Queue)
        {
            context.AddQueuedFollowUp(correlationId, message);
        }
        else
        {
            context.TrackPendingGuidance(correlationId, message);
        }

        context.StartConversationOperation(
            "follow-up",
            token => context.SubmitFollowUpAsync(userInputs, mode, token, correlationId),
            cancellationToken,
            onResult: result => WriteFollowUpResult(context, result, mode, correlationId));
        return Task.CompletedTask;
    }

    private static async Task HandleQueuedFollowUpMutationAsync(
        InteractiveFollowUpCommandContext context,
        string modeToken,
        string indexText,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(indexText, out var index) || index <= 0)
        {
            context.WriteLine("用法：/follow-up <promote|drop> <编号>", isError: true);
            return;
        }

        var entry = context.ResolveQueuedFollowUp(index);
        if (entry is null)
        {
            context.WriteLine($"未找到待发送 #{index}。", isError: true);
            return;
        }

        var mutationKind = IsDropQueuedFollowUpToken(modeToken)
            ? ControlPlanePendingFollowUpMutationKind.Drop
            : ControlPlanePendingFollowUpMutationKind.PromoteToSteer;
        var result = await context.MutatePendingFollowUpAsync(
                entry.CorrelationId,
                mutationKind,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Accepted)
        {
            context.WriteLine($"待发送 #{entry.Index} 处理失败：{result.Message}", isError: true, countAsFailure: false);
            return;
        }

        context.RemoveQueuedFollowUp(entry.CorrelationId);
        if (mutationKind == ControlPlanePendingFollowUpMutationKind.Drop)
        {
            context.WriteLine($"已删除待发送 #{entry.Index}。");
            return;
        }

        context.TrackPendingGuidance(entry.CorrelationId, entry.Preview);
        context.WriteLine($"已将待发送 #{entry.Index} 转为引导，等待进入当前回合上下文。");
    }

    public Task<bool> TryExecuteRunningFollowUpInputAsync(
        InteractiveFollowUpCommandContext context,
        string trimmed,
        ControlPlaneFollowUpMode? runningPlainTextMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.HasSteerableConversation())
        {
            return Task.FromResult(false);
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var followUpInputs = CliConversationInputUtilities.BuildStructuredInputsFromText(trimmed);
        var mode = runningPlainTextMode ?? ControlPlaneFollowUpMode.Steer;
        context.RecordSyntheticEvent(
            "CliPlainTextFollowUpSubmitted",
            context.CurrentSessionThreadId,
            context.LastObservedTurnId,
            $"运行中普通输入已作为 {mode.ToString().ToLowerInvariant()} follow-up 发送。",
            trimmed);
        if (mode == ControlPlaneFollowUpMode.Queue)
        {
            context.AddQueuedFollowUp(correlationId, trimmed);
        }
        else
        {
            context.WriteLine(BuildFollowUpSubmittedMessage(mode, running: true));
            context.TrackPendingGuidance(correlationId, trimmed);
        }

        context.StartConversationOperation(
            "follow-up",
            token => context.SubmitFollowUpAsync(followUpInputs, mode, token, correlationId),
            cancellationToken,
            onResult: result => WriteFollowUpResult(context, result, mode, correlationId));
        return Task.FromResult(true);
    }

    public Task<bool> TryExecuteRestoredDraftInputAsync(
        InteractiveFollowUpCommandContext context,
        string trimmed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var editedDraft = context.RestoredFollowUps.TakeEditedComposerDraft(trimmed);
        if (editedDraft is null)
        {
            return Task.FromResult(false);
        }

        context.WriteLine($"已用当前输入更新恢复草稿，并按 {editedDraft.Mode} follow-up 发送。");
        StartRestoredFollowUp(context, editedDraft, "resume-follow-up-edit", cancellationToken);
        return Task.FromResult(true);
    }

    public void HandleRestoredDraftState(InteractiveFollowUpCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = context.RestoredFollowUps.CaptureSnapshot();
        if (snapshot.ComposerDraft is null)
        {
            if (snapshot.PendingDispatch is not null)
            {
                context.WriteLine(JsonSerializer.Serialize(new
                {
                    mode = snapshot.PendingDispatch.Draft.Mode.ToString(),
                    preview = snapshot.PendingDispatch.Draft.PreviewText,
                    correlationId = snapshot.PendingDispatch.Draft.CorrelationId,
                    turnId = snapshot.PendingDispatch.TurnId,
                    status = "dispatching",
                    queuedCount = snapshot.QueuedCount,
                }, context.JsonOptions));
                return;
            }

            context.WriteLine("当前没有已恢复的 follow-up 草稿。");
            return;
        }

        context.WriteLine(JsonSerializer.Serialize(new
        {
            mode = snapshot.ComposerDraft.Mode.ToString(),
            preview = snapshot.ComposerDraft.PreviewText,
            correlationId = snapshot.ComposerDraft.CorrelationId,
            pendingBucket = snapshot.ComposerDraft.PendingBucket,
            queuedCount = snapshot.QueuedCount,
        }, context.JsonOptions));
    }

    public void HandleSendRestoredFollowUp(
        InteractiveFollowUpCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = context.RestoredFollowUps.CaptureSnapshot();
        if (snapshot.ComposerDraft is null)
        {
            if (snapshot.PendingDispatch is not null)
            {
                context.WriteLine("当前已有发送中的恢复草稿，请等待当前回合完成。", isError: true);
                return;
            }

            context.WriteLine("当前没有可发送的恢复草稿。", isError: true);
            return;
        }

        if (context.HasRunningConversation())
        {
            context.WriteLine("当前已有运行中的回合，暂时不能发送恢复草稿。", isError: true);
            return;
        }

        var takeResult = context.RestoredFollowUps.TakeComposerDraftForSend();
        if (takeResult.Draft is null)
        {
            context.WriteLine("当前没有可发送的恢复草稿。", isError: true);
            return;
        }

        var draft = takeResult.Draft;
        context.WriteLine($"已发送恢复草稿 {draft.Mode} follow-up：{draft.PreviewText}");
        StartRestoredFollowUp(context, draft, "resume-follow-up", cancellationToken);
    }

    public void HandleDropRestoredFollowUp(InteractiveFollowUpCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var dropResult = context.RestoredFollowUps.DropComposerDraft();
        if (dropResult.Status == RestoredFollowUpDropStatus.Dispatching)
        {
            context.WriteLine("当前已有发送中的恢复草稿，暂时不能丢弃。", isError: true);
            return;
        }

        if (dropResult.Status == RestoredFollowUpDropStatus.Empty || dropResult.DroppedDraft is null)
        {
            context.WriteLine("当前没有可丢弃的恢复草稿。", isError: true);
            return;
        }

        context.WriteLine($"已丢弃恢复草稿：{dropResult.DroppedDraft.PreviewText}");
        WriteRestoredFollowUpPromotion(context, dropResult.PromotedDraft);
    }

    public void TryPromotePendingRestoredFollowUpAfterTurnCompleted(
        InteractiveFollowUpCommandContext context,
        string? turnId)
    {
        ArgumentNullException.ThrowIfNull(context);
        var promoted = context.RestoredFollowUps.TryPromoteAfterTurnCompleted(turnId);
        WriteRestoredFollowUpPromotion(context, promoted);
    }

    public void WriteRestoredFollowUpPromotion(
        InteractiveFollowUpCommandContext context,
        RestoredPendingFollowUp? draft)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (draft is null)
        {
            return;
        }

        context.WriteLine($"已恢复到待编辑 {draft.Mode} follow-up：{draft.PreviewText}");
    }

    public void RestoreDraftAfterDispatchFailure(
        InteractiveFollowUpCommandContext context,
        RestoredPendingFollowUp draft,
        string message)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.RestoredFollowUps.RestoreDraftAfterDispatchFailure(draft);
        context.WriteLine(message, isError: true, countAsFailure: false);
    }

    private void StartRestoredFollowUp(
        InteractiveFollowUpCommandContext context,
        RestoredPendingFollowUp draft,
        string label,
        CancellationToken cancellationToken)
    {
        context.WriteUserMessage(draft.PreviewText, "恢复发送");
        context.StartConversationOperation(
            label,
            token => context.SubmitFollowUpAsync(draft.Inputs, draft.Mode, token, draft.CorrelationId),
            cancellationToken,
            onResult: result => HandleRestoredFollowUpDispatchResult(context, result, draft),
            onCancelled: () => RestoreDraftAfterDispatchFailure(
                context,
                draft,
                "恢复草稿发送已取消，已保留当前草稿。"),
            onException: _ => RestoreDraftAfterDispatchFailure(
                context,
                draft,
                "恢复草稿发送异常，已保留当前草稿。"));
    }

    private void HandleRestoredFollowUpDispatchResult(
        InteractiveFollowUpCommandContext context,
        ControlPlaneTurnSubmissionResult result,
        RestoredPendingFollowUp draft)
    {
        var transition = context.RestoredFollowUps.ApplyDispatchResult(
            result,
            draft,
            context.IsTerminalTurnStatus);
        if (transition.Kind == RestoredFollowUpDispatchTransitionKind.FailedAndRestored)
        {
            context.WriteLine("恢复草稿发送失败，已保留当前草稿。", isError: true, countAsFailure: false);
        }

        WriteRestoredFollowUpPromotion(context, transition.PromotedDraft);
    }

    private static string ReadFirstToken(string? value, out string rest)
    {
        value ??= string.Empty;
        value = value.Trim();
        if (value.Length == 0)
        {
            rest = string.Empty;
            return string.Empty;
        }

        var separator = value.IndexOf(' ', StringComparison.Ordinal);
        if (separator < 0)
        {
            rest = string.Empty;
            return value;
        }

        rest = value[(separator + 1)..].Trim();
        return value[..separator];
    }

    private static bool IsQueuedFollowUpMutationToken(string value)
        => string.Equals(value, "promote", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "drop", StringComparison.OrdinalIgnoreCase);

    private static bool IsDropQueuedFollowUpToken(string value)
        => string.Equals(value, "drop", StringComparison.OrdinalIgnoreCase);

    private static string BuildFollowUpSubmittedMessage(ControlPlaneFollowUpMode mode, bool running)
        => mode switch
        {
            ControlPlaneFollowUpMode.Queue => running
                ? "已加入待发送队列：当前回合结束后会自动作为下一条用户消息发送。"
                : "已加入待发送队列：运行时会按下一条用户消息处理。",
            ControlPlaneFollowUpMode.Steer => "已提交引导信息，等待进入当前回合上下文。",
            ControlPlaneFollowUpMode.Interrupt => "已请求中断当前回合，等待确认。",
            _ => $"已提交 {mode} follow-up。",
        };

    private static void WriteFollowUpResult(
        InteractiveFollowUpCommandContext context,
        ControlPlaneTurnSubmissionResult result,
        ControlPlaneFollowUpMode requestedMode,
        string correlationId)
    {
        if (!result.Accepted)
        {
            if (requestedMode == ControlPlaneFollowUpMode.Queue)
            {
                context.RemoveQueuedFollowUp(correlationId);
                if (IsExpectedPendingFollowUpMutationRejection(result))
                {
                    return;
                }
            }

            context.WriteLine("follow-up 提交失败：运行时未接收这条内容。", isError: true, countAsFailure: false);
            return;
        }

        var effectiveMode = result.EffectiveMode ?? result.RequestedMode ?? requestedMode;
        if (requestedMode == ControlPlaneFollowUpMode.Queue)
        {
            return;
        }

        var label = effectiveMode switch
        {
            ControlPlaneFollowUpMode.Queue => "排队",
            ControlPlaneFollowUpMode.Steer => "引导",
            ControlPlaneFollowUpMode.Interrupt => "中断",
            _ => effectiveMode.ToString(),
        };
        if (effectiveMode is ControlPlaneFollowUpMode.Steer or ControlPlaneFollowUpMode.Interrupt)
        {
            return;
        }

        context.WriteLine(
            $"运行时已接收 {label} follow-up。");
    }

    private static bool IsDroppedPendingFollowUpRejection(ControlPlaneTurnSubmissionResult result)
        => result.Message.Contains("删除", StringComparison.Ordinal)
           || result.Message.Contains("deleted", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedPendingFollowUpMutationRejection(ControlPlaneTurnSubmissionResult result)
        => IsDroppedPendingFollowUpRejection(result)
           || result.Message.Contains("转换为引导", StringComparison.Ordinal)
           || result.Message.Contains("转为引导", StringComparison.Ordinal)
           || result.Message.Contains("promoted", StringComparison.OrdinalIgnoreCase);

}

internal sealed record InteractiveFollowUpCommandContext(
    RestoredFollowUpCoordinator RestoredFollowUps,
    JsonSerializerOptions JsonOptions,
    Func<bool> HasSteerableConversation,
    Func<bool> HasRunningConversation,
    string? CurrentSessionThreadId,
    string? LastObservedTurnId,
    Action<string?> RememberUserRequestedInterrupt,
    InteractiveFollowUpSyntheticEventRecorder RecordSyntheticEvent,
    InteractiveFollowUpSubmitter SubmitFollowUpAsync,
    InteractiveFollowUpMutator MutatePendingFollowUpAsync,
    Action<string, string> AddQueuedFollowUp,
    Action<string> RemoveQueuedFollowUp,
    Func<int, QueuedFollowUpDockEntrySnapshot?> ResolveQueuedFollowUp,
    Action<string, string> TrackPendingGuidance,
    InteractiveFollowUpConversationOperationStarter StartConversationOperation,
    Func<string?, bool> IsTerminalTurnStatus,
    Action<string, string?> WriteUserMessage,
    InteractiveFollowUpWriteLine WriteLine);

internal delegate void InteractiveFollowUpWriteLine(
    string message,
    bool isError = false,
    bool countAsFailure = true);

internal delegate void InteractiveFollowUpSyntheticEventRecorder(
    string kind,
    string? threadId,
    string? turnId,
    string? message,
    string? text);

internal delegate Task<ControlPlaneTurnSubmissionResult> InteractiveFollowUpSubmitter(
    IReadOnlyList<ControlPlaneInputItem> inputs,
    ControlPlaneFollowUpMode mode,
    CancellationToken cancellationToken,
    string? correlationId);

internal delegate Task<ControlPlanePendingFollowUpMutationResult> InteractiveFollowUpMutator(
    string correlationId,
    ControlPlanePendingFollowUpMutationKind kind,
    CancellationToken cancellationToken);

internal delegate void InteractiveFollowUpConversationOperationStarter(
    string label,
    Func<CancellationToken, Task<ControlPlaneTurnSubmissionResult>> operation,
    CancellationToken cancellationToken,
    Action<ControlPlaneTurnSubmissionResult>? onResult = null,
    Action? onCancelled = null,
    Action<Exception>? onException = null);
