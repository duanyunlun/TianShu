using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Anthropic;

/// <summary>
/// Anthropic Messages HTTP transport binding。
/// HTTP transport binding for Anthropic Messages.
/// </summary>
public sealed class AnthropicMessagesTransportProtocolBinding : IProviderResponsesTransportProtocolBinding
{
    private const string AnthropicVersion = "2023-06-01";
    private const string TraceParentHeaderName = "traceparent";

    /// <inheritdoc />
    public string WireApi => ProviderWireApi.AnthropicMessages;

    /// <inheritdoc />
    public ProviderResponsesTransportHttpRequestBinding CreateHttpRequestBinding(
        string baseUrl,
        ProviderResponsesTransportHttpRequestContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(context);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["x-api-key"] = context.ApiKey,
            ["anthropic-version"] = AnthropicVersion,
        };

        if (context.Kind == ProviderResponsesTransportHttpRequestKind.StreamRequest)
        {
            headers["Accept"] = "text/event-stream";
        }

        if (!string.IsNullOrWhiteSpace(context.TraceParent))
        {
            headers[TraceParentHeaderName] = context.TraceParent;
        }

        return new ProviderResponsesTransportHttpRequestBinding(
            ResolveHttpEndpoint(baseUrl),
            headers);
    }

    /// <inheritdoc />
    public ProviderResponsesTransportWebSocketConnectionBinding CreateWebSocketConnectionBinding(
        string baseUrl,
        ProviderResponsesTransportWebSocketConnectContext context)
        => throw new NotSupportedException("Anthropic Messages 不支持 Responses websocket transport。");

    /// <inheritdoc />
    public ProviderResponsesTransportWebSocketRequestBinding CreateWebSocketRequestBinding(
        ProviderResponsesTransportWebSocketRequestContext context)
        => throw new NotSupportedException("Anthropic Messages 不支持 Responses websocket transport。");

    /// <inheritdoc />
    public string? ReadStickyTurnState(ProviderResponsesTransportResponseHeaders headers)
        => null;

    private static string ResolveHttpEndpoint(string baseUrl)
        => ProviderEndpointPathUtilities.ResolveVersionedEndpoint(
            baseUrl,
            "v1",
            "messages");
}
