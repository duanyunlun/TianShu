using System.Text.Json.Serialization;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Remote;

/// <summary>
/// 远程事件游标，用于断线重连后的补发和去重。
/// Remote event cursor used for replay and de-duplication after reconnect.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<RemoteEventCursor>))]
public readonly record struct RemoteEventCursor
{
    public RemoteEventCursor(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(RemoteEventCursor value) => value.Value;
}

/// <summary>
/// 远程事件 transport 类型；它只描述传输方式，不改变事件语义。
/// Remote event transport kind; it describes transport only and does not alter event semantics.
/// </summary>
public enum RemoteEventTransportKind
{
    Unspecified = 0,
    ServerSentEvents = 1,
    WebSocket = 2,
    LocalHttpPolling = 3,
    NamedPipe = 4,
    StdioBridge = 5,
    CloudRelayAdapter = 6,
}

/// <summary>
/// 远程事件补发模式。
/// Remote event replay mode.
/// </summary>
public enum RemoteEventReplayMode
{
    Unspecified = 0,
    LatestOnly = 1,
    FromCursor = 2,
    SnapshotThenEvents = 3,
}

/// <summary>
/// 事件保留状态，说明 cursor 是否仍可补发。
/// Event retention state describing whether a cursor can still be replayed.
/// </summary>
public enum RemoteEventRetentionState
{
    Unknown = 0,
    Available = 1,
    CursorExpired = 2,
    NotRetained = 3,
}

/// <summary>
/// 远程连续性事件类型。
/// Remote continuity event kind.
/// </summary>
public enum RemoteContinuityEventKind
{
    Unknown = 0,
    Heartbeat = 1,
    SnapshotRequired = 2,
    SnapshotAvailable = 3,
    RunStateChanged = 4,
    StageChanged = 5,
    ToolStateChanged = 6,
    SubAgentStateChanged = 7,
    ApprovalRequested = 8,
    ApprovalUpdated = 9,
    ArtifactChanged = 10,
    DiagnosticsUpdated = 11,
    EvidenceUpdated = 12,
}

/// <summary>
/// 远程事件可见性说明。
/// Remote event visibility descriptor.
/// </summary>
public sealed record RemoteEventVisibility
{
    public RemoteEventVisibility(
        bool redacted = false,
        IReadOnlyList<string>? visibleScopes = null,
        IReadOnlyList<string>? redactedKinds = null,
        string? policyRef = null)
    {
        Redacted = redacted;
        VisibleScopes = visibleScopes ?? Array.Empty<string>();
        RedactedKinds = redactedKinds ?? Array.Empty<string>();
        PolicyRef = string.IsNullOrWhiteSpace(policyRef) ? null : policyRef.Trim();
    }

    public bool Redacted { get; }

    public IReadOnlyList<string> VisibleScopes { get; }

    public IReadOnlyList<string> RedactedKinds { get; }

    public string? PolicyRef { get; }
}

/// <summary>
/// 远程事件订阅请求。
/// Remote event subscription request.
/// </summary>
public sealed record RemoteEventSubscriptionRequest
{
    public RemoteEventSubscriptionRequest(
        string subscriptionId,
        ThreadId threadId,
        RemoteEventTransportKind transportKind,
        RemoteEventReplayMode replayMode,
        RemoteEventCursor? lastCursor = null,
        DeviceId? deviceId = null,
        SessionId? sessionId = null,
        bool includeSnapshotOnStart = true,
        TimeSpan? heartbeatInterval = null,
        IReadOnlyList<RemoteContinuityEventKind>? eventKinds = null)
    {
        if (transportKind == RemoteEventTransportKind.Unspecified)
        {
            throw new ArgumentException("远程事件订阅必须声明 transport kind。", nameof(transportKind));
        }

        if (replayMode == RemoteEventReplayMode.FromCursor && lastCursor is null)
        {
            throw new ArgumentException("FromCursor 补发模式必须提供 last cursor。", nameof(lastCursor));
        }

        SubscriptionId = IdentifierGuard.AgainstNullOrWhiteSpace(subscriptionId, nameof(subscriptionId));
        ThreadId = threadId;
        TransportKind = transportKind;
        ReplayMode = replayMode == RemoteEventReplayMode.Unspecified
            ? RemoteEventReplayMode.SnapshotThenEvents
            : replayMode;
        LastCursor = lastCursor;
        DeviceId = deviceId;
        SessionId = sessionId;
        IncludeSnapshotOnStart = includeSnapshotOnStart;
        HeartbeatInterval = NormalizeHeartbeat(heartbeatInterval);
        EventKinds = eventKinds ?? Array.Empty<RemoteContinuityEventKind>();
    }

    public string SubscriptionId { get; }

    public ThreadId ThreadId { get; }

    public RemoteEventTransportKind TransportKind { get; }

    public RemoteEventReplayMode ReplayMode { get; }

    public RemoteEventCursor? LastCursor { get; }

    public DeviceId? DeviceId { get; }

    public SessionId? SessionId { get; }

    public bool IncludeSnapshotOnStart { get; }

    public TimeSpan HeartbeatInterval { get; }

    public IReadOnlyList<RemoteContinuityEventKind> EventKinds { get; }

    private static TimeSpan NormalizeHeartbeat(TimeSpan? heartbeatInterval)
    {
        if (heartbeatInterval is null)
        {
            return TimeSpan.FromSeconds(15);
        }

        if (heartbeatInterval.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), "Heartbeat interval 必须大于 0。");
        }

        return heartbeatInterval.Value;
    }
}

/// <summary>
/// 远程事件补发计划。
/// Remote event replay plan.
/// </summary>
public sealed record RemoteEventReplayPlan
{
    public RemoteEventReplayPlan(
        RemoteEventReplayMode replayMode,
        RemoteEventRetentionState retentionState,
        RemoteEventCursor? fromCursor = null,
        bool snapshotRequired = false,
        string? reason = null)
    {
        if (replayMode == RemoteEventReplayMode.FromCursor && fromCursor is null)
        {
            throw new ArgumentException("FromCursor 补发计划必须提供 from cursor。", nameof(fromCursor));
        }

        ReplayMode = replayMode == RemoteEventReplayMode.Unspecified
            ? RemoteEventReplayMode.SnapshotThenEvents
            : replayMode;
        RetentionState = retentionState;
        FromCursor = fromCursor;
        SnapshotRequired = snapshotRequired
            || retentionState is RemoteEventRetentionState.CursorExpired or RemoteEventRetentionState.NotRetained;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public RemoteEventReplayMode ReplayMode { get; }

    public RemoteEventRetentionState RetentionState { get; }

    public RemoteEventCursor? FromCursor { get; }

    public bool SnapshotRequired { get; }

    public string? Reason { get; }
}

/// <summary>
/// 远程连续性事件 envelope，所有 transport 都必须传输同一语义。
/// Remote continuity event envelope; all transports must carry the same semantics.
/// </summary>
public sealed record RemoteContinuityEvent
{
    public RemoteContinuityEvent(
        string eventId,
        ThreadId threadId,
        RemoteEventCursor cursor,
        RemoteContinuityEventKind kind,
        DateTimeOffset occurredAt,
        StructuredValue? payload = null,
        RemoteEventVisibility? visibility = null,
        string? correlationId = null)
    {
        EventId = IdentifierGuard.AgainstNullOrWhiteSpace(eventId, nameof(eventId));
        ThreadId = threadId;
        Cursor = cursor;
        Kind = kind == RemoteContinuityEventKind.Unknown
            ? throw new ArgumentException("远程事件必须声明明确 kind。", nameof(kind))
            : kind;
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
        Payload = payload;
        Visibility = visibility ?? new RemoteEventVisibility();
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();
    }

    public string EventId { get; }

    public ThreadId ThreadId { get; }

    public RemoteEventCursor Cursor { get; }

    public RemoteContinuityEventKind Kind { get; }

    public DateTimeOffset OccurredAt { get; }

    public StructuredValue? Payload { get; }

    public RemoteEventVisibility Visibility { get; }

    public string? CorrelationId { get; }
}

/// <summary>
/// 远程事件订阅接口；实现只能输出远程事件 envelope，不拥有 Host / Kernel / Runtime 状态。
/// Remote event subscription interface; implementations only emit remote event envelopes and do not own Host / Kernel / Runtime state.
/// </summary>
public interface IRemoteContinuityEventSubscriber
{
    IAsyncEnumerable<RemoteContinuityEvent> SubscribeAsync(
        RemoteEventSubscriptionRequest request,
        CancellationToken cancellationToken);
}
