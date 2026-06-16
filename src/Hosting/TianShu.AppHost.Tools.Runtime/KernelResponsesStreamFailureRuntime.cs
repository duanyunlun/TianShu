using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Responses stream 失败运行时，负责把 provider stream / transport failure 归类为可审计的重试决策输入。
/// Runtime that classifies provider response stream and transport failures for retry decisions.
/// </summary>
internal sealed class KernelResponsesStreamFailureRuntime
{
    public ProviderResponsesTransportFailure ClassifyHttpStreamFailure(
        Exception ex,
        CancellationToken cancellationToken)
    {
        return ex switch
        {
            KernelResponsesStreamException streamException => new(
                ProviderResponsesTransportFailureKind.ProviderStreamFailure,
                streamException.IsRetryable,
                ex.Message),
            HttpRequestException => new(
                ProviderResponsesTransportFailureKind.HttpRequestFailure,
                IsRetryable: true,
                ex.Message),
            IOException => new(
                ProviderResponsesTransportFailureKind.IoFailure,
                IsRetryable: true,
                ex.Message),
            TimeoutException => new(
                ProviderResponsesTransportFailureKind.Timeout,
                IsRetryable: true,
                ex.Message),
            TaskCanceledException => new(
                ProviderResponsesTransportFailureKind.OperationCanceled,
                IsRetryable: !cancellationToken.IsCancellationRequested,
                ex.Message),
            OperationCanceledException => new(
                ProviderResponsesTransportFailureKind.OperationCanceled,
                IsRetryable: !cancellationToken.IsCancellationRequested,
                ex.Message),
            _ => new(
                ProviderResponsesTransportFailureKind.Unknown,
                IsRetryable: false,
                ex.Message),
        };
    }

    public ProviderResponsesTransportFailure ClassifyWebSocketFailure(
        Exception ex,
        CancellationToken cancellationToken)
    {
        return ex switch
        {
            KernelResponsesWebSocketUpgradeRequiredException => new(
                ProviderResponsesTransportFailureKind.WebSocketUpgradeRequired,
                IsRetryable: false,
                ex.Message),
            KernelResponsesStreamException streamException => new(
                ProviderResponsesTransportFailureKind.ProviderStreamFailure,
                streamException.IsRetryable,
                ex.Message),
            WebSocketException => new(
                ProviderResponsesTransportFailureKind.WebSocketTransportFailure,
                IsRetryable: !cancellationToken.IsCancellationRequested,
                ex.Message),
            IOException => new(
                ProviderResponsesTransportFailureKind.IoFailure,
                IsRetryable: !cancellationToken.IsCancellationRequested,
                ex.Message),
            TimeoutException => new(
                ProviderResponsesTransportFailureKind.Timeout,
                IsRetryable: !cancellationToken.IsCancellationRequested,
                ex.Message),
            TaskCanceledException => new(
                ProviderResponsesTransportFailureKind.OperationCanceled,
                IsRetryable: !cancellationToken.IsCancellationRequested,
                ex.Message),
            OperationCanceledException => new(
                ProviderResponsesTransportFailureKind.OperationCanceled,
                IsRetryable: !cancellationToken.IsCancellationRequested,
                ex.Message),
            _ => new(
                ProviderResponsesTransportFailureKind.Unknown,
                IsRetryable: false,
                ex.Message),
        };
    }

    public Exception CreateStreamException(string kind, JsonElement root)
        => new KernelResponsesStreamException(
            BuildStreamFailureMessage(kind, root),
            IsRetryableStreamFailure(root));

    private static string BuildStreamFailureMessage(string kind, JsonElement root)
    {
        if (TryBuildStreamFailureMessageFromObject(kind, root, out var directMessage))
        {
            return directMessage!;
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("response", out var response)
            && TryBuildStreamFailureMessageFromObject(kind, response, out var nestedMessage))
        {
            return nestedMessage!;
        }

        var raw = root.GetRawText();
        var brief = raw.Length > 240 ? $"{raw[..240]}..." : raw;
        return $"{kind} event received: {brief}";
    }

    private static bool IsRetryableStreamFailure(JsonElement root)
    {
        if (TryGetStreamFailureError(root, out var code, out var type))
        {
            return IsRetryableStreamFailureCode(code, type);
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("response", out var response)
            && TryGetStreamFailureError(response, out code, out type))
        {
            return IsRetryableStreamFailureCode(code, type);
        }

        return false;
    }

    private static bool TryGetStreamFailureError(JsonElement container, out string? code, out string? type)
    {
        code = null;
        type = null;

        if (container.ValueKind != JsonValueKind.Object
            || !container.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        code = Normalize(ReadString(error, "code"));
        type = Normalize(ReadString(error, "type"));
        return !string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(type);
    }

    private static bool IsRetryableStreamFailureCode(string? code, string? type)
        => string.Equals(Normalize(code), "server_error", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Normalize(type), "server_error", StringComparison.OrdinalIgnoreCase);

    private static bool TryBuildStreamFailureMessageFromObject(
        string kind,
        JsonElement container,
        out string? message)
    {
        message = null;
        if (container.ValueKind != JsonValueKind.Object
            || !container.TryGetProperty("error", out var error))
        {
            return false;
        }

        if (error.ValueKind == JsonValueKind.Object)
        {
            var errorMessage = Normalize(ReadString(error, "message"));
            var code = Normalize(ReadString(error, "code"));
            var type = Normalize(ReadString(error, "type"));

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                parts.Add(errorMessage);
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                parts.Add($"code={code}");
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                parts.Add($"type={type}");
            }

            if (parts.Count == 0)
            {
                return false;
            }

            message = $"{kind}: {string.Join(", ", parts)}";
            return true;
        }

        if (error.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var rawMessage = Normalize(error.GetString());
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return false;
        }

        message = $"{kind}: {rawMessage}";
        return true;
    }

    private static string? ReadString(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => current.ToString(),
            _ => null,
        };
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class KernelResponsesWebSocketUpgradeRequiredException : Exception
{
    public KernelResponsesWebSocketUpgradeRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
