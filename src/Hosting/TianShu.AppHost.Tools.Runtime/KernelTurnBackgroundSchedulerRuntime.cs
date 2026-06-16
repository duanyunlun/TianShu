using System.Collections.Concurrent;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Background turn 调度运行时，负责维护运行中 turn 的注册、任务启动、取消与完成清理。
/// Background turn scheduler runtime that owns running-turn registration, task scheduling, cancellation, and completion cleanup.
/// </summary>
internal sealed class KernelTurnBackgroundSchedulerRuntime
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> runningTurns;
    private readonly ConcurrentDictionary<string, Task> runningTurnTasks;

    public KernelTurnBackgroundSchedulerRuntime(
        ConcurrentDictionary<string, CancellationTokenSource> runningTurns,
        ConcurrentDictionary<string, Task> runningTurnTasks)
    {
        this.runningTurns = runningTurns ?? throw new ArgumentNullException(nameof(runningTurns));
        this.runningTurnTasks = runningTurnTasks ?? throw new ArgumentNullException(nameof(runningTurnTasks));
    }

    public CancellationTokenSource Register(string turnId, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runningTurns[turnId] = cts;
        return cts;
    }

    public void Schedule(
        string turnId,
        Func<CancellationTokenSource, Task> runAsync)
    {
        ArgumentNullException.ThrowIfNull(runAsync);

        if (!runningTurns.TryGetValue(turnId, out var cts))
        {
            throw new InvalidOperationException($"后台 turn `{turnId}` 尚未注册，无法调度。");
        }

        runningTurnTasks[turnId] = Task.Run(() => runAsync(cts), CancellationToken.None);
    }

    public bool TryCancel(string turnId)
    {
        if (!runningTurns.TryGetValue(turnId, out var cts))
        {
            return false;
        }

        cts.Cancel();
        return true;
    }

    public void Complete(string turnId)
    {
        if (runningTurns.TryRemove(turnId, out var running))
        {
            running.Dispose();
        }

        runningTurnTasks.TryRemove(turnId, out _);
    }
}
