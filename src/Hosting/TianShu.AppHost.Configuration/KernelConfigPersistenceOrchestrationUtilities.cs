using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// persisted config 合并读取与文本物化编排辅助件。
/// Helpers for merged persisted-config reads and merged TOML materialization.
/// </summary>
internal static class KernelConfigPersistenceOrchestrationUtilities
{
    public static Dictionary<string, string> ReadMergedPersistedConfigValues(
        string? cwd,
        string userConfigPath,
        Action<Dictionary<string, string>, string> mergePersistedConfigValues)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        mergePersistedConfigValues(values, userConfigPath);
        foreach (var projectConfigPath in TianShuConfigTomlPathResolver.EnumerateProjectConfigPaths(cwd))
        {
            mergePersistedConfigValues(values, projectConfigPath);
        }

        return values;
    }

    public static TomlTable ReadMergedPersistedConfigTable(string? cwd, string userConfigPath)
    {
        var root = new TomlTable();
        KernelConfigPersistenceUtilities.MergePersistedConfigTable(root, KernelConfigPersistenceUtilities.ReadPersistedConfigTable(userConfigPath));
        foreach (var projectConfigPath in TianShuConfigTomlPathResolver.EnumerateProjectConfigPaths(cwd))
        {
            KernelConfigPersistenceUtilities.MergePersistedConfigTable(root, KernelConfigPersistenceUtilities.ReadPersistedConfigTable(projectConfigPath));
        }

        return root;
    }

    public static string? ReadMergedPersistedConfigText(
        string? cwd,
        string userConfigPath,
        IReadOnlyDictionary<string, string> processOverrideValues,
        Action<TomlTable, IReadOnlyDictionary<string, string>> applyPersistedConfigValues)
    {
        var root = ReadMergedPersistedConfigTable(cwd, userConfigPath);
        applyPersistedConfigValues(root, processOverrideValues);
        return root.Count == 0
            ? null
            : Toml.FromModel(root).TrimEnd() + Environment.NewLine;
    }
}
