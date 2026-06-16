using System.Text.Json;

namespace TianShu.Cli.Interaction.Events;

internal static class ToolInvocationCommandInputParser
{
    public static ToolInvocationInput Build(string raw, JsonElement root)
    {
        var command = ReadJsonCommandSummary(root);
        return new ToolInvocationInput(raw, command ?? ToolInvocationJsonHelpers.BuildCompactJsonSummary(root), command, null);
    }

    private static string? ReadJsonCommandSummary(JsonElement root, int depth = 0)
    {
        if (depth > 4)
        {
            return null;
        }

        var direct = ReadJsonCommandText(root, "command")
                     ?? ReadJsonStringArray(root, "command")
                     ?? ReadJsonCommandText(root, "cmd")
                     ?? ReadJsonStringArray(root, "cmd")
                     ?? ReadJsonCommandText(root, "script")
                     ?? ReadJsonStringArray(root, "script")
                     ?? ReadJsonStringArray(root, "argv")
                     ?? ReadJsonStringArray(root, "args")
                     ?? ReadJsonCommandText(root, "input");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        return ReadNestedJsonCommandSummary(root, depth, "arguments")
               ?? ReadNestedJsonCommandSummary(root, depth, "input")
               ?? ReadNestedJsonCommandSummary(root, depth, "parameters")
               ?? ReadNestedJsonCommandSummary(root, depth, "payload");
    }

    private static string? ReadNestedJsonCommandSummary(JsonElement root, int depth, string propertyName)
    {
        if (!ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return ReadJsonCommandSummary(value, depth + 1);
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = ToolInvocationJsonHelpers.NormalizeDisplayText(value.GetString());
        if (string.IsNullOrWhiteSpace(text) || !ToolInvocationJsonHelpers.TryParseJsonObject(text, out var document) || document is null)
        {
            return null;
        }

        using (document)
        {
            return ReadJsonCommandSummary(document.RootElement, depth + 1);
        }
    }

    private static string? ReadJsonCommandText(JsonElement root, string propertyName)
    {
        if (!ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var command = ToolInvocationJsonHelpers.NormalizeDisplayText(value.GetString());
        return string.IsNullOrWhiteSpace(command)
               || command.StartsWith('{')
               || command.StartsWith('[')
            ? null
            : command;
    }

    private static string? ReadJsonStringArray(JsonElement root, string propertyName)
    {
        if (!ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = value.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => ToolInvocationJsonHelpers.NormalizeDisplayText(item.GetString()))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return parts.Length == 0 ? null : string.Join(" ", parts);
    }
}
