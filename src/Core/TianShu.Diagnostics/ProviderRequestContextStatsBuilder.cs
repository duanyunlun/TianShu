using System.Text.Json;
using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// Provider 请求上下文统计构建器，只观察 wire payload，不改变请求内容。
/// Provider request context stats builder; observes wire payloads without mutating them.
/// </summary>
public static class ProviderRequestContextStatsBuilder
{
    public static ProviderRequestContextStats Build(
        IReadOnlyDictionary<string, object?> payload,
        ProviderRequestContextStatsBuildOptions options,
        JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        var serializedPayloadChars = JsonSerializer.Serialize(payload, jsonOptions).Length;
        var instructions = BuildTextFieldStats(payload, "instructions")
                           ?? BuildTextFieldStats(payload, "system")
                           ?? BuildTextFieldStats(payload, "system_instruction")
                           ?? BuildTextFieldStats(payload, "systemInstruction");
        var input = BuildCollectionStats(payload, options.InputPropertyName)
                    ?? BuildCollectionStats(payload, "input")
                    ?? BuildCollectionStats(payload, "messages")
                    ?? BuildCollectionStats(payload, "contents");
        var tools = BuildToolsCollectionStats(payload);

        return new ProviderRequestContextStats
        {
            ThreadId = options.ThreadId,
            TurnId = options.TurnId,
            RequestSequence = options.RequestSequence,
            Model = options.Model,
            Provider = options.Provider,
            Transport = options.Transport,
            InputPropertyName = options.InputPropertyName,
            TopLevelKeys = payload.Keys
                .Where(static key => !IsSensitiveKey(key))
                .Order(StringComparer.Ordinal)
                .ToArray(),
            SerializedPayloadChars = serializedPayloadChars,
            EstimatedPayloadTokens = EstimateTokens(serializedPayloadChars),
            Instructions = instructions,
            Input = input,
            Tools = tools,
            PayloadArtifact = options.PayloadArtifact,
        };
    }

    private static ProviderRequestTextFieldStats? BuildTextFieldStats(IReadOnlyDictionary<string, object?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value))
        {
            return null;
        }

        var chars = CountTextChars(value);
        return new ProviderRequestTextFieldStats
        {
            Key = key,
            Chars = chars,
            EstimatedTokens = EstimateTokens(chars),
        };
    }

    private static ProviderRequestCollectionStats? BuildCollectionStats(IReadOnlyDictionary<string, object?> payload, string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !payload.TryGetValue(key, out var value))
        {
            return null;
        }

        var items = EnumerateItems(value).ToArray();
        var itemStats = items
            .Select((item, index) => BuildInputItemStats(index, item))
            .ToArray();
        var chars = CountTextChars(value);

        return new ProviderRequestCollectionStats
        {
            Key = key,
            Count = items.Length,
            Chars = chars,
            EstimatedTokens = EstimateTokens(chars),
            Items = itemStats,
        };
    }

    private static ProviderRequestCollectionStats? BuildToolsCollectionStats(IReadOnlyDictionary<string, object?> payload)
    {
        const string key = "tools";
        if (!payload.TryGetValue(key, out var value))
        {
            return null;
        }

        var items = EnumerateToolItems(value).ToArray();
        var itemStats = items
            .Select((item, index) => BuildInputItemStats(index, item))
            .ToArray();
        var chars = CountTextChars(value);

        return new ProviderRequestCollectionStats
        {
            Key = key,
            Count = items.Length,
            Chars = chars,
            EstimatedTokens = EstimateTokens(chars),
            Items = itemStats,
        };
    }

    private static IEnumerable<object?> EnumerateToolItems(object? value)
    {
        foreach (var item in EnumerateItems(value))
        {
            var declarations = ReadProperty(item, "functionDeclarations");
            var flattened = false;
            foreach (var declaration in EnumerateItems(declarations))
            {
                flattened = true;
                yield return declaration;
            }

            if (!flattened)
            {
                yield return item;
            }
        }
    }

    private static ProviderRequestItemStats BuildInputItemStats(int index, object? item)
    {
        var role = ReadStringProperty(item, "role");
        var type = ReadStringProperty(item, "type");
        var content = ReadProperty(item, "content");
        var textChars = CountTextChars(item);

        return new ProviderRequestItemStats
        {
            Index = index,
            Role = role,
            Type = type,
            ContentItemCount = CountItems(content),
            Chars = textChars,
            EstimatedTokens = EstimateTokens(textChars),
        };
    }

    private static IEnumerable<object?> EnumerateItems(object? value)
    {
        if (value is null)
        {
            yield break;
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (value is IEnumerable<JsonElement> jsonElements)
        {
            foreach (var item in jsonElements)
            {
                yield return item;
            }

            yield break;
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                yield return item;
            }
        }
    }

    private static int CountItems(object? value)
        => EnumerateItems(value).Count();

    private static object? ReadProperty(object? value, string key)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var property)
                ? property
                : null;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary.TryGetValue(key, out var property) ? property : null;
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            return dictionary.TryGetValue(key, out var property) ? property : null;
        }

        return null;
    }

    private static string? ReadStringProperty(object? value, string key)
    {
        var property = ReadProperty(value, key);
        return property switch
        {
            string text => string.IsNullOrWhiteSpace(text) ? null : text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };
    }

    private static int CountTextChars(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value is string text)
        {
            return text.Length;
        }

        if (value is JsonElement element)
        {
            return CountJsonElementTextChars(element);
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary
                .Where(static pair => !IsSensitiveKey(pair.Key))
                .Sum(static pair => CountTextChars(pair.Value));
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            return dictionary
                .Where(static pair => !IsSensitiveKey(pair.Key))
                .Sum(static pair => CountTextChars(pair.Value));
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            var count = 0;
            foreach (var item in enumerable)
            {
                count += CountTextChars(item);
            }

            return count;
        }

        return 0;
    }

    private static int CountJsonElementTextChars(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()?.Length ?? 0,
            JsonValueKind.Object => element.EnumerateObject()
                .Where(static property => !IsSensitiveKey(property.Name))
                .Sum(static property => CountJsonElementTextChars(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Sum(CountJsonElementTextChars),
            _ => 0,
        };
    }

    private static int EstimateTokens(int chars)
        => chars <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(chars / 3.0d));

    private static bool IsSensitiveKey(string key)
        => key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
           || key.Contains("api_key", StringComparison.OrdinalIgnoreCase)
           || key.Contains("apikey", StringComparison.OrdinalIgnoreCase)
           || key.Contains("token", StringComparison.OrdinalIgnoreCase)
           || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || key.Contains("cookie", StringComparison.OrdinalIgnoreCase);
}
