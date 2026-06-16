using System.Text.Json;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

internal static class StructuredValueTestHelper
{
    internal static StructuredValue? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return FromJsonElement(document.RootElement);
    }

    private static StructuredValue FromJsonElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => StructuredValue.FromObject(
                element.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    static property => FromJsonElement(property.Value),
                    StringComparer.Ordinal)),
            JsonValueKind.Array => StructuredValue.FromArray(
                element.EnumerateArray().Select(FromJsonElement).ToArray()),
            JsonValueKind.String => StructuredValue.FromString(element.GetString() ?? string.Empty),
            JsonValueKind.Number => StructuredValue.FromNumber(element.GetRawText()),
            JsonValueKind.True => StructuredValue.FromBoolean(true),
            JsonValueKind.False => StructuredValue.FromBoolean(false),
            JsonValueKind.Null or JsonValueKind.Undefined => StructuredValue.Null,
            _ => StructuredValue.FromString(element.ToString()),
        };
}
