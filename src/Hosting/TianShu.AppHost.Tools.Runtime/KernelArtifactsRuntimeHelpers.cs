using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelArtifactsRuntimeHelpers
{
    public static KernelArtifactsRuntimeOptions ResolveArtifactsRuntimeOptions(
        string tianShuHome,
        string? runtimeVersion,
        string? cacheRoot,
        string? preferredNodePath)
    {
        var effectiveRuntimeVersion = string.IsNullOrWhiteSpace(runtimeVersion)
            ? KernelArtifactsRuntimeManager.PinnedRuntimeVersion
            : runtimeVersion!;
        return new KernelArtifactsRuntimeOptions(
            TianShuHome: tianShuHome,
            RuntimeVersion: effectiveRuntimeVersion,
            CacheRoot: cacheRoot,
            PreferredNodePath: preferredNodePath);
    }

    public static bool ResolveConfiguredArtifactEnabled(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return false;
        }

        var inFeaturesSection = false;
        foreach (var rawLine in configText.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inFeaturesSection = string.Equals(line[1..^1].Trim(), "features", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inFeaturesSection)
            {
                continue;
            }

            var equalIndex = line.IndexOf('=');
            if (equalIndex <= 0)
            {
                continue;
            }

            var key = line[..equalIndex].Trim();
            if (!string.Equals(key, "artifact", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return bool.TryParse(ReadScalarConfigValue(line[(equalIndex + 1)..]), out var enabled) && enabled;
        }

        return false;
    }

    private static string? ReadScalarConfigValue(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(rawValue);
            var root = json.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.String => KernelToolJsonHelpers.Normalize(root.GetString()),
                JsonValueKind.Number => KernelToolJsonHelpers.Normalize(root.GetRawText()),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return KernelToolJsonHelpers.Normalize(rawValue.Trim().Trim('"', '\''));
        }
    }
}
