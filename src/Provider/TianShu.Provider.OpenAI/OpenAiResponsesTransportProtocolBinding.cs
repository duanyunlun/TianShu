using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI Responses transport protocol binding。
/// Protocol binding for the OpenAI Responses transport.
/// </summary>
public sealed class OpenAiResponsesTransportProtocolBinding : IProviderResponsesTransportProtocolBinding
{
    private const string ClientRequestIdHeaderName = "x-client-request-id";
    private const string TurnMetadataHeaderName = "x-codex-turn-metadata";
    private const string TurnStateHeaderName = "x-codex-turn-state";
    private const string TraceParentHeaderName = "traceparent";
    private const string OpenAiBetaHeaderName = "OpenAI-Beta";
    private const string ResponsesWebsocketsV2BetaHeaderValue = "responses_websockets=2026-02-06";

    /// <inheritdoc />
    public string WireApi => "responses";

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

        if (!string.IsNullOrWhiteSpace(context.StickyTurnState))
        {
            headers[TurnStateHeaderName] = context.StickyTurnState;
        }

        if (!string.IsNullOrWhiteSpace(context.TurnMetadataHeader))
        {
            headers[TurnMetadataHeaderName] = context.TurnMetadataHeader;
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(context);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = $"Bearer {context.ApiKey}",
            [OpenAiBetaHeaderName] = ResponsesWebsocketsV2BetaHeaderValue,
            [ClientRequestIdHeaderName] = context.ClientRequestId,
        };

        if (!string.IsNullOrWhiteSpace(context.StickyTurnState))
        {
            headers[TurnStateHeaderName] = context.StickyTurnState;
        }

        if (!string.IsNullOrWhiteSpace(context.TurnMetadataHeader))
        {
            headers[TurnMetadataHeaderName] = context.TurnMetadataHeader;
        }

        if (!string.IsNullOrWhiteSpace(context.TraceParent))
        {
            headers[TraceParentHeaderName] = context.TraceParent;
        }

        return new ProviderResponsesTransportWebSocketConnectionBinding(
            ResolveWebSocketEndpoint(baseUrl),
            headers);
    }

    /// <inheritdoc />
    public ProviderResponsesTransportWebSocketRequestBinding CreateWebSocketRequestBinding(
        ProviderResponsesTransportWebSocketRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = new Dictionary<string, object?>(context.Payload, StringComparer.Ordinal)
        {
            ["type"] = "response.create",
            ["input"] = context.Input,
        };

        if (string.IsNullOrWhiteSpace(context.PreviousResponseId))
        {
            request.Remove("previous_response_id");
        }
        else
        {
            request["previous_response_id"] = context.PreviousResponseId;
        }

        if (!string.IsNullOrWhiteSpace(context.TurnMetadataHeader))
        {
            request["client_metadata"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TurnMetadataHeaderName] = context.TurnMetadataHeader,
            };
        }

        return new ProviderResponsesTransportWebSocketRequestBinding(request);
    }

    /// <inheritdoc />
    public string? ReadStickyTurnState(ProviderResponsesTransportResponseHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return headers.Values.TryGetValue(TurnStateHeaderName, out var values)
            ? Normalize(values.FirstOrDefault())
            : null;
    }

    private static string ResolveHttpEndpoint(string baseUrl)
        => ProviderEndpointPathUtilities.ResolveVersionedEndpoint(
            baseUrl,
            "v1",
            "responses");

    private static Uri ResolveWebSocketEndpoint(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"无效 provider base_url：{baseUrl}", innerException: null);
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme switch
            {
                "http" => "ws",
                "https" => "wss",
                "ws" => "ws",
                "wss" => "wss",
                _ => throw new InvalidOperationException($"不支持的 provider websocket 协议：{uri.Scheme}", innerException: null),
            },
        };

        var endpoint = ResolveHttpEndpoint(baseUrl);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            throw new InvalidOperationException($"无效 provider base_url：{baseUrl}", innerException: null);
        }

        builder.Path = endpointUri.AbsolutePath;
        return builder.Uri;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
