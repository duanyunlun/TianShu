using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Sessions;

/// <summary>
/// 控制平面会话快照，描述当前运行时的即时会话状态。
/// Control-plane session snapshot describing the runtime's immediate session state.
/// </summary>
public sealed record ControlPlaneSessionSnapshot
{
    /// <summary>
    /// 当前运行时名称。
    /// Current runtime name.
    /// </summary>
    public string RuntimeName { get; init; } = string.Empty;

    /// <summary>
    /// 当前活动线程标识。
    /// Current active thread identifier.
    /// </summary>
    public ThreadId? ActiveThreadId { get; init; }

    /// <summary>
    /// 是否存在活动中的 turn。
    /// Whether an active turn is currently in flight.
    /// </summary>
    public bool HasActiveTurn { get; init; }
}
