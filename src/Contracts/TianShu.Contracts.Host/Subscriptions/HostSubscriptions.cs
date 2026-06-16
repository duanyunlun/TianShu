using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Host;

/// <summary>
/// 宿主订阅请求，表达消费层希望订阅哪个投影作用域。
/// Host subscription request describing which projection scope the consumer wants to subscribe to.
/// </summary>
public sealed record HostSubscriptionRequest
{
    /// <summary>
    /// 初始化宿主订阅请求。
    /// Initializes a host subscription request.
    /// </summary>
    public HostSubscriptionRequest(
        ProjectionSubscription subscription,
        HostCapabilityNegotiation? negotiation = null)
    {
        Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
        Negotiation = negotiation;
    }

    public ProjectionSubscription Subscription { get; }

    public HostCapabilityNegotiation? Negotiation { get; }
}

/// <summary>
/// 宿主会话流订阅请求。
/// Host request that subscribes to conversation stream events.
/// </summary>
public sealed record HostConversationStreamSubscription
{
    /// <summary>
    /// 可选线程过滤条件。
    /// Optional thread filter.
    /// </summary>
    public ThreadId? ThreadId { get; init; }
}
