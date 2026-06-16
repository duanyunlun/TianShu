using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 配置对象与 TOML 转换共用工具。
/// Shared helpers for TOML parsing and config-object conversion.
/// </summary>
public static class TianShuConfigObjectUtilities
{
    public static string ComputeConfigObjectVersion(Dictionary<string, object?> config)
    {
        var serialized = JsonSerializer.Serialize(config);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(serialized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static Dictionary<string, object?> ReadTomlConfigObject(string path, bool suppressErrors = true)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        try
        {
            if (Toml.ToModel(File.ReadAllText(path)) is not TomlTable table)
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            return ConvertTomlTableToDictionary(table);
        }
        catch (Exception ex)
        {
            if (!suppressErrors)
            {
                throw new FormatException($"failed to parse config file `{path}`: {ex.Message}", ex);
            }

            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }
    }

    public static Dictionary<string, object?> ConvertTomlTableToDictionary(TomlTable table)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in table)
        {
            result[pair.Key] = ConvertTomlValue(pair.Value);
        }

        return result;
    }

    public static object? ConvertTomlValue(object? value)
    {
        return value switch
        {
            TomlTable table => ConvertTomlTableToDictionary(table),
            TomlArray array => array.Cast<object?>().Select(ConvertTomlValue).ToList(),
            DateTime dateTime => dateTime.ToString("O"),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
            _ => value,
        };
    }

    public static Dictionary<string, object?> CloneConfigDictionary(Dictionary<string, object?> source)
    {
        var clone = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            clone[pair.Key] = CloneConfigValue(pair.Value);
        }

        return clone;
    }

    public static object? CloneConfigValue(object? value)
    {
        return value switch
        {
            Dictionary<string, object?> dictionary => CloneConfigDictionary(dictionary),
            List<object?> list => list.Select(CloneConfigValue).ToList(),
            _ => value,
        };
    }
}
