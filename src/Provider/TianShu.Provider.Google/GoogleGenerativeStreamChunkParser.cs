using System.Text;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Google;

/// <summary>
/// Parses Google Generative stream chunks into TianShu canonical stream chunks.
/// 将 Google Generative 流式 chunk 解析为 TianShu 规范化 chunk。
/// </summary>
public sealed class GoogleGenerativeStreamChunkParser : IProviderResponsesStreamChunkParser
{
    public bool TryReadChunk(JsonElement root, out ProviderResponsesStreamChunk chunk)
    {
        chunk = new ProviderResponsesStreamChunk();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            throw new ProviderResponsesStreamParseException(
                BuildErrorMessage(error),
                IsRetryableError(error));
        }

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var text = new StringBuilder();
        var calls = new List<JsonElement>();
        var completed = false;
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ReadString(candidate, "finishReason")))
            {
                completed = true;
            }

            if (!candidate.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Object
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                var partText = ReadString(part, "text");
                if (!string.IsNullOrEmpty(partText))
                {
                    text.Append(partText);
                }

                if (part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("functionCall", out var functionCall)
                    && TryBuildFunctionCall(functionCall, out var call))
                {
                    calls.Add(call);
                }
            }
        }

        chunk = new ProviderResponsesStreamChunk(
            TextDelta: text.Length == 0 ? null : text.ToString(),
            FunctionCalls: calls,
            Completed: completed);
        return true;
    }

    private static bool TryBuildFunctionCall(JsonElement functionCall, out JsonElement call)
    {
        call = default;
        if (functionCall.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var name = ReadString(functionCall, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var callId = ReadString(functionCall, "id") ?? name;
        var arguments = functionCall.TryGetProperty("args", out var args) && args.ValueKind != JsonValueKind.Undefined
            ? args.GetRawText()
            : "{}";
        call = JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "function_call",
            ["call_id"] = callId,
            ["name"] = name,
            ["arguments"] = string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments,
        });
        return true;
    }

    private static string BuildErrorMessage(JsonElement error)
    {
        var message = ReadString(error, "message")?.Trim();
        var code = ReadInt(error, "code");
        var status = ReadString(error, "status")?.Trim();
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(message))
        {
            parts.Add(message!);
        }

        if (code is not null)
        {
            parts.Add($"code={code.Value}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            parts.Add($"status={status}");
        }

        return parts.Count == 0
            ? $"google_generative error: {error.GetRawText()}"
            : $"google_generative error: {string.Join(", ", parts)}";
    }

    private static bool IsRetryableError(JsonElement error)
    {
        var code = ReadInt(error, "code");
        var status = ReadString(error, "status")?.Trim();
        return code is >= 500
               || string.Equals(status, "UNAVAILABLE", StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => null,
        };
    }
}
