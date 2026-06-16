using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Runtime 内部使用的结构化值模型，用于承载 app-server 返回的树状载荷。
/// Runtime-internal structured value used to carry tree-shaped payloads returned by app-server.
/// </summary>
[JsonConverter(typeof(AgentStructuredValueJsonConverter))]
public sealed class AgentStructuredValue
{
    private static readonly IReadOnlyDictionary<string, AgentStructuredValue> EmptyProperties =
        new Dictionary<string, AgentStructuredValue>(StringComparer.Ordinal);

    private static readonly IReadOnlyList<AgentStructuredValue> EmptyItems = Array.Empty<AgentStructuredValue>();

    private AgentStructuredValue(
        AgentStructuredValueKind kind,
        string? stringValue = null,
        string? numberValue = null,
        bool? booleanValue = null,
        IReadOnlyDictionary<string, AgentStructuredValue>? properties = null,
        IReadOnlyList<AgentStructuredValue>? items = null)
    {
        Kind = kind;
        StringValue = stringValue;
        NumberValue = numberValue;
        BooleanValue = booleanValue;
        Properties = properties ?? EmptyProperties;
        Items = items ?? EmptyItems;
    }

    public AgentStructuredValueKind Kind { get; }

    public string? StringValue { get; }

    public string? NumberValue { get; }

    public bool? BooleanValue { get; }

    public IReadOnlyDictionary<string, AgentStructuredValue> Properties { get; }

    public IReadOnlyList<AgentStructuredValue> Items { get; }

    public static AgentStructuredValue Null { get; } = new(AgentStructuredValueKind.Null);

    public static AgentStructuredValue FromString(string value)
        => new(AgentStructuredValueKind.String, stringValue: value);

    public static AgentStructuredValue FromNumber(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(AgentStructuredValueKind.Number, numberValue: value);
    }

    public static AgentStructuredValue FromBoolean(bool value)
        => new(AgentStructuredValueKind.Boolean, booleanValue: value);

    public static AgentStructuredValue FromObject(IReadOnlyDictionary<string, AgentStructuredValue>? properties)
        => new(AgentStructuredValueKind.Object, properties: properties);

    public static AgentStructuredValue FromArray(IReadOnlyList<AgentStructuredValue>? items)
        => new(AgentStructuredValueKind.Array, items: items);

    public static AgentStructuredValue FromJsonElement(JsonElement element)
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

    public static AgentStructuredValue FromPlainObject(object? value)
    {
        if (value is null)
        {
            return Null;
        }

        if (value is AgentStructuredValue structuredValue)
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

        if (value is IReadOnlyDictionary<string, AgentStructuredValue> structuredDictionary)
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
            var items = new List<AgentStructuredValue>();
            foreach (var item in enumerable)
            {
                items.Add(FromPlainObject(item));
            }

            return FromArray(items);
        }

        return FromString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }

    public object? ToPlainObject()
        => Kind switch
        {
            AgentStructuredValueKind.Object => Properties.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToPlainObject(),
                StringComparer.Ordinal),
            AgentStructuredValueKind.Array => Items.Select(static item => item.ToPlainObject()).ToArray(),
            AgentStructuredValueKind.String => StringValue,
            AgentStructuredValueKind.Number => ParseNumber(NumberValue),
            AgentStructuredValueKind.Boolean => BooleanValue,
            AgentStructuredValueKind.Null => null,
            _ => null,
        };

    public AgentStructuredValue GetProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (Kind != AgentStructuredValueKind.Object)
        {
            throw new InvalidOperationException($"Structured value is {Kind}, not Object.");
        }

        if (!Properties.TryGetValue(propertyName, out var propertyValue))
        {
            throw new KeyNotFoundException($"Structured value does not contain property '{propertyName}'.");
        }

        return propertyValue;
    }

    public bool TryGetProperty(string propertyName, out AgentStructuredValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (Kind == AgentStructuredValueKind.Object && Properties.TryGetValue(propertyName, out var propertyValue))
        {
            value = propertyValue;
            return true;
        }

        value = null;
        return false;
    }

    public string? GetString()
        => Kind switch
        {
            AgentStructuredValueKind.String => StringValue,
            AgentStructuredValueKind.Number => NumberValue,
            AgentStructuredValueKind.Boolean => BooleanValue?.ToString(),
            AgentStructuredValueKind.Null => null,
            _ => throw new InvalidOperationException($"Structured value is {Kind}, not scalar text."),
        };

    public bool GetBoolean()
        => Kind == AgentStructuredValueKind.Boolean && BooleanValue.HasValue
            ? BooleanValue.Value
            : throw new InvalidOperationException($"Structured value is {Kind}, not Boolean.");

    public int GetInt32()
    {
        if (Kind != AgentStructuredValueKind.Number
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
/// Runtime 内部结构化值的离散类型。
/// Discrete value kinds supported by the runtime-internal structured value model.
/// </summary>
public enum AgentStructuredValueKind
{
    Null,
    Object,
    Array,
    String,
    Number,
    Boolean,
}

internal sealed class AgentStructuredValueJsonConverter : JsonConverter<AgentStructuredValue>
{
    public override AgentStructuredValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return AgentStructuredValue.FromJsonElement(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, AgentStructuredValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case AgentStructuredValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.Properties)
                {
                    writer.WritePropertyName(property.Key);
                    Write(writer, property.Value, options);
                }

                writer.WriteEndObject();
                break;
            case AgentStructuredValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.Items)
                {
                    Write(writer, item, options);
                }

                writer.WriteEndArray();
                break;
            case AgentStructuredValueKind.String:
                writer.WriteStringValue(value.StringValue);
                break;
            case AgentStructuredValueKind.Number:
                writer.WriteRawValue(value.NumberValue ?? "null");
                break;
            case AgentStructuredValueKind.Boolean:
                writer.WriteBooleanValue(value.BooleanValue ?? false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
