using System.Text;
using System.Text.Json;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Accumulates provider replay thinking blocks for tool-use follow-up requests.
/// 累计 provider replay thinking block，供工具调用 follow-up 请求回放。
/// </summary>
public sealed class ProviderResponsesThinkingBlockAccumulator
{
    private readonly string type;
    private readonly string? redactedData;
    private readonly StringBuilder thinking = new();
    private readonly StringBuilder signature = new();

    public ProviderResponsesThinkingBlockAccumulator(JsonElement contentBlock)
    {
        type = ReadString(contentBlock, "type") ?? "thinking";
        redactedData = ReadString(contentBlock, "data");
        AppendThinking(ReadString(contentBlock, "thinking"));
        AppendSignature(ReadString(contentBlock, "signature"));
    }

    public void AppendThinking(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            thinking.Append(value);
        }
    }

    public void AppendSignature(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            signature.Append(value);
        }
    }

    public JsonElement ToJsonElement()
    {
        var block = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = type,
        };

        if (string.Equals(type, "redacted_thinking", StringComparison.OrdinalIgnoreCase))
        {
            block["data"] = redactedData ?? thinking.ToString();
        }
        else
        {
            block["thinking"] = thinking.ToString();
            var signatureText = signature.ToString();
            if (!string.IsNullOrWhiteSpace(signatureText))
            {
                block["signature"] = signatureText;
            }
        }

        return JsonSerializer.SerializeToElement(block);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}

/// <summary>
/// Accumulates provider replay tool-use blocks.
/// 累计 provider replay tool-use block。
/// </summary>
public sealed class ProviderResponsesToolUseBlockAccumulator(string id, string name)
{
    private readonly StringBuilder partialJson = new();

    public void AppendPartialJson(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            partialJson.Append(value);
        }
    }

    public JsonElement ToFunctionCallJsonElement(IReadOnlyList<JsonElement> thinkingBlocks)
    {
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "function_call",
            ["call_id"] = id,
            ["name"] = name,
            ["arguments"] = NormalizeArguments(partialJson.ToString()),
        };

        if (thinkingBlocks.Count > 0)
        {
            item["thinking_blocks"] = thinkingBlocks.Select(static block => block.Clone()).ToArray();
        }

        return JsonSerializer.SerializeToElement(item);
    }

    private static string NormalizeArguments(string arguments)
        => string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments;
}
