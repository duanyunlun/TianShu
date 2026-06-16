namespace TianShu.Contracts.Conversations;

/// <summary>
/// 线程投影，表达线程当前的只读摘要视图。
/// Thread projection expressing the current read-only summary of a thread.
/// </summary>
public sealed record ThreadProjection(ThreadSnapshot Snapshot, IReadOnlyList<TurnRef> Turns)
{
    public IReadOnlyList<TurnRef> Turns { get; } = Turns ?? Array.Empty<TurnRef>();
}
