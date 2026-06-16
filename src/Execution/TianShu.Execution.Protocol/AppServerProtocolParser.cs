using System.Text.Json;

namespace TianShu.Execution.Protocol;

internal static class AppServerProtocolParser
{
    public static bool TryParseIncoming(JsonElement root, string rawJson, out AppServerIncomingEnvelope envelope)
    {
        if (TryParseRpcResponse(root, rawJson, out var rpcResponse))
        {
            envelope = new AppServerIncomingEnvelope(AppServerIncomingKind.RpcResponse, RpcResponse: rpcResponse);
            return true;
        }

        if (TryParseServerRequest(root, rawJson, out var serverRequest))
        {
            envelope = new AppServerIncomingEnvelope(AppServerIncomingKind.ServerRequest, ServerRequest: serverRequest);
            return true;
        }

        if (TryParseNotification(root, rawJson, out var notification))
        {
            envelope = new AppServerIncomingEnvelope(AppServerIncomingKind.Notification, Notification: notification);
            return true;
        }

        envelope = new AppServerIncomingEnvelope(AppServerIncomingKind.Unknown);
        return false;
    }

    public static bool TryParseRpcResponse(JsonElement root, string rawJson, out AppServerRpcResponseEnvelope response)
    {
        response = null!;
        if (!root.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.Number
            || !idElement.TryGetInt64(out var id)
            || root.TryGetProperty("method", out _))
        {
            return false;
        }

        AppServerRpcErrorEnvelope? error = null;
        if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
        {
            var message = errorElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? errorElement.GetRawText()
                : errorElement.GetRawText();
            var code = errorElement.TryGetProperty("code", out var codeElement)
                       && codeElement.ValueKind == JsonValueKind.Number
                       && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : -32603;
            var data = errorElement.TryGetProperty("data", out var dataElement)
                ? dataElement.Clone()
                : (JsonElement?)null;
            error = new AppServerRpcErrorEnvelope(code, message, data);
        }

        var result = root.TryGetProperty("result", out var resultElement)
            ? resultElement.Clone()
            : (JsonElement?)null;

        response = new AppServerRpcResponseEnvelope(id, root.Clone(), result, error, rawJson);
        return true;
    }

    public static bool TryParseServerRequest(JsonElement root, string rawJson, out AppServerServerRequestEnvelope request)
    {
        request = null!;
        if (!root.TryGetProperty("method", out var methodElement)
            || methodElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        var method = methodElement.GetString();
        if (string.IsNullOrWhiteSpace(method))
        {
            return false;
        }

        var parameters = root.TryGetProperty("params", out var parametersElement)
            ? parametersElement.Clone()
            : default;
        request = new AppServerServerRequestEnvelope(method, idElement.Clone(), parameters, root.Clone(), rawJson);
        return true;
    }

    public static bool TryParseNotification(JsonElement root, string rawJson, out AppServerNotificationEnvelope notification)
    {
        notification = null!;
        if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var method = methodElement.GetString();
        if (string.IsNullOrWhiteSpace(method))
        {
            return false;
        }

        if (root.TryGetProperty("id", out _))
        {
            return false;
        }

        var parameters = root.TryGetProperty("params", out var parametersElement)
            ? parametersElement.Clone()
            : default;
        notification = new AppServerNotificationEnvelope(method, parameters, root.Clone(), rawJson);
        return true;
    }
}
