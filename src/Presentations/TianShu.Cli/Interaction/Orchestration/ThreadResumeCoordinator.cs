using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;

namespace TianShu.Cli.Interaction.Orchestration;

/// <summary>
/// Coordinates thread resume side effects for the interactive CLI session.
/// 编排交互式 CLI 会话中的线程恢复副作用。
/// </summary>
internal sealed class ThreadResumeCoordinator
{
    public async Task<ControlPlaneThreadSnapshot?> ResumeThreadByIdAsync(
        ThreadResumeCoordinatorContext context,
        string threadId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var resumed = await context.ResumeThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (resumed is not null)
        {
            ConsumeResumedThreadState(context, resumed, cancellationToken);
        }

        context.WriteLine(
            resumed is null
                ? "恢复线程失败。"
                : $"已恢复线程：{resumed.Thread.ThreadId.Value}（种子历史 {resumed.SeedHistory.Count}，回合数 {resumed.Turns.Count}）",
            resumed is null);
        WritePendingInteractiveReplaySummary(context, resumed);
        return resumed;
    }

    public async Task TryConsumeStartupResumedThreadStateAsync(
        ThreadResumeCoordinatorContext context,
        bool shouldConsumeStartupResumedThread,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sessionSnapshot = await context.GetSessionSnapshotAsync(cancellationToken).ConfigureAwait(false);
        context.ApplySessionSnapshot(sessionSnapshot);
        if (sessionSnapshot.ActiveThreadId is null || !shouldConsumeStartupResumedThread)
        {
            return;
        }

        var resumed = await context.ResumeThreadAsync(sessionSnapshot.ActiveThreadId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (resumed is null)
        {
            return;
        }

        await context.RefreshSessionSnapshotAsync(cancellationToken).ConfigureAwait(false);
        ConsumeResumedThreadState(context, resumed, cancellationToken);
        WritePendingInteractiveReplaySummary(context, resumed);
    }

    public void ConsumeResumedThreadState(
        ThreadResumeCoordinatorContext context,
        ControlPlaneThreadSnapshot resumed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resumed);

        context.PendingInteractiveRequests.Clear();
        ReplayPendingInteractiveRequests(context, resumed.PendingInteractiveRequests, cancellationToken);
        RestorePendingFollowUps(context, resumed.PendingInputState);
    }

    private static void ReplayPendingInteractiveRequests(
        ThreadResumeCoordinatorContext context,
        IReadOnlyList<ControlPlanePendingInteractiveRequest> requests,
        CancellationToken cancellationToken)
    {
        foreach (var replayEvent in context.PendingInteractiveReplay.BuildReplayEvents(requests))
        {
            context.HandleReplayEvent(replayEvent, cancellationToken);
        }
    }

    private static void RestorePendingFollowUps(
        ThreadResumeCoordinatorContext context,
        ControlPlanePendingInputState? pendingInputState)
    {
        var result = context.RestoredFollowUps.Restore(pendingInputState);
        if (result.RestoredCount == 0)
        {
            return;
        }

        context.WriteLine($"已恢复待编辑 follow-up：{result.RestoredCount}。", false);
        context.WriteRestoredFollowUpPromotion(result.PromotedDraft);
    }

    private static void WritePendingInteractiveReplaySummary(
        ThreadResumeCoordinatorContext context,
        ControlPlaneThreadSnapshot? resumed)
    {
        if (resumed is null || resumed.PendingInteractiveRequests.Count == 0)
        {
            return;
        }

        context.WriteLine(
            $"已回放待处理交互：审批 {context.PendingInteractiveRequests.ApprovalCount}，权限 {context.PendingInteractiveRequests.PermissionCount}，补录 {context.PendingInteractiveRequests.UserInputCount}。",
            false);
    }
}

internal sealed record ThreadResumeCoordinatorContext(
    Func<string, CancellationToken, Task<ControlPlaneThreadSnapshot?>> ResumeThreadAsync,
    Func<CancellationToken, Task<ControlPlaneSessionSnapshot>> GetSessionSnapshotAsync,
    Func<CancellationToken, Task> RefreshSessionSnapshotAsync,
    Action<ControlPlaneSessionSnapshot> ApplySessionSnapshot,
    PendingInteractiveRequestStore PendingInteractiveRequests,
    PendingInteractiveReplayCoordinator PendingInteractiveReplay,
    RestoredFollowUpCoordinator RestoredFollowUps,
    Action<ControlPlaneConversationStreamEvent, CancellationToken> HandleReplayEvent,
    Action<RestoredPendingFollowUp?> WriteRestoredFollowUpPromotion,
    Action<string, bool> WriteLine);
