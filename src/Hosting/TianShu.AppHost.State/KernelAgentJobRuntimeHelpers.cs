namespace TianShu.AppHost.State;

/// <summary>
/// agent-job 运行中的活跃 worker 快照，用于在轮询恢复期间追踪 agent 与 item 的对应关系。
/// Active worker snapshot for agent-job runtime used to track agent-to-item assignments during polling recovery.
/// </summary>
internal sealed record KernelAgentJobActiveWorker(string AgentId, string ItemId, DateTimeOffset StartedAt);

/// <summary>
/// agent-job 进度通知节流器，避免高频轮询下重复发送同一份进度增量。
/// agent-job progress emission throttle that avoids repeatedly sending the same progress delta under fast polling.
/// </summary>
internal sealed class KernelAgentJobProgressEmitter
{
    private static readonly TimeSpan EmitInterval = TimeSpan.FromSeconds(1);
    private DateTimeOffset lastEmitAt = DateTimeOffset.MinValue;
    private int lastProcessed = -1;
    private int lastFailed = -1;

    public bool ShouldEmit(KernelAgentJobProgressSnapshot progress, bool force)
    {
        var processed = progress.CompletedItems + progress.FailedItems;
        return force
               || processed != lastProcessed
               || progress.FailedItems != lastFailed
               || DateTimeOffset.UtcNow - lastEmitAt >= EmitInterval;
    }

    public void MarkEmitted(KernelAgentJobProgressSnapshot progress)
    {
        lastEmitAt = DateTimeOffset.UtcNow;
        lastProcessed = progress.CompletedItems + progress.FailedItems;
        lastFailed = progress.FailedItems;
    }
}
