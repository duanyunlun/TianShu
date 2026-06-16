using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Google;

/// <summary>
/// Google Gemini Generative 请求组合器。
/// Request composer for the Google Gemini Generative API.
/// </summary>
public sealed class GoogleGenerativeRequestComposer : IProviderResponsesRequestComposer
{
    /// <inheritdoc />
    public string WireApi => ProviderWireApi.GoogleGenerative;

    /// <inheritdoc />
    public ProviderResponsesRequestComposition Compose(ProviderResponsesRequestComposerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Model);
        ArgumentNullException.ThrowIfNull(context.Input);

        var systemParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Instructions))
        {
            systemParts.Add(context.Instructions);
        }

        var contents = BuildContents(context.Input, systemParts);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contents"] = contents.Count == 0
                ? [BuildTextContent("user", string.Empty)]
                : contents,
        };

        if (systemParts.Count > 0)
        {
            payload["systemInstruction"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["parts"] = systemParts.Select(BuildTextPart).ToArray(),
            };
        }

        if (context.Tools.Count > 0)
        {
            payload["tools"] = CloneJsonElements(context.Tools);
        }

        var thinkingConfig = BuildThinkingConfig(context);
        if (thinkingConfig is not null)
        {
            payload["generationConfig"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["thinkingConfig"] = thinkingConfig,
            };
        }

        return new ProviderResponsesRequestComposition(payload, Array.Empty<JsonElement>(), InputPropertyName: null);
    }

    private static Dictionary<string, object?>? BuildThinkingConfig(ProviderResponsesRequestComposerContext context)
    {
        if (context.ReasoningEnabled == false || !IsGeminiModel(context.Model))
        {
            return null;
        }

        var isGemini3 = IsGemini3Model(context.Model);
        var budget = isGemini3 ? null : context.ReasoningBudgetTokens ?? EffortToThinkingBudget(context.ReasoningEffort);
        var thinkingLevel = isGemini3 ? EffortToThinkingLevel(context.ReasoningEffort) : null;
        if (budget is null && string.IsNullOrWhiteSpace(thinkingLevel) && string.IsNullOrWhiteSpace(context.ReasoningSummary))
        {
            return null;
        }

        var thinkingConfig = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(thinkingLevel))
        {
            thinkingConfig["thinkingLevel"] = thinkingLevel;
        }

        if (budget is > 0)
        {
            thinkingConfig["thinkingBudget"] = budget.Value;
        }

        if (!string.Equals(context.ReasoningSummary, "off", StringComparison.OrdinalIgnoreCase))
        {
            thinkingConfig["includeThoughts"] = true;
        }

        return thinkingConfig.Count == 0 ? null : thinkingConfig;
    }

    private static int? EffortToThinkingBudget(string? effort)
        => effort?.Trim().ToLowerInvariant() switch
        {
            "low" => 1024,
            "medium" => 4096,
            "high" => 8192,
            "xhigh" => 16384,
            _ => null,
        };

    private static bool IsGeminiModel(string model)
        => model.Contains("gemini", StringComparison.OrdinalIgnoreCase);

    private static bool IsGemini3Model(string model)
        => model.Contains("gemini-3", StringComparison.OrdinalIgnoreCase);

    private static string? EffortToThinkingLevel(string? effort)
        => effort?.Trim().ToLowerInvariant() switch
        {
            "low" => "low",
            "medium" or "high" or "xhigh" => "high",
            _ => "high",
        };

    private static List<Dictionary<string, object?>> BuildContents(
        IReadOnlyList<JsonElement> input,
        List<string> systemParts)
    {
        var contents = new List<Dictionary<string, object?>>();
        var callNameById = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var item in input)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = ReadString(item, "type");
            if (string.Equals(type, "function_call", StringComparison.OrdinalIgnoreCase))
            {
                if (TryBuildFunctionCallContent(item, out var callId, out var name, out var functionCallContent))
                {
                    callNameById[callId!] = name!;
                    contents.Add(functionCallContent);
                }

                continue;
            }

            if (string.Equals(type, "function_call_output", StringComparison.OrdinalIgnoreCase))
            {
                if (TryBuildFunctionResponseContent(item, callNameById, out var functionResponseContent))
                {
                    contents.Add(functionResponseContent);
                }

                continue;
            }

            if (!string.Equals(type, "message", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var role = NormalizeRole(ReadString(item, "role"));
            var textParts = BuildTextParts(item);
            if (textParts.Count == 0)
            {
                continue;
            }

            if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
            {
                systemParts.AddRange(textParts.Select(static part => part.TryGetValue("text", out var text) ? text as string : null)
                    .Where(static text => !string.IsNullOrWhiteSpace(text))!);
                continue;
            }

            contents.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["role"] = role,
                ["parts"] = textParts,
            });
        }

        return contents;
    }

    private static Dictionary<string, object?> BuildTextContent(string role, string text)
        => new(StringComparer.Ordinal)
        {
            ["role"] = role,
            ["parts"] = new[] { BuildTextPart(text) },
        };

    private static bool TryBuildFunctionCallContent(
        JsonElement item,
        out string? callId,
        out string? name,
        out Dictionary<string, object?> content)
    {
        content = new Dictionary<string, object?>(StringComparer.Ordinal);
        callId = ReadString(item, "call_id");
        name = ReadString(item, "name");
        var arguments = ReadString(item, "arguments") ?? "{}";
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        callId = string.IsNullOrWhiteSpace(callId) ? name : callId;
        var functionCall = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = name,
            ["args"] = DeserializeJsonObject(arguments),
        };

        content["role"] = "model";
        content["parts"] = new[]
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["functionCall"] = functionCall,
            },
        };
        return true;
    }

    private static bool TryBuildFunctionResponseContent(
        JsonElement item,
        IReadOnlyDictionary<string, string> callNameById,
        out Dictionary<string, object?> content)
    {
        content = new Dictionary<string, object?>(StringComparer.Ordinal);
        var callId = ReadString(item, "call_id");
        if (string.IsNullOrWhiteSpace(callId))
        {
            return false;
        }

        var name = ReadString(item, "name")
            ?? ReadString(item, "tool_name")
            ?? (callNameById.TryGetValue(callId!, out var mappedName) ? mappedName : null)
            ?? callId;
        content["role"] = "user";
        content["parts"] = new[]
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["functionResponse"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = name,
                    ["response"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["output"] = ReadToolOutput(item),
                    },
                },
            },
        };
        return true;
    }

    private static IReadOnlyList<Dictionary<string, object?>> BuildTextParts(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return [];
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            return string.IsNullOrWhiteSpace(text) ? [] : [BuildTextPart(text!)];
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var parts = new List<Dictionary<string, object?>>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var text = ReadString(part, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(BuildTextPart(text!));
            }
        }

        return parts;
    }

    private static Dictionary<string, object?> BuildTextPart(string text)
        => new(StringComparer.Ordinal)
        {
            ["text"] = text,
        };

    private static string NormalizeRole(string? role)
        => role?.Trim().ToLowerInvariant() switch
        {
            "assistant" => "model",
            "model" => "model",
            "developer" or "system" => "system",
            _ => "user",
        };

    private static object? DeserializeJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = json,
            };
        }
    }

    private static string ReadToolOutput(JsonElement item)
    {
        if (!item.TryGetProperty("output", out var output))
        {
            return string.Empty;
        }

        return output.ValueKind == JsonValueKind.String
            ? output.GetString() ?? string.Empty
            : output.GetRawText();
    }

    private static IReadOnlyList<object?> CloneJsonElements(IReadOnlyList<JsonElement> elements)
        => elements
            .Select(static element => JsonSerializer.Deserialize<object?>(element.GetRawText()))
            .ToArray();

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
