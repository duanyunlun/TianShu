using TianShu.Contracts.Catalog;
using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction.Rendering;

internal static class SelectionPickerRowRenderer
{
    public static string BuildThreadListRow(ControlPlaneThreadSummary thread, bool includeCwd)
    {
        var title = ResolveThreadTitle(thread.Name, thread.ThreadId.Value);
        if (includeCwd && !string.IsNullOrWhiteSpace(thread.WorkingDirectory))
        {
            return $"{FormatThreadTimestamp(thread.UpdatedAt)}  {title}  {thread.WorkingDirectory}";
        }

        return $"{FormatThreadTimestamp(thread.UpdatedAt)}  {title}";
    }

    public static string BuildStartupThreadPickerRow(ControlPlaneThreadSummary thread, bool includeCwd)
    {
        var title = ResolveThreadTitle(thread.Name, thread.ThreadId.Value);
        if (includeCwd && !string.IsNullOrWhiteSpace(thread.WorkingDirectory))
        {
            return $"{FormatThreadTimestamp(thread.UpdatedAt)}\t{title}\t{thread.WorkingDirectory}";
        }

        return $"{FormatThreadTimestamp(thread.UpdatedAt)}\t{title}";
    }

    public static string BuildModelSelectionRow(ControlPlaneModelCatalogItem model)
    {
        var row = model.Model;
        if (!string.IsNullOrWhiteSpace(model.DisplayName)
            && !string.Equals(model.DisplayName, model.Model, StringComparison.OrdinalIgnoreCase))
        {
            row += $" - {model.DisplayName}";
        }

        if (model.IsDefault)
        {
            row += "  default";
        }

        return row;
    }

    private static string ResolveThreadTitle(string? name, string threadId)
    {
        var resolvedName = Normalize(name);
        if (!string.IsNullOrWhiteSpace(resolvedName)
            && !string.Equals(resolvedName, threadId, StringComparison.Ordinal))
        {
            return resolvedName!;
        }

        return "未命名线程";
    }

    private static string FormatThreadTimestamp(DateTimeOffset timestamp)
        => timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
