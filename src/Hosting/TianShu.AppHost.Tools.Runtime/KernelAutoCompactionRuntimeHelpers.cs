using System.Text.Json;
using TianShu.AppHost.Configuration;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelAutoCompactionRuntimeHelpers
{
    public static long? ResolveConfiguredModelAutoCompactTokenLimit(Dictionary<string, object?> config)
    {
        if (!TryReadValueExact(config, "model_auto_compact_token_limit", out var rawValue))
        {
            return null;
        }

        return rawValue is not null && TryReadLong(rawValue, out var configuredValue)
            ? configuredValue
            : null;
    }

    public static long EstimatePromptTokenCount(
        string? instructions,
        IReadOnlyList<Dictionary<string, object?>> messages)
    {
        var totalChars = KernelToolJsonHelpers.Normalize(instructions)?.Length ?? 0;
        foreach (var message in messages)
        {
            if (!message.TryGetValue("content", out var rawContent) || rawContent is not string content)
            {
                continue;
            }

            totalChars += KernelToolJsonHelpers.Normalize(content)?.Length ?? 0;
        }

        return Math.Max(1, totalChars / 3);
    }

    public static long EstimateResponsesFollowUpTokenCount(
        string? instructions,
        IReadOnlyList<object> input)
    {
        var totalChars = KernelToolJsonHelpers.Normalize(instructions)?.Length ?? 0;
        foreach (var item in input)
        {
            totalChars += JsonSerializer.Serialize(item).Length;
        }

        return Math.Max(1, totalChars / 3);
    }

    public static List<object> BuildResponsesFollowUpInput(
        IReadOnlyList<object> priorInput,
        IReadOnlyList<object> responseItems,
        IReadOnlyList<object> nextInput)
    {
        var input = new List<object>(priorInput.Count + responseItems.Count + nextInput.Count);
        foreach (var item in priorInput)
        {
            input.Add(item);
        }

        foreach (var item in responseItems)
        {
            input.Add(item);
        }

        foreach (var item in nextInput)
        {
            input.Add(item);
        }

        return input;
    }

    private static bool TryReadValueExact(Dictionary<string, object?> config, string propertyName, out object? value)
        => config.TryGetValue(propertyName, out value);

    private static bool TryReadLong(object? value, out long longValue)
    {
        switch (value)
        {
            case long typedLong:
                longValue = typedLong;
                return true;
            case int typedInt:
                longValue = typedInt;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var parsedLong):
                longValue = parsedLong;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out var parsedLongFromString):
                longValue = parsedLongFromString;
                return true;
            case string text when long.TryParse(text, out var parsedLongFromText):
                longValue = parsedLongFromText;
                return true;
            default:
                longValue = default;
                return false;
        }
    }
}
