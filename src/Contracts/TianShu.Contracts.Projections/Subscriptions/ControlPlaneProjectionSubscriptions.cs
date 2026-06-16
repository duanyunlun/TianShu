using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Projections;

/// <summary>
/// 控制平面线程投影订阅请求。
/// Control-plane subscription request for thread projections.
/// </summary>
public sealed record ControlPlaneThreadSubscription
{
    /// <summary>
    /// 线程过滤条件；为空表示订阅所有线程事件。
    /// Optional thread filter; null subscribes to all thread events.
    /// </summary>
    public ThreadId? ThreadId { get; init; }
}

/// <summary>
/// 控制平面工作流投影订阅请求。
/// Control-plane subscription request for workflow projections.
/// </summary>
public sealed record ControlPlaneWorkflowSubscription
{
    /// <summary>
    /// 线程过滤条件；为空表示订阅所有工作流事件。
    /// Optional thread filter; null subscribes to all workflow events.
    /// </summary>
    public ThreadId? ThreadId { get; init; }
}

/// <summary>
/// 控制平面代理投影订阅请求。
/// Control-plane subscription request for agent projections.
/// </summary>
public sealed record ControlPlaneAgentSubscription
{
    /// <summary>
    /// 线程过滤条件；为空表示订阅所有代理事件。
    /// Optional thread filter; null subscribes to all agent events.
    /// </summary>
    public ThreadId? ThreadId { get; init; }
}

/// <summary>
/// 控制平面治理投影订阅请求。
/// Control-plane subscription request for governance projections.
/// </summary>
public sealed record ControlPlaneGovernanceSubscription
{
    /// <summary>
    /// 线程过滤条件；为空表示订阅所有治理事件。
    /// Optional thread filter; null subscribes to all governance events.
    /// </summary>
    public ThreadId? ThreadId { get; init; }
}
