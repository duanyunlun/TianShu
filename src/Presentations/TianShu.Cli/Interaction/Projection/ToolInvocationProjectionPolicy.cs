using TianShu.Cli.Interaction.Events;

namespace TianShu.Cli.Interaction.Projection;

internal static class ToolInvocationProjectionPolicy
{
    public static bool IsInternalToolEvent(ToolInvocationEvent toolEvent)
    {
        if (string.Equals(toolEvent.Status, "hook", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(toolEvent.Phase, "hook", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolEvent.Phase, "request_approval", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolEvent.Phase, "request_permission", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolEvent.Phase, "request_user_input", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (string.Equals(toolEvent.Phase, "before", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolEvent.Phase, "after", StringComparison.OrdinalIgnoreCase))
               && NormalizeDisplayText(toolEvent.InputText) is null
               && NormalizeDisplayText(toolEvent.OutputText) is null;
    }

    public static bool ShouldSuppressCompletedDisplay(ToolInvocationSnapshot snapshot)
        => string.Equals(snapshot.ToolName, "update_plan", StringComparison.OrdinalIgnoreCase)
           && !OutputLooksLikeFailure(snapshot.OutputText, snapshot.Status);

    public static string ResolveCompletionKey(ToolInvocationEvent toolEvent, ToolInvocationSnapshot snapshot)
        => NormalizeDisplayText(toolEvent.CallId)
           ?? NormalizeDisplayText(toolEvent.ItemId)
           ?? NormalizeDisplayText(snapshot.CallId)
           ?? NormalizeDisplayText(snapshot.ItemId)
           ?? $"{snapshot.ToolName}:{NormalizeDisplayText(snapshot.InputText) ?? "<input>"}:{NormalizeDisplayText(snapshot.OutputText) ?? "<output>"}";

    private static bool OutputLooksLikeFailure(string? outputText, string? status)
    {
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var output = NormalizeDisplayText(outputText) ?? string.Empty;
        return output.Contains("工具参数无效", StringComparison.OrdinalIgnoreCase)
               || output.Contains("error", StringComparison.OrdinalIgnoreCase)
               || output.Contains("failed", StringComparison.OrdinalIgnoreCase)
               || output.Contains("缺少", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeDisplayText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(" ", value.Trim().Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
