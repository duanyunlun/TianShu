using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAICompatible;

/// <summary>
/// OpenAI-compatible Chat Completions transport binding。
/// Transport binding for OpenAI-compatible Chat Completions.
/// </summary>
public sealed class OpenAiChatCompletionsTransportProtocolBinding : IProviderResponsesTransportProtocolBinding
{
    private const string TraceParentHeaderName = "traceparent";

    /// <inheritdoc />
    public string WireApi => ProviderWireApi.OpenAiChatCompletions;

    /// <inheritdoc />
    public ProviderResponsesTransportHttpRequestBinding CreateHttpRequestBinding(
        string baseUrl,
        ProviderResponsesTransportHttpRequestContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(context);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = $"Bearer {context.ApiKey}",
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
        => throw new NotSupportedException("OpenAI-compatible Chat Completions 不支持 Responses websocket transport。");

    /// <inheritdoc />
    public ProviderResponsesTransportWebSocketRequestBinding CreateWebSocketRequestBinding(
        ProviderResponsesTransportWebSocketRequestContext context)
        => throw new NotSupportedException("OpenAI-compatible Chat Completions 不支持 Responses websocket transport。");

    /// <inheritdoc />
    public string? ReadStickyTurnState(ProviderResponsesTransportResponseHeaders headers)
        => null;

    private static string ResolveHttpEndpoint(string baseUrl)
        => ProviderEndpointPathUtilities.ResolveVersionedEndpoint(
            baseUrl,
            "v1",
            "chat/completions");
}
