using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.Contracts.Primitives;

/// <summary>
/// 跨域共享的结构化值模型，用于在 typed-first Contracts 中承载轻量级树状数据。
/// Cross-domain structured value used to carry lightweight tree-shaped data in typed-first contracts.
/// </summary>
[JsonConverter(typeof(StructuredValueJsonConverter))]
public sealed class StructuredValue
{
    private static readonly IReadOnlyDictionary<string, StructuredValue> EmptyProperties =
        new Dictionary<string, StructuredValue>(StringComparer.Ordinal);

    private static readonly IReadOnlyList<StructuredValue> EmptyItems = Array.Empty<StructuredValue>();

    private StructuredValue(
        StructuredValueKind kind,
        string? stringValue = null,
        string? numberValue = null,
        bool? booleanValue = null,
        IReadOnlyDictionary<string, StructuredValue>? properties = null,
        IReadOnlyList<StructuredValue>? items = null)
    {
        Kind = kind;
        StringValue = stringValue;
        NumberValue = numberValue;
        BooleanValue = booleanValue;
        Properties = properties ?? EmptyProperties;
        Items = items ?? EmptyItems;
    }

    public StructuredValueKind Kind { get; }

    public string? StringValue { get; }

    public string? NumberValue { get; }

    public bool? BooleanValue { get; }

    public IReadOnlyDictionary<string, StructuredValue> Properties { get; }

    public IReadOnlyList<StructuredValue> Items { get; }

    /// <summary>
    /// 空值单例，表示显式的结构化空值。
    /// Null singleton that represents an explicit structured null.
    /// </summary>
    public static StructuredValue Null { get; } = new(StructuredValueKind.Null);

    /// <summary>
    /// 从字符串创建结构化值。
    /// Creates a structured value from a string.
    /// </summary>
    public static StructuredValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new StructuredValue(StructuredValueKind.String, stringValue: value);
    }

    /// <summary>
    /// 从数字字符串创建结构化值。
    /// Creates a structured value from an invariant numeric string.
    /// </summary>
    public static StructuredValue FromNumber(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new StructuredValue(StructuredValueKind.Number, numberValue: value);
    }

    /// <summary>
    /// 从布尔值创建结构化值。
    /// Creates a structured value from a boolean.
    /// </summary>
    public static StructuredValue FromBoolean(bool value) => new(StructuredValueKind.Boolean, booleanValue: value);

    /// <summary>
    /// 从对象属性字典创建结构化值。
    /// Creates a structured value from an object property map.
    /// </summary>
    public static StructuredValue FromObject(IReadOnlyDictionary<string, StructuredValue>? properties)
        => new(StructuredValueKind.Object, properties: properties);

    /// <summary>
    /// 从数组项集合创建结构化值。
    /// Creates a structured value from an array item collection.
    /// </summary>
    public static StructuredValue FromArray(IReadOnlyList<StructuredValue>? items)
        => new(StructuredValueKind.Array, items: items);

    /// <summary>
    /// 从 <see cref="JsonElement"/> 递归转换为结构化值。
    /// Recursively converts a <see cref="JsonElement"/> into a structured value.
    /// </summary>
    public static StructuredValue FromJsonElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => FromObject(
                element.EnumerateObject()
                    .ToDictionary(
                        static property => property.Name,
                        static property => FromJsonElement(property.Value),
                        StringComparer.Ordinal)),
            JsonValueKind.Array => FromArray(element.EnumerateArray().Select(FromJsonElement).ToArray()),
            JsonValueKind.String => FromString(element.GetString() ?? string.Empty),
            JsonValueKind.Number => FromNumber(element.GetRawText()),
            JsonValueKind.True => FromBoolean(true),
            JsonValueKind.False => FromBoolean(false),
            JsonValueKind.Null => Null,
            _ => FromString(element.GetRawText()),
        };

    /// <summary>
    /// 从普通 CLR 对象递归转换为结构化值。
    /// Recursively converts a plain CLR object into a structured value.
    /// </summary>
    public static StructuredValue FromPlainObject(object? value)
    {
        if (value is null)
        {
            return Null;
        }

        if (value is StructuredValue structuredValue)
        {
            return structuredValue;
        }

        if (value is JsonElement jsonElement)
        {
            return FromJsonElement(jsonElement);
        }

        if (value is string text)
        {
            return FromString(text);
        }

        if (value is bool booleanValue)
        {
            return FromBoolean(booleanValue);
        }

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return FromNumber(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0");
        }

        if (value is IReadOnlyDictionary<string, StructuredValue> structuredDictionary)
        {
            return FromObject(structuredDictionary);
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return FromObject(readOnlyDictionary.ToDictionary(
                static pair => pair.Key,
                static pair => FromPlainObject(pair.Value),
                StringComparer.Ordinal));
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            return FromObject(dictionary.ToDictionary(
                static pair => pair.Key,
                static pair => FromPlainObject(pair.Value),
                StringComparer.Ordinal));
        }

        if (value is IEnumerable<KeyValuePair<string, object?>> enumerablePairs)
        {
            return FromObject(enumerablePairs.ToDictionary(
                static pair => pair.Key,
                static pair => FromPlainObject(pair.Value),
                StringComparer.Ordinal));
        }

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            var items = new List<StructuredValue>();
            foreach (var item in enumerable)
            {
                items.Add(FromPlainObject(item));
            }

            return FromArray(items);
        }

        return FromString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }

    /// <summary>
    /// 将结构化值还原为普通 CLR 对象，便于序列化或测试断言。
    /// Converts the structured value back into a plain CLR object for serialization or assertions.
    /// </summary>
    public object? ToPlainObject()
        => Kind switch
        {
            StructuredValueKind.Object => Properties.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToPlainObject(),
                StringComparer.Ordinal),
            StructuredValueKind.Array => Items.Select(static item => item.ToPlainObject()).ToArray(),
            StructuredValueKind.String => StringValue,
            StructuredValueKind.Number => ParseNumber(NumberValue),
            StructuredValueKind.Boolean => BooleanValue,
            StructuredValueKind.Null => null,
            _ => null,
        };

    /// <summary>
    /// 读取对象属性，若当前值不是对象或属性不存在则抛出异常。
    /// Reads an object property and throws when the current value is not an object or the property is missing.
    /// </summary>
    public StructuredValue GetProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (Kind != StructuredValueKind.Object)
        {
            throw new InvalidOperationException($"Structured value is {Kind}, not Object.");
        }

        if (!Properties.TryGetValue(propertyName, out var propertyValue))
        {
            throw new KeyNotFoundException($"Structured value does not contain property '{propertyName}'.");
        }

        return propertyValue;
    }

    /// <summary>
    /// 尝试读取对象属性。
    /// Attempts to read an object property.
    /// </summary>
    public bool TryGetProperty(string propertyName, out StructuredValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (Kind == StructuredValueKind.Object && Properties.TryGetValue(propertyName, out var propertyValue))
        {
            value = propertyValue;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// 将标量结构化值读取为文本表示。
    /// Reads a scalar structured value as its textual representation.
    /// </summary>
    public string? GetString()
        => Kind switch
        {
            StructuredValueKind.String => StringValue,
            StructuredValueKind.Number => NumberValue,
            StructuredValueKind.Boolean => BooleanValue?.ToString(),
            StructuredValueKind.Null => null,
            _ => throw new InvalidOperationException($"Structured value is {Kind}, not scalar text."),
        };

    /// <summary>
    /// 将结构化值读取为布尔值。
    /// Reads the structured value as a boolean.
    /// </summary>
    public bool GetBoolean()
        => Kind == StructuredValueKind.Boolean && BooleanValue.HasValue
            ? BooleanValue.Value
            : throw new InvalidOperationException($"Structured value is {Kind}, not Boolean.");

    /// <summary>
    /// 将结构化值读取为 32 位整数。
    /// Reads the structured value as a 32-bit integer.
    /// </summary>
    public int GetInt32()
    {
        if (Kind != StructuredValueKind.Number
            || !int.TryParse(NumberValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"Structured value is {Kind}, not Int32.");
        }

        return value;
    }

    private static object? ParseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return decimalValue;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        return value;
    }
}

/// <summary>
/// 结构化值的离散种类。
/// Discrete kinds supported by the structured value model.
/// </summary>
public enum StructuredValueKind
{
    Null = 0,
    Object = 1,
    Array = 2,
    String = 3,
    Number = 4,
    Boolean = 5,
}

/// <summary>
/// `StructuredValue` 的 JSON 转换器，负责在原生 JSON 树与 typed-first 结构化值之间做双向映射。
/// JSON converter for `StructuredValue` that bridges native JSON trees and the typed-first structured value model.
/// </summary>
internal sealed class StructuredValueJsonConverter : JsonConverter<StructuredValue>
{
    public override StructuredValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return StructuredValue.FromJsonElement(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, StructuredValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        WriteStructuredValue(writer, value);
    }

    private static void WriteStructuredValue(Utf8JsonWriter writer, StructuredValue value)
    {
        switch (value.Kind)
        {
            case StructuredValueKind.Null:
                writer.WriteNullValue();
                return;
            case StructuredValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.Properties)
                {
                    writer.WritePropertyName(property.Key);
                    WriteStructuredValue(writer, property.Value);
                }

                writer.WriteEndObject();
                return;
            case StructuredValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.Items)
                {
                    WriteStructuredValue(writer, item);
                }

                writer.WriteEndArray();
                return;
            case StructuredValueKind.String:
                writer.WriteStringValue(value.StringValue);
                return;
            case StructuredValueKind.Number:
                writer.WriteRawValue(value.NumberValue ?? "0", skipInputValidation: true);
                return;
            case StructuredValueKind.Boolean:
                writer.WriteBooleanValue(value.BooleanValue ?? false);
                return;
            default:
                throw new JsonException($"不支持的 StructuredValueKind：{value.Kind}");
        }
    }
}
