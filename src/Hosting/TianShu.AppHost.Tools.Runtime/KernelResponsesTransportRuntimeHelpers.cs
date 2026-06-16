using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// responses transport 的 websocket 增量输入辅助件。
/// WebSocket incremental-input helpers for responses transport.
/// </summary>
internal static class KernelResponsesTransportRuntimeHelpers
{
    public static (IReadOnlyList<JsonElement> Input, string? PreviousResponseId) BuildResponsesWebSocketRequestInput(
        IReadOnlyList<JsonElement> requestInput,
        string requestSignature,
        ResponsesWebSocketTurnSession session)
    {
        if (session.LastRequestInput is null
            || string.IsNullOrWhiteSpace(session.LastResponseId)
            || !string.Equals(session.LastRequestSignature, requestSignature, StringComparison.Ordinal))
        {
            return (CloneJsonElements(requestInput, startIndex: 0), null);
        }

        var baselineCount = session.LastRequestInput.Count + session.LastResponseItems.Count;
        if (baselineCount > requestInput.Count
            || !HasMatchingPrefix(requestInput, session.LastRequestInput, session.LastResponseItems))
        {
            return (CloneJsonElements(requestInput, startIndex: 0), null);
        }

        var delta = CloneJsonElements(requestInput, baselineCount);
        return (delta, session.LastResponseId);
    }

    public static string BuildResponsesWebSocketRequestSignature(
        IReadOnlyDictionary<string, object?> payload,
        JsonSerializerOptions jsonOptions)
    {
        var signaturePayload = new Dictionary<string, object?>(payload, StringComparer.Ordinal);
        signaturePayload.Remove("input");
        signaturePayload.Remove("previous_response_id");
        return JsonSerializer.Serialize(signaturePayload, jsonOptions);
    }

    public static IReadOnlyList<JsonElement> CloneJsonElements(IEnumerable<JsonElement> items)
        => items.Select(static item => item.Clone()).ToArray();

    private static IReadOnlyList<JsonElement> CloneJsonElements(IReadOnlyList<JsonElement> items, int startIndex)
    {
        if (startIndex >= items.Count)
        {
            return Array.Empty<JsonElement>();
        }

        var clones = new JsonElement[items.Count - startIndex];
        for (var index = 0; index < clones.Length; index++)
        {
            clones[index] = items[startIndex + index].Clone();
        }

        return clones;
    }

    private static bool HasMatchingPrefix(
        IReadOnlyList<JsonElement> requestInput,
        IReadOnlyList<JsonElement> lastRequestInput,
        IReadOnlyList<JsonElement> lastResponseItems)
    {
        var requestIndex = 0;
        foreach (var item in lastRequestInput)
        {
            if (!string.Equals(item.GetRawText(), requestInput[requestIndex].GetRawText(), StringComparison.Ordinal))
            {
                return false;
            }

            requestIndex++;
        }

        foreach (var item in lastResponseItems)
        {
            if (!string.Equals(item.GetRawText(), requestInput[requestIndex].GetRawText(), StringComparison.Ordinal))
            {
                return false;
            }

            requestIndex++;
        }

        return true;
    }

    public static ProviderResponsesTransportResponseHeaders CreateTransportResponseHeaders(HttpHeaders headers)
    {
        var values = headers.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
        return new ProviderResponsesTransportResponseHeaders(values);
    }

    public static ProviderResponsesTransportResponseHeaders CreateTransportResponseHeaders(
        IReadOnlyDictionary<string, IEnumerable<string>> headers)
    {
        var values = headers.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
        return new ProviderResponsesTransportResponseHeaders(values);
    }

    public static void ApplyTransportHeaders(HttpHeaders headers, IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }
    }

    public static void ApplyTransportHeaders(ClientWebSocketOptions options, IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            options.SetRequestHeader(pair.Key, pair.Value);
        }
    }
}
