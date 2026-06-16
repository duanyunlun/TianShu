using System.Text.Json;
using static TianShu.AppHost.Configuration.KernelTomlTextParsingUtilities;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelJsReplRuntimeHelpers
{
    public static KernelJsReplOptions ResolveJsReplOptions(
        string? cwd,
        string? configText,
        string? environmentNodePath,
        string? environmentModuleDirectories)
    {
        var mergedConfigText = configText ?? string.Empty;

        var nodePath = KernelToolJsonHelpers.Normalize(environmentNodePath);
        if (string.IsNullOrWhiteSpace(nodePath)
            && TryParseTopLevelTomlScalar(mergedConfigText, "js_repl_node_path", out var configNodePath))
        {
            nodePath = KernelToolJsonHelpers.Normalize(configNodePath);
        }

        var moduleDirectories = Array.Empty<string>();
        var normalizedEnvironmentModuleDirectories = KernelToolJsonHelpers.Normalize(environmentModuleDirectories);
        if (!string.IsNullOrWhiteSpace(normalizedEnvironmentModuleDirectories))
        {
            moduleDirectories = normalizedEnvironmentModuleDirectories!
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }
        else if (TryParseTomlStringArray(mergedConfigText, "js_repl_node_module_dirs", out var configModuleDirectories))
        {
            moduleDirectories = configModuleDirectories.ToArray();
        }

        return new KernelJsReplOptions(
            NodePath: nodePath ?? "node",
            WorkingDirectory: KernelToolJsonHelpers.Normalize(cwd) ?? Environment.CurrentDirectory,
            NodeModuleDirectories: moduleDirectories);
    }

    public static string BuildJsReplNestedToolCallItemId(string turnId, string requestId, string toolName)
    {
        static char MapChar(char value) => char.IsLetterOrDigit(value) || value is '_' or '-' ? value : '_';

        var safeToolName = string.IsNullOrWhiteSpace(toolName)
            ? "tool"
            : new string(toolName.Select(MapChar).ToArray());
        var safeRequestId = string.IsNullOrWhiteSpace(requestId)
            ? Guid.NewGuid().ToString("N")
            : new string(requestId.Select(MapChar).ToArray());
        return $"jsrepl_{safeToolName}_{safeRequestId}_{turnId}";
    }
}
