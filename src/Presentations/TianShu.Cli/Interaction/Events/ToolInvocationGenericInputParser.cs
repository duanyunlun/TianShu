using System.Text.Json;

namespace TianShu.Cli.Interaction.Events;

internal static class ToolInvocationGenericInputParser
{
    public static ToolInvocationInput Build(string raw, JsonElement root)
        => new(
            raw,
            ToolInvocationJsonHelpers.ReadJsonString(root, "query")
            ?? ToolInvocationJsonHelpers.ReadJsonString(root, "path")
            ?? ToolInvocationJsonHelpers.ReadJsonString(root, "command")
            ?? ToolInvocationJsonHelpers.ReadJsonString(root, "prompt")
            ?? ToolInvocationJsonHelpers.BuildCompactJsonSummary(root),
            null,
            ToolInvocationJsonHelpers.ReadJsonString(root, "path"));
}
