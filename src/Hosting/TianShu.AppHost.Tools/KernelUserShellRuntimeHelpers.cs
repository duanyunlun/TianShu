using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// user-shell 本地执行运行时辅助方法，负责生成 item id 与历史文本投影。
/// Runtime helpers for user-shell local execution, including item id generation and history text projection.
/// </summary>
internal static class KernelUserShellRuntimeHelpers
{
    public static string NextItemId()
        => $"cmd_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}";

    public static bool TryBuildCommandHistoryText(string? itemType, JsonElement payload, out string historyText)
    {
        historyText = string.Empty;
        var normalizedType = KernelToolJsonHelpers.Normalize(itemType);
        if (!string.Equals(normalizedType, KernelUserShellToolHelpers.UserShellCommandRecordType, StringComparison.OrdinalIgnoreCase)
            || payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var command = KernelToolJsonHelpers.ReadString(payload, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var exitCode = KernelToolJsonHelpers.ReadInt(payload, "exitCode") ?? -1;
        var aggregatedOutput = KernelToolJsonHelpers.ReadString(payload, "aggregatedOutput") ?? string.Empty;
        long? durationMs = null;
        if (payload.TryGetProperty("durationMs", out var durationElement)
            && durationElement.ValueKind == JsonValueKind.Number
            && durationElement.TryGetInt64(out var parsedDurationMs))
        {
            durationMs = parsedDurationMs;
        }

        historyText = KernelUserShellToolHelpers.BuildCommandHistoryText(
            command!,
            exitCode,
            durationMs,
            aggregatedOutput);
        return true;
    }
}
