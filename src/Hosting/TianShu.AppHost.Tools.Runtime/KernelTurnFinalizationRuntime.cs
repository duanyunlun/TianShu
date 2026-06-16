using System.Collections.Concurrent;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn finalization 运行时，负责 turn 结束后的 diff 通知、运行时资源释放与活动状态清理。
/// Turn finalization runtime that owns diff notification, runtime resource disposal, and active-state cleanup after a turn ends.
/// </summary>
internal sealed class KernelTurnFinalizationRuntime
{
    private readonly KernelThreadManager threadManager;
    private readonly KernelTurnBackgroundSchedulerRuntime backgroundTurnSchedulerRuntime;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> steerInputsByTurn;
    private readonly ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionTurnByTurn;
    private readonly Func<string, CancellationToken, Task<string>> captureThreadGitDiffAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Action<string> deactivateCodeModeTurn;
    private readonly Func<string, Task> disposeJsReplManagerAsync;

    public KernelTurnFinalizationRuntime(
        KernelThreadManager threadManager,
        KernelTurnBackgroundSchedulerRuntime backgroundTurnSchedulerRuntime,
        ConcurrentDictionary<string, ConcurrentQueue<string>> steerInputsByTurn,
        ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionTurnByTurn,
        Func<string, CancellationToken, Task<string>> captureThreadGitDiffAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Action<string> deactivateCodeModeTurn,
        Func<string, Task> disposeJsReplManagerAsync)
    {
        this.threadManager = threadManager ?? throw new ArgumentNullException(nameof(threadManager));
        this.backgroundTurnSchedulerRuntime = backgroundTurnSchedulerRuntime ?? throw new ArgumentNullException(nameof(backgroundTurnSchedulerRuntime));
        this.steerInputsByTurn = steerInputsByTurn ?? throw new ArgumentNullException(nameof(steerInputsByTurn));
        this.grantedPermissionTurnByTurn = grantedPermissionTurnByTurn ?? throw new ArgumentNullException(nameof(grantedPermissionTurnByTurn));
        this.captureThreadGitDiffAsync = captureThreadGitDiffAsync ?? throw new ArgumentNullException(nameof(captureThreadGitDiffAsync));
        this.writeNotificationAsync = writeNotificationAsync ?? throw new ArgumentNullException(nameof(writeNotificationAsync));
        this.deactivateCodeModeTurn = deactivateCodeModeTurn ?? throw new ArgumentNullException(nameof(deactivateCodeModeTurn));
        this.disposeJsReplManagerAsync = disposeJsReplManagerAsync ?? throw new ArgumentNullException(nameof(disposeJsReplManagerAsync));
    }

    public async Task FinalizeAsync(string threadId, string turnId)
    {
        await NotifyDiffAsync(threadId, turnId).ConfigureAwait(false);

        backgroundTurnSchedulerRuntime.Complete(turnId);

        deactivateCodeModeTurn(turnId);
        await disposeJsReplManagerAsync(turnId).ConfigureAwait(false);

        grantedPermissionTurnByTurn.TryRemove(turnId, out _);
        steerInputsByTurn.TryRemove(turnId, out _);

        if (threadManager.TryGetThread(threadId, out var thread))
        {
            _ = thread?.ClearActiveTurn(turnId);
        }
    }

    private async Task NotifyDiffAsync(string threadId, string turnId)
    {
        try
        {
            var diff = await captureThreadGitDiffAsync(threadId, CancellationToken.None).ConfigureAwait(false);
            await writeNotificationAsync("turn/diff/updated", new
            {
                threadId,
                turnId,
                diff,
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // 忽略 diff 采集失败，避免影响 turn 生命周期收敛。
        }
    }
}
