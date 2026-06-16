using System.Text;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// 工具配置模板导出器。Exports a TianShu tool profile TOML template from the resolved runtime catalog.
/// </summary>
public static class TianShuToolProfileTomlExporter
{
    /// <summary>
    /// 从当前解析后的工具目录生成可审阅、可合并的工具配置模板。
    /// Builds a reviewable tool configuration template from the resolved tool catalog.
    /// </summary>
    public static string ExportBuiltinProfileToml(
        ResolvedToolCatalogSnapshot catalog,
        string profileId = "builtin")
    {
        ArgumentNullException.ThrowIfNull(catalog);

        profileId = string.IsNullOrWhiteSpace(profileId) ? "builtin" : profileId.Trim();
        var items = catalog.Items
            .OrderBy(static item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var enabledTools = items
            .Where(static item => item.Available)
            .Select(static item => item.Name)
            .ToArray();
        var disabledTools = items
            .Where(static item => !item.Available)
            .Select(static item => item.Name)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("# TianShu 当前内置工具配置模板。");
        builder.AppendLine("# 该文件由运行时工具 catalog 生成，用于审阅或手动合并；不会自动覆盖 tianshu.toml。");
        builder.AppendLine("schema_version = 1");
        builder.AppendLine();
        builder.AppendLine($"[tool_profiles.{QuoteBareKey(profileId)}]");
        WriteStringArray(builder, "enabled", enabledTools);
        WriteStringArray(builder, "disabled", disabledTools);
        builder.AppendLine();

        foreach (var item in items)
        {
            builder.AppendLine($"[tools.{QuoteBareKey(item.Name)}]");
            builder.AppendLine($"enabled = {FormatBoolean(item.Available)}");
            if (!string.IsNullOrWhiteSpace(item.ImplementationId))
            {
                builder.AppendLine($"implementation_id = {QuoteString(item.ImplementationId)}");
            }

            builder.AppendLine($"implementation_kind = {QuoteString(FormatImplementationKind(item.ImplementationKind))}");
            builder.AppendLine("priority = 0");
            if (item.FallbackPolicy is { } fallback && !string.IsNullOrWhiteSpace(fallback.Strategy))
            {
                builder.AppendLine($"fallback = {QuoteString(fallback.Strategy)}");
            }

            if (!item.ModelVisible)
            {
                builder.AppendLine("# model_visible = false");
            }

            if (!string.IsNullOrWhiteSpace(item.Reason))
            {
                builder.AppendLine($"# reason = {QuoteString(item.Reason)}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void WriteStringArray(StringBuilder builder, string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            builder.AppendLine($"{key} = []");
            return;
        }

        builder.AppendLine($"{key} = [");
        foreach (var value in values)
        {
            builder.AppendLine($"  {QuoteString(value)},");
        }

        builder.AppendLine("]");
    }

    private static string FormatImplementationKind(ToolImplementationKind kind)
        => kind.ToString().ToLowerInvariant();

    private static string FormatBoolean(bool value)
        => value ? "true" : "false";

    private static string QuoteBareKey(string value)
        => IsBareKey(value) ? value : QuoteString(value);

    private static bool IsBareKey(string value)
        => value.All(static ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    private static string QuoteString(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
