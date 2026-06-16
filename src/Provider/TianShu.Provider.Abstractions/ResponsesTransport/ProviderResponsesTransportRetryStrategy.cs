namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider responses transport 失败分类。
/// Failure kinds emitted by provider responses transport orchestration.
/// </summary>
public enum ProviderResponsesTransportFailureKind
{
    /// <summary>
    /// 未知失败。
    /// Unknown failure.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Provider 流事件显式失败。
    /// Explicit provider stream-event failure.
    /// </summary>
    ProviderStreamFailure = 1,

    /// <summary>
    /// HTTP 请求失败。
    /// HTTP request failure.
    /// </summary>
    HttpRequestFailure = 2,

    /// <summary>
    /// 通用 I/O 失败。
    /// Generic I/O failure.
    /// </summary>
    IoFailure = 3,

    /// <summary>
    /// 超时失败。
    /// Timeout failure.
    /// </summary>
    Timeout = 4,

    /// <summary>
    /// 操作被取消。
    /// Operation was cancelled.
    /// </summary>
    OperationCanceled = 5,

    /// <summary>
    /// WebSocket 传输失败。
    /// WebSocket transport failure.
    /// </summary>
    WebSocketTransportFailure = 6,

    /// <summary>
    /// WebSocket 握手要求切回 HTTP。
    /// WebSocket handshake requires switching back to HTTP.
    /// </summary>
    WebSocketUpgradeRequired = 7,
}

/// <summary>
/// Provider responses transport 失败快照。
/// Failure snapshot for provider responses transport orchestration.
/// </summary>
public sealed record ProviderResponsesTransportFailure(
    ProviderResponsesTransportFailureKind Kind,
    bool IsRetryable,
    string? Message = null);

/// <summary>
/// Provider responses transport 重试决策。
/// Retry decision returned by provider responses transport strategy.
/// </summary>
public sealed record ProviderResponsesTransportRetryDecision(
    bool ShouldRetry,
    bool ShouldSwitchToHttpTransport,
    TimeSpan Delay,
    string? RetryMessage = null)
{
    /// <summary>
    /// 不重试也不切换 transport。
    /// Do not retry and do not switch transport.
    /// </summary>
    public static ProviderResponsesTransportRetryDecision None { get; } =
        new(ShouldRetry: false, ShouldSwitchToHttpTransport: false, Delay: TimeSpan.Zero);

    /// <summary>
    /// 终止 websocket 并切回 HTTP transport。
    /// Stop websocket usage and switch back to HTTP transport.
    /// </summary>
    public static ProviderResponsesTransportRetryDecision SwitchToHttpTransport { get; } =
        new(ShouldRetry: false, ShouldSwitchToHttpTransport: true, Delay: TimeSpan.Zero);

    /// <summary>
    /// 构造一次重试决策。
    /// Creates a retry decision.
    /// </summary>
    public static ProviderResponsesTransportRetryDecision Retry(TimeSpan delay, string retryMessage) =>
        new(
            ShouldRetry: true,
            ShouldSwitchToHttpTransport: false,
            Delay: delay,
            RetryMessage: retryMessage);
}

/// <summary>
/// Provider responses transport 重试策略。
/// Retry strategy for provider-specific responses transport orchestration.
/// </summary>
public interface IProviderResponsesTransportRetryStrategy
{
    /// <summary>
    /// 该策略负责的 wire API 标识。
    /// Wire API identifier owned by this strategy.
    /// </summary>
    string WireApi { get; }

    /// <summary>
    /// 评估 HTTP SSE transport 的重试决策。
    /// Evaluates retry behavior for HTTP SSE transport.
    /// </summary>
    ProviderResponsesTransportRetryDecision EvaluateHttpStreamRetry(
        ProviderResponsesTransportFailure failure,
        int retryIndex,
        int maxRetries,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        bool cancellationRequested);

    /// <summary>
    /// 评估 websocket transport 的重试或切换决策。
    /// Evaluates retry-or-switch behavior for websocket transport.
    /// </summary>
    ProviderResponsesTransportRetryDecision EvaluateWebSocketRetry(
        ProviderResponsesTransportFailure failure,
        int retryIndex,
        int maxRetries,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        bool cancellationRequested);
}
