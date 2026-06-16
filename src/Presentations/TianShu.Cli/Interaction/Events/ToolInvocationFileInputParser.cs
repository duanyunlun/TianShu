using System.Text.Json;

namespace TianShu.Cli.Interaction.Events;

internal static class ToolInvocationFileInputParser
{
    public static ToolInvocationInput Build(string raw, JsonElement root)
    {
        var path = ToolInvocationJsonHelpers.ReadJsonString(root, "path")
                   ?? ToolInvocationJsonHelpers.ReadJsonString(root, "file_path")
                   ?? ToolInvocationJsonHelpers.ReadJsonString(root, "relative_path")
                   ?? ReadFirstChangePath(root);
        return new ToolInvocationInput(raw, path ?? ToolInvocationJsonHelpers.BuildCompactJsonSummary(root), null, path);
    }

    private static string? ReadFirstChangePath(JsonElement root)
    {
        if (!ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, "changes", out var changes)
            || changes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var change in changes.EnumerateArray())
        {
            if (change.ValueKind == JsonValueKind.Object
                && ToolInvocationJsonHelpers.ReadJsonString(change, "path") is { } path)
            {
                return path;
            }
        }

        return null;
    }
}
