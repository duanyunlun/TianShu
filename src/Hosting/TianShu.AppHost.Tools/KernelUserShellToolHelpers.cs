using System.Text.Json.Serialization;

namespace TianShu.AppHost.Tools;

/// <summary>
/// User shell 执行结果与历史回放文本的宿主工具辅助方法。
/// Host-tool helpers for user shell result payloads and history replay text formatting.
/// </summary>
internal static class KernelUserShellToolHelpers
{
    public const string UserShellCommandRecordType = "userShellCommandExecution";
    public const int UserShellCommandReplayTokenLimit = 2500;

    public static KernelUserShellRunResultPayload BuildRunResult(
        string turnId,
        string itemId,
        string turnStatus,
        string itemStatus,
        int exitCode,
        string stdout,
        string stderr,
        bool reusedActiveTurn)
        => new()
        {
            TurnId = turnId,
            ItemId = itemId,
            TurnStatus = turnStatus,
            ItemStatus = itemStatus,
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr,
            ReusedActiveTurn = reusedActiveTurn,
        };

    public static string BuildCommandHistoryText(
        string command,
        int exitCode,
        long? durationMs,
        string aggregatedOutput)
    {
        var output = KernelTextTruncator.FormattedTruncateTokens(
            aggregatedOutput ?? string.Empty,
            UserShellCommandReplayTokenLimit,
            out _);
        var durationSeconds = Math.Max(0, durationMs ?? 0) / 1000d;
        return string.Join("\n",
            "<user_shell_command>",
            "<command>",
            command,
            "</command>",
            "<result>",
            $"Exit code: {exitCode}",
            $"Duration: {durationSeconds:0.0000} seconds",
            "Output:",
            output,
            "</result>",
            "</user_shell_command>");
    }
}

/// <summary>
/// User shell 运行结果的强类型 northbound payload。
/// Typed northbound payload describing the outcome of a user shell run.
/// </summary>
internal sealed class KernelUserShellRunResultPayload
{
    [JsonPropertyName("turnId")]
    public string TurnId { get; init; } = string.Empty;

    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("turnStatus")]
    public string TurnStatus { get; init; } = string.Empty;

    [JsonPropertyName("itemStatus")]
    public string ItemStatus { get; init; } = string.Empty;

    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }

    [JsonPropertyName("stdout")]
    public string Stdout { get; init; } = string.Empty;

    [JsonPropertyName("stderr")]
    public string Stderr { get; init; } = string.Empty;

    [JsonPropertyName("reusedActiveTurn")]
    public bool ReusedActiveTurn { get; init; }
}
