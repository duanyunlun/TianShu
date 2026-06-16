using System.Text.Json;
using Tomlyn.Model;
using static TianShu.AppHost.Configuration.KernelConfigPersistenceUtilities;

namespace TianShu.AppHost.Tools;

/// <summary>
/// persisted skill-config 兼容读写辅助件。
/// Helpers for persisted skill-config compatibility flattening and TOML mutation.
/// </summary>
internal static class KernelPersistedSkillConfigUtilities
{
    public static Dictionary<string, string> ReadPersistedConfigValues(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var table = ReadPersistedConfigTable(path);
        return FlattenPersistedConfigTable(table, path);
    }

    public static void MergePersistedConfigValues(Dictionary<string, string> target, string path)
    {
        foreach (var pair in ReadPersistedConfigValues(path))
        {
            target[pair.Key] = pair.Value;
        }
    }

    public static Dictionary<string, string> FlattenPersistedConfigTable(TomlTable root, string? sourcePath = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in root)
        {
            if (string.Equals(pair.Key, "skills", StringComparison.OrdinalIgnoreCase)
                && pair.Value is TomlTable skillsTable)
            {
                FlattenSkillsConfig(values, skillsTable, sourcePath);
            }

            FlattenPersistedConfigValue(values, pair.Key, pair.Value);
        }

        return values;
    }

    public static void ApplyPersistedConfigValues(TomlTable root, IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            ApplyPersistedConfigValue(root, pair.Key, pair.Value);
        }
    }

    public static void ApplyPersistedConfigValue(TomlTable root, string key, string rawJson)
    {
        if (TryApplySpecialPersistedConfigValue(root, key, rawJson))
        {
            return;
        }

        var segments = SplitConfigKeyPath(CanonicalizePersistedConfigKeyPath(key));
        if (segments.Count == 0)
        {
            return;
        }

        var jsonValue = ParsePersistedJsonValue(rawJson);
        if (jsonValue.ValueKind == JsonValueKind.Null)
        {
            RemoveTomlPathValue(root, segments);
            return;
        }

        SetTomlPathValue(root, segments, ConvertJsonElementToTomlValue(jsonValue));
    }

    public static string ToSkillEnabledConfigKey(string skillPath)
        => $"skills::{KernelPathUtilities.NormalizeSkillDocumentPath(skillPath)}";

    private static void FlattenSkillsConfig(Dictionary<string, string> values, TomlTable skillsTable, string? sourcePath)
    {
        if (!skillsTable.TryGetValue("config", out var configValue))
        {
            return;
        }

        if (configValue is TomlTableArray tableArray)
        {
            foreach (var table in tableArray)
            {
                FlattenSkillConfigEntry(values, table, sourcePath);
            }

            return;
        }

        if (configValue is TomlArray array)
        {
            foreach (var item in array.OfType<TomlTable>())
            {
                FlattenSkillConfigEntry(values, item, sourcePath);
            }
        }
    }

    private static void FlattenSkillConfigEntry(Dictionary<string, string> values, TomlTable entry, string? sourcePath)
    {
        var path = entry.TryGetValue("path", out var pathValue)
            ? ResolvePersistedSkillConfigPath(pathValue as string, sourcePath)
            : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var enabled = entry.TryGetValue("enabled", out var enabledValue) && enabledValue is bool boolean
            ? boolean
            : true;
        values[ToSkillEnabledConfigKey(path!)] = JsonSerializer.Serialize(enabled);
    }

    private static string? ResolvePersistedSkillConfigPath(string? rawPath, string? sourcePath)
    {
        var normalized = KernelToolJsonHelpers.Normalize(rawPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        const string skillScheme = "skill://";
        if (normalized.StartsWith(skillScheme, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[skillScheme.Length..];
        }

        if (!Path.IsPathRooted(normalized))
        {
            var sourceDirectory = string.IsNullOrWhiteSpace(sourcePath)
                ? null
                : Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                normalized = Path.Combine(sourceDirectory!, normalized);
            }
        }

        return KernelPathUtilities.NormalizeSkillDocumentPath(normalized);
    }

    private static bool TryApplySpecialPersistedConfigValue(TomlTable root, string key, string rawJson)
    {
        if (!key.StartsWith("skills::", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var skillPath = KernelToolJsonHelpers.Normalize(key["skills::".Length..]);
        if (string.IsNullOrWhiteSpace(skillPath))
        {
            return true;
        }

        var jsonValue = ParsePersistedJsonValue(rawJson);
        var skills = GetOrCreateTomlTable(root, "skills");
        var skillConfig = GetOrCreateTomlTableArray(skills, "config");
        var existingEntry = skillConfig
            .Select(static (entry, index) => new { entry, index })
            .FirstOrDefault(item =>
                KernelPathUtilities.AreEquivalentForComparison(
                    item.entry.TryGetValue("path", out var value) ? value as string : null,
                    skillPath));
        var removeEntry = jsonValue.ValueKind == JsonValueKind.Null
                          || jsonValue.ValueKind == JsonValueKind.True
                          || (jsonValue.ValueKind == JsonValueKind.String
                              && bool.TryParse(jsonValue.GetString(), out var parsedEnabled)
                              && parsedEnabled);
        if (removeEntry)
        {
            if (existingEntry is not null)
            {
                skillConfig.RemoveAt(existingEntry.index);
            }

            CleanupSkillsConfig(root, skills, skillConfig);
            return true;
        }

        var enabled = jsonValue.ValueKind switch
        {
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(jsonValue.GetString(), out var parsed) => parsed,
            _ => false,
        };
        if (enabled)
        {
            if (existingEntry is not null)
            {
                skillConfig.RemoveAt(existingEntry.index);
            }

            CleanupSkillsConfig(root, skills, skillConfig);
            return true;
        }

        var entry = existingEntry?.entry ?? new TomlTable();
        entry["path"] = skillPath!;
        entry["enabled"] = false;
        if (existingEntry is null)
        {
            skillConfig.Add(entry);
        }

        return true;
    }

    private static TomlTableArray GetOrCreateTomlTableArray(TomlTable root, string key)
    {
        if (root.TryGetValue(key, out var existing)
            && existing is TomlTableArray tableArray)
        {
            return tableArray;
        }

        var created = new TomlTableArray();
        root[key] = created;
        return created;
    }

    private static void CleanupSkillsConfig(TomlTable root, TomlTable skills, TomlTableArray skillConfig)
    {
        if (skillConfig.Count == 0)
        {
            skills.Remove("config");
        }

        if (skills.Count == 0)
        {
            root.Remove("skills");
        }
    }

}
