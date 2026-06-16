using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Google;

/// <summary>
/// Google Generative HTTP transport binding。
/// HTTP transport binding for Google Generative.
/// </summary>
public sealed class GoogleGenerativeTransportProtocolBinding : IProviderResponsesTransportProtocolBinding
{
    private const string TraceParentHeaderName = "traceparent";

    /// <inheritdoc />
    public string WireApi => ProviderWireApi.GoogleGenerative;

    /// <inheritdoc />
    public ProviderResponsesTransportHttpRequestBinding CreateHttpRequestBinding(
        string baseUrl,
        ProviderResponsesTransportHttpRequestContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Model);

        var endpoint = ResolveHttpEndpoint(baseUrl, context.Model!, context.ApiKey, context.Kind);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (context.Kind == ProviderResponsesTransportHttpRequestKind.StreamRequest)
        {
            headers["Accept"] = "text/event-stream";
        }

        if (!string.IsNullOrWhiteSpace(context.TraceParent))
        {
            headers[TraceParentHeaderName] = context.TraceParent;
        }

        return new ProviderResponsesTransportHttpRequestBinding(endpoint, headers);
    }

    /// <inheritdoc />
    public ProviderResponsesTransportWebSocketConnectionBinding CreateWebSocketConnectionBinding(
        string baseUrl,
        ProviderResponsesTransportWebSocketConnectContext context)
        => throw new NotSupportedException("Google Generative 不支持 Responses websocket transport。");

    /// <inheritdoc />
    public ProviderResponsesTransportWebSocketRequestBinding CreateWebSocketRequestBinding(
        ProviderResponsesTransportWebSocketRequestContext context)
        => throw new NotSupportedException("Google Generative 不支持 Responses websocket transport。");

    /// <inheritdoc />
    public string? ReadStickyTurnState(ProviderResponsesTransportResponseHeaders headers)
        => null;

    private static string ResolveHttpEndpoint(
        string baseUrl,
        string model,
        string apiKey,
        ProviderResponsesTransportHttpRequestKind kind)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var method = kind == ProviderResponsesTransportHttpRequestKind.StreamRequest
            ? "streamGenerateContent"
            : "generateContent";
        var endpoint = trimmed.EndsWith(":generateContent", StringComparison.OrdinalIgnoreCase)
                       || trimmed.EndsWith(":streamGenerateContent", StringComparison.OrdinalIgnoreCase)
            ? ReplaceMethod(trimmed, method)
            : ProviderEndpointPathUtilities.ResolveVersionedEndpoint(
                trimmed,
                "v1beta",
                NormalizeModelPath(model)) + $":{method}";

        endpoint = AppendQuery(endpoint, "key", apiKey);
        return kind == ProviderResponsesTransportHttpRequestKind.StreamRequest
            ? AppendQuery(endpoint, "alt", "sse")
            : endpoint;
    }

    private static string ReplaceMethod(string endpoint, string method)
    {
        var index = endpoint.LastIndexOf(':');
        return index < 0 ? endpoint : endpoint[..(index + 1)] + method;
    }

    private static string NormalizeModelPath(string model)
    {
        var normalized = model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? model
            : "models/" + model;
        return Uri.EscapeDataString(normalized).Replace("%2F", "/", StringComparison.Ordinal);
    }

    private static string AppendQuery(string endpoint, string key, string value)
    {
        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return endpoint + separator + Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);
    }
}
