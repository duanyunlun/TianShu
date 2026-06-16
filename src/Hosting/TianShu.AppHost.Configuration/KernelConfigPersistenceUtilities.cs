using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// persisted config TOML / JSON 互转与 key-path 处理辅助件。
/// Helpers for persisted config TOML/JSON conversion and key-path mutation.
/// </summary>
internal static class KernelConfigPersistenceUtilities
{
    public static void MergePersistedConfigTable(TomlTable target, TomlTable source)
    {
        foreach (var pair in source)
        {
            if (pair.Value is TomlTable sourceTable
                && target.TryGetValue(pair.Key, out var existing)
                && existing is TomlTable targetTable)
            {
                MergePersistedConfigTable(targetTable, sourceTable);
                continue;
            }

            target[pair.Key] = pair.Value;
        }
    }

    public static TomlTable ReadPersistedConfigTable(string path)
    {
        if (!File.Exists(path))
        {
            return new TomlTable();
        }

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TomlTable();
        }

        var syntax = Toml.Parse(text);
        if (syntax.HasErrors)
        {
            var first = syntax.Diagnostics.FirstOrDefault()?.ToString() ?? "tianshu.toml 解析失败";
            throw new InvalidDataException(first);
        }

        if (syntax.ToModel() is not TomlTable table)
        {
            throw new InvalidDataException("tianshu.toml 根节点必须是 TOML table。");
        }

        return table;
    }

    public static void FlattenPersistedConfigValue(Dictionary<string, string> values, string keyPath, object? value)
    {
        if (value is TomlTable table)
        {
            foreach (var pair in table)
            {
                FlattenPersistedConfigValue(values, $"{keyPath}.{pair.Key}", pair.Value);
            }

            return;
        }

        values[keyPath] = ConvertTomlScalarToJson(value);
    }

    public static string ConvertTomlScalarToJson(object? value)
    {
        return value switch
        {
            null => "null",
            TomlArray array => JsonSerializer.Serialize(ConvertTomlArrayToList(array)),
            TomlTable table => JsonSerializer.Serialize(KernelConfigObjectUtilities.ConvertTomlTableToDictionary(table)),
            string text => JsonSerializer.Serialize(text),
            bool boolean => JsonSerializer.Serialize(boolean),
            sbyte number => JsonSerializer.Serialize(number),
            byte number => JsonSerializer.Serialize(number),
            short number => JsonSerializer.Serialize(number),
            ushort number => JsonSerializer.Serialize(number),
            int number => JsonSerializer.Serialize(number),
            uint number => JsonSerializer.Serialize(number),
            long number => JsonSerializer.Serialize(number),
            ulong number => JsonSerializer.Serialize(number),
            float number => JsonSerializer.Serialize(number),
            double number => JsonSerializer.Serialize(number),
            decimal number => JsonSerializer.Serialize(number),
            DateTimeOffset dto => JsonSerializer.Serialize(dto),
            DateTime dateTime => JsonSerializer.Serialize(dateTime),
            TimeSpan timeSpan => JsonSerializer.Serialize(timeSpan.ToString()),
            _ => JsonSerializer.Serialize(value.ToString())
        };
    }

    public static List<object?> ConvertTomlArrayToList(TomlArray array)
    {
        var list = new List<object?>(array.Count);
        foreach (var item in array)
        {
            list.Add(ConvertTomlValueToClr(item));
        }

        return list;
    }

    public static object? ConvertTomlValueToClr(object? value)
    {
        return value switch
        {
            null => null,
            TomlTable table => KernelConfigObjectUtilities.ConvertTomlTableToDictionary(table),
            TomlArray array => ConvertTomlArrayToList(array),
            TimeSpan timeSpan => timeSpan.ToString(),
            _ => value,
        };
    }

    public static JsonElement ParsePersistedJsonValue(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return document.RootElement.Clone();
    }

    public static object ConvertJsonElementToTomlValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number when value.TryGetInt64(out var int64) => int64,
            JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => ConvertJsonArrayToTomlArray(value),
            JsonValueKind.Object => ConvertJsonObjectToTomlTable(value),
            _ => string.Empty,
        };
    }

    public static TomlArray ConvertJsonArrayToTomlArray(JsonElement value)
    {
        var array = new TomlArray();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            array.Add(ConvertJsonElementToTomlValue(item));
        }

        return array;
    }

    public static TomlTable ConvertJsonObjectToTomlTable(JsonElement value)
    {
        var table = new TomlTable();
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            table[property.Name] = ConvertJsonElementToTomlValue(property.Value);
        }

        return table;
    }

    public static void SetTomlPathValue(TomlTable root, IReadOnlyList<string> segments, object value)
    {
        TomlTable current = root;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            current = GetOrCreateTomlTable(current, segments[index]);
        }

        current[segments[^1]] = value;
    }

    public static void RemoveTomlPathValue(TomlTable root, IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            return;
        }

        var stack = new Stack<(TomlTable Table, string Key)>();
        TomlTable current = root;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (!current.TryGetValue(segments[index], out var next)
                || next is not TomlTable nextTable)
            {
                return;
            }

            stack.Push((current, segments[index]));
            current = nextTable;
        }

        current.Remove(segments[^1]);
        while (stack.Count > 0 && current.Count == 0)
        {
            var (parent, key) = stack.Pop();
            parent.Remove(key);
            current = parent;
        }
    }

    public static TomlTable GetOrCreateTomlTable(TomlTable parent, string key)
    {
        if (parent.TryGetValue(key, out var existing) && existing is TomlTable table)
        {
            return table;
        }

        var created = new TomlTable();
        parent[key] = created;
        return created;
    }

    public static List<string> SplitConfigKeyPath(string keyPath)
    {
        return keyPath
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment.Trim().Trim('"', '\''))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToList();
    }

    public static string CanonicalizePersistedConfigKeyPath(string key)
    {
        var segments = SplitConfigKeyPath(key);
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(".", segments);
    }

    public static string ResolvePersistedConfigTomlPath(string? filePath = null, string? cwd = null)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return Path.GetFullPath(filePath);
        }

        return TianShuConfigTomlPathResolver.ResolveWritableProjectConfigTomlPath(cwd);
    }
}
