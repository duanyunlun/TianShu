using System.Text.Json;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// CLI / session 配置覆盖值的持久化转换与路径重定基辅助件。
/// Helpers for persisted CLI/session config override conversion and path rebasing.
/// </summary>
internal static class KernelConfigOverrideUtilities
{
    public static string ConvertRawOverrideToJson(string rawValue)
    {
        var normalized = Normalize(rawValue) ?? string.Empty;
        if (normalized.StartsWith("json:", StringComparison.OrdinalIgnoreCase))
        {
            var payload = normalized["json:".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(payload))
            {
                try
                {
                    _ = JsonDocument.Parse(payload);
                    return payload;
                }
                catch
                {
                    // Invalid json: payloads are persisted as strings below.
                }
            }
        }

        if (bool.TryParse(normalized, out var boolValue))
        {
            return JsonSerializer.Serialize(boolValue);
        }

        if (int.TryParse(normalized, out var intValue))
        {
            return JsonSerializer.Serialize(intValue);
        }

        if (double.TryParse(normalized, out var doubleValue))
        {
            return JsonSerializer.Serialize(doubleValue);
        }

        return JsonSerializer.Serialize(rawValue);
    }

    public static string RebaseCliConfigOverrideRawValue(string canonicalKey, string rawValue, string baseDirectory)
    {
        var normalized = Normalize(rawValue) ?? string.Empty;
        if (normalized.StartsWith("json:", StringComparison.OrdinalIgnoreCase))
        {
            var payload = normalized["json:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(payload))
            {
                return rawValue;
            }

            try
            {
                using var document = JsonDocument.Parse(payload);
                var rebased = RebaseCliConfigOverrideJsonElement(
                    document.RootElement,
                    KernelConfigPersistenceUtilities.SplitConfigKeyPath(canonicalKey),
                    baseDirectory);
                return $"json:{JsonSerializer.Serialize(rebased)}";
            }
            catch
            {
                return rawValue;
            }
        }

        return ShouldRebaseCliConfigOverridePath(KernelConfigPersistenceUtilities.SplitConfigKeyPath(canonicalKey))
            ? RebaseCliRelativePath(rawValue, baseDirectory)
            : rawValue;
    }

    public static object? RebaseCliConfigOverrideJsonElement(
        JsonElement element,
        IReadOnlyList<string> segments,
        string baseDirectory)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    static property => property.Name,
                    property => RebaseCliConfigOverrideJsonElement(
                        property.Value,
                        [.. segments, property.Name],
                        baseDirectory),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => RebaseCliConfigOverrideJsonElement(item, segments, baseDirectory))
                .ToList(),
            JsonValueKind.String when ShouldRebaseCliConfigOverridePath(segments) => RebaseCliRelativePath(element.GetString() ?? string.Empty, baseDirectory),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Null => null,
            _ => element.ToString(),
        };
    }

    public static bool ShouldRebaseCliConfigOverridePath(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            return false;
        }

        return segments switch
        {
            ["log_dir"] => true,
            ["model_instructions_file"] => true,
            ["experimental_instructions_file"] => true,
            ["experimental_compact_prompt_file"] => true,
            ["js_repl_node_path"] => true,
            ["js_repl_node_module_dirs"] => true,
            ["sqlite_home"] => true,
            ["collaboration_mode", "plan_prompt_file"] => true,
            ["profiles", _, "instructions_file"] => true,
            ["profiles", _, "model_instructions_file"] => true,
            ["profiles", _, "experimental_instructions_file"] => true,
            ["profiles", _, "experimental_compact_prompt_file"] => true,
            ["profiles", _, "js_repl_node_path"] => true,
            ["profiles", _, "js_repl_node_module_dirs"] => true,
            ["profiles", _, "zsh_path"] => true,
            ["profiles", _, "model_route_set_json"] => true,
            ["mcp_servers", _, "cwd"] => true,
            ["skills", "config", "path"] => true,
            _ => false,
        };
    }

    public static string RebaseCliRelativePath(string rawValue, string baseDirectory)
    {
        var normalized = Normalize(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return rawValue;
        }

        if (Path.IsPathRooted(normalized))
        {
            return Path.GetFullPath(normalized);
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, normalized));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
