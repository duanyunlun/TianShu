using System.Text.Json;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Responses HTTP 请求类型。
/// Responses HTTP request kind.
/// </summary>
public enum ProviderResponsesTransportHttpRequestKind
{
    /// <summary>
    /// 常规 JSON 请求。
    /// Regular JSON request.
    /// </summary>
    JsonRequest = 0,

    /// <summary>
    /// SSE 流式请求。
    /// SSE streaming request.
    /// </summary>
    StreamRequest = 1,
}

/// <summary>
/// Responses HTTP 请求上下文。
/// Context for provider-specific responses HTTP requests.
/// </summary>
public sealed record ProviderResponsesTransportHttpRequestContext(
    string ApiKey,
    string? StickyTurnState,
    string? TurnMetadataHeader,
    ProviderResponsesTransportHttpRequestKind Kind = ProviderResponsesTransportHttpRequestKind.StreamRequest,
    string? Model = null,
    string? TraceParent = null);

/// <summary>
/// Provider 生成的 HTTP 请求绑定结果。
/// Provider-generated HTTP request binding.
/// </summary>
public sealed record ProviderResponsesTransportHttpRequestBinding(
    string Endpoint,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Responses websocket 握手上下文。
/// Context for provider-specific responses websocket handshakes.
/// </summary>
public sealed record ProviderResponsesTransportWebSocketConnectContext(
    string ApiKey,
    string ClientRequestId,
    string? StickyTurnState,
    string? TurnMetadataHeader,
    string? TraceParent = null);

/// <summary>
/// Provider 生成的 websocket 握手绑定结果。
/// Provider-generated websocket handshake binding.
/// </summary>
public sealed record ProviderResponsesTransportWebSocketConnectionBinding(
    Uri Endpoint,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Responses websocket create-request 上下文。
/// Context for provider-specific websocket create requests.
/// </summary>
public sealed record ProviderResponsesTransportWebSocketRequestContext(
    IReadOnlyDictionary<string, object?> Payload,
    IReadOnlyList<JsonElement> Input,
    string? PreviousResponseId,
    string? TurnMetadataHeader);

/// <summary>
/// Provider 生成的 websocket request 负载。
/// Provider-generated websocket request payload.
/// </summary>
public sealed record ProviderResponsesTransportWebSocketRequestBinding(
    IReadOnlyDictionary<string, object?> Payload);

/// <summary>
/// Provider transport 响应头快照。
/// Snapshot of provider transport response headers.
/// </summary>
public sealed record ProviderResponsesTransportResponseHeaders(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values);

/// <summary>
/// Provider-specific responses transport protocol binding。
/// Provider-specific protocol binding for responses transport.
/// </summary>
public interface IProviderResponsesTransportProtocolBinding
{
    /// <summary>
    /// 该 binding 对应的 wire API 标识。
    /// Wire API identifier owned by this binding.
    /// </summary>
    string WireApi { get; }

    /// <summary>
    /// 构造 HTTP 请求绑定。
    /// Builds an HTTP request binding.
    /// </summary>
    ProviderResponsesTransportHttpRequestBinding CreateHttpRequestBinding(
        string baseUrl,
        ProviderResponsesTransportHttpRequestContext context);

    /// <summary>
    /// 构造 websocket 握手绑定。
    /// Builds a websocket handshake binding.
    /// </summary>
    ProviderResponsesTransportWebSocketConnectionBinding CreateWebSocketConnectionBinding(
        string baseUrl,
        ProviderResponsesTransportWebSocketConnectContext context);

    /// <summary>
    /// 构造 websocket create-request 负载。
    /// Builds a websocket create-request payload.
    /// </summary>
    ProviderResponsesTransportWebSocketRequestBinding CreateWebSocketRequestBinding(
        ProviderResponsesTransportWebSocketRequestContext context);

    /// <summary>
    /// 从 provider 响应头中解析 sticky turn state。
    /// Reads sticky turn state from provider response headers.
    /// </summary>
    string? ReadStickyTurnState(ProviderResponsesTransportResponseHeaders headers);
}
