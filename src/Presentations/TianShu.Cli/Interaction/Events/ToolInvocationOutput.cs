using System.Globalization;
using System.Text.Json;
using TianShu.Cli.Interaction;

namespace TianShu.Cli.Interaction.Events;

internal sealed record ToolInvocationOutput(
    string? RawText,
    string? Summary,
    int? ExitCode,
    double? DurationSeconds,
    bool IsCancellation = false)
{
    public static ToolInvocationOutput? FromRaw(ToolPresentationKind kind, string? rawText, string? status)
    {
        var raw = ToolInvocationJsonHelpers.NormalizeDisplayText(rawText);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (IsCancellationText(raw))
        {
            return new ToolInvocationOutput(raw, "工具执行已取消。", null, null, IsCancellation: true);
        }

        if (!ToolInvocationJsonHelpers.TryParseJsonObject(raw, out var document) || document is null)
        {
            return new ToolInvocationOutput(raw, raw, null, null);
        }

        using (document)
        {
            var root = document.RootElement;
            var textOutput = ReadJsonOutputText(root, "output")
                             ?? ReadJsonOutputText(root, "stdout")
                             ?? ReadJsonOutputText(root, "stderr")
                             ?? ReadJsonOutputText(root, "message")
                             ?? ReadJsonOutputText(root, "error");
            if (!string.IsNullOrWhiteSpace(textOutput))
            {
                if (IsCancellationText(textOutput))
                {
                    return new ToolInvocationOutput(raw, "工具执行已取消。", null, null, IsCancellation: true);
                }

                return new ToolInvocationOutput(raw, textOutput, null, null);
            }

            var exitCode = ReadJsonInt(root, "exit_code")
                           ?? ReadJsonInt(root, "exitCode")
                           ?? ReadNestedJsonInt(root, "metadata", "exit_code")
                           ?? ReadNestedJsonInt(root, "metadata", "exitCode");
            if (exitCode is not null)
            {
                var duration = ReadJsonDouble(root, "duration_seconds")
                               ?? ReadJsonDouble(root, "durationSeconds")
                               ?? ReadNestedJsonDouble(root, "metadata", "duration_seconds")
                               ?? ReadNestedJsonDouble(root, "metadata", "durationSeconds");
                var prefix = exitCode.Value == 0 ? "完成" : "失败";
                var suffix = duration is null ? string.Empty : $"，耗时 {FormatDurationSeconds(duration.Value)}";
                return new ToolInvocationOutput(raw, $"{prefix}，退出码 {exitCode.Value}{suffix}", exitCode, duration);
            }

            var fallback = kind == ToolPresentationKind.Command
                           && string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
                ? null
                : ToolInvocationJsonHelpers.BuildCompactJsonSummary(root);
            return new ToolInvocationOutput(raw, fallback, null, null);
        }
    }

    private static string? ReadJsonOutputText(JsonElement root, string propertyName)
    {
        if (!ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? ToolInvocationJsonHelpers.NormalizeDisplayText(value.GetString())
            : null;
    }

    private static bool IsCancellationText(string value)
    {
        var normalized = ToolInvocationJsonHelpers.NormalizeDisplayText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Equals("A task was canceled.", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("A task was cancelled.", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("The operation was canceled.", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("The operation was cancelled.", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("TaskCanceledException", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("OperationCanceledException", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ReadJsonInt(JsonElement root, string propertyName)
    {
        if (!ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static int? ReadNestedJsonInt(JsonElement root, string objectName, string propertyName)
        => ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, objectName, out var nested)
           && nested.ValueKind == JsonValueKind.Object
            ? ReadJsonInt(nested, propertyName)
            : null;

    private static double? ReadJsonDouble(JsonElement root, string propertyName)
    {
        if (!ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static double? ReadNestedJsonDouble(JsonElement root, string objectName, string propertyName)
        => ToolInvocationJsonHelpers.TryGetJsonPropertyIgnoreCase(root, objectName, out var nested)
           && nested.ValueKind == JsonValueKind.Object
            ? ReadJsonDouble(nested, propertyName)
            : null;

    private static string FormatDurationSeconds(double seconds)
        => seconds < 10
            ? $"{seconds:0.0}s"
            : $"{seconds:0}s";
}
