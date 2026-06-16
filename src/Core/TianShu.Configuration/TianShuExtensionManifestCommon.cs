using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 扩展包 manifest 的公共字段。
/// Common fields shared by extension package manifests.
/// </summary>
public interface ITianShuExtensionManifestMetadata
{
    /// <summary>
    /// 扩展包版本。
    /// Extension package version.
    /// </summary>
    string? Version { get; set; }

    /// <summary>
    /// 最低 TianShu 版本要求。
    /// Minimum TianShu version required by the package.
    /// </summary>
    string? MinTianShuVersion { get; set; }

    /// <summary>
    /// 扩展包声明的能力标签。
    /// Capability tags declared by the package.
    /// </summary>
    IReadOnlyList<string> Capabilities { get; set; }

    /// <summary>
    /// 扩展包声明的诊断标签。
    /// Diagnostic tags declared by the package.
    /// </summary>
    IReadOnlyList<string> Diagnostics { get; set; }

    /// <summary>
    /// 扩展包当前加载状态；该字段只用于投影和诊断，不写回 manifest。
    /// Current package load status; projection-only and not written back to the manifest.
    /// </summary>
    string LoadStatus { get; set; }

    /// <summary>
    /// 扩展包不可用或降级的非敏感原因；该字段只用于投影和诊断，不写回 manifest。
    /// Non-sensitive reason for unavailable or degraded package state; projection-only and not written back.
    /// </summary>
    string? UnavailableReason { get; set; }
}

internal static class TianShuExtensionManifestCommon
{
    public const string CurrentTianShuVersion = "0.1.0";
    public const string LoadStatusAvailable = "available";
    public const string LoadStatusUnavailable = "unavailable";
    public const string VersionIncompatibleIssueCode = "version_incompatible";

    private static readonly string[] ExtensionTypes = ["assembly", "package", "plugin", "builtin"];

    public static void ReadMetadata(TomlTable table, ITianShuExtensionManifestMetadata target)
    {
        target.Version = ReadString(table, "version");
        target.MinTianShuVersion = ReadString(table, "min_tianshu_version");
        target.Capabilities = ReadStringArray(table, "capabilities");
        target.Diagnostics = ReadStringArray(table, "diagnostics");
    }

    public static void WriteMetadata(TomlTable table, ITianShuExtensionManifestMetadata source)
    {
        SetOptional(table, "version", source.Version);
        SetOptional(table, "min_tianshu_version", source.MinTianShuVersion);
        SetStringArray(table, "capabilities", source.Capabilities);
        SetStringArray(table, "diagnostics", source.Diagnostics);
    }

    public static bool IsKnownExtensionType(string? type)
        => !string.IsNullOrWhiteSpace(type)
           && ExtensionTypes.Contains(type.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsCompatible(ITianShuExtensionManifestMetadata metadata, out string? reason)
    {
        var result = EvaluateCompatibility(metadata);
        reason = result.UnavailableReason;
        return result.IsCompatible;
    }

    public static TianShuExtensionManifestCompatibilityResult EvaluateCompatibility(ITianShuExtensionManifestMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.MinTianShuVersion))
        {
            return TianShuExtensionManifestCompatibilityResult.Available;
        }

        if (!Version.TryParse(metadata.MinTianShuVersion, out var minimum))
        {
            return TianShuExtensionManifestCompatibilityResult.Unavailable(
                $"min_tianshu_version 不是有效版本：{metadata.MinTianShuVersion}");
        }

        if (!Version.TryParse(CurrentTianShuVersion, out var current))
        {
            return TianShuExtensionManifestCompatibilityResult.Unavailable(
                $"当前 TianShu 版本不是有效版本：{CurrentTianShuVersion}");
        }

        if (current < minimum)
        {
            return TianShuExtensionManifestCompatibilityResult.Unavailable(
                $"需要 TianShu >= {minimum}，当前为 {current}");
        }

        return TianShuExtensionManifestCompatibilityResult.Available;
    }

    public static bool ApplyCompatibility(ITianShuExtensionManifestMetadata metadata)
    {
        var result = EvaluateCompatibility(metadata);
        metadata.LoadStatus = result.LoadStatus;
        metadata.UnavailableReason = result.UnavailableReason;
        return result.IsCompatible;
    }

    public static IReadOnlyList<string> ReadStringArray(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlArray array)
        {
            return [];
        }

        return array
            .OfType<string>()
            .Select(static item => item.Trim())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void SetStringArray(TomlTable table, string key, IReadOnlyList<string>? values)
    {
        var normalized = values?
            .Select(static value => value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (normalized.Length == 0)
        {
            table.Remove(key);
            return;
        }

        var array = new TomlArray();
        foreach (var value in normalized)
        {
            array.Add(value);
        }

        table[key] = array;
    }

    public static void SetOptional(TomlTable table, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            table.Remove(key);
            return;
        }

        table[key] = value.Trim();
    }

    private static string? ReadString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as string : null;
}

internal sealed record TianShuExtensionManifestCompatibilityResult(
    bool IsCompatible,
    string LoadStatus,
    string? UnavailableReason)
{
    public static TianShuExtensionManifestCompatibilityResult Available { get; } = new(
        true,
        TianShuExtensionManifestCommon.LoadStatusAvailable,
        null);

    public static TianShuExtensionManifestCompatibilityResult Unavailable(string reason)
        => new(false, TianShuExtensionManifestCommon.LoadStatusUnavailable, reason);
}
