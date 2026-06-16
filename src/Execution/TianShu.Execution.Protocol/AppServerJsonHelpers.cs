using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TianShu.Execution.Protocol;

internal static class AppServerJsonHelpers
{
    internal static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static T? Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] T>(
        JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return default;
        }

        var typeInfo = AppServerJsonSerializerContext.Default.GetTypeInfo(typeof(T));
        if (typeInfo is JsonTypeInfo<T> typedTypeInfo)
        {
            return JsonSerializer.Deserialize(element.GetRawText(), typedTypeInfo);
        }

        return JsonSerializer.Deserialize<T>(element.GetRawText(), SerializerOptions);
    }

    public static JsonElement? TryCloneProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.Clone();
    }
}
