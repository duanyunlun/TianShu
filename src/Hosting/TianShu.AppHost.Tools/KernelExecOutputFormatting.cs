using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 负责把 shell 执行结果转换成结构化或 freeform 输出文本，供不同工具面复用。
/// Converts shell execution results into structured or freeform text payloads for reuse across host tool surfaces.
/// </summary>
internal static class KernelExecOutputFormatting
{
    // Fallback model metadata uses TruncationPolicyConfig::bytes(10_000).
    private const int DefaultModelTruncationChars = 10_000;

    public static string FormatStructured(KernelExecToolCallOutput execOutput)
    {
        var durationSeconds = RoundToSingleDecimal(execOutput.Duration.TotalSeconds);
        var content = BuildContentWithTimeout(execOutput);
        var formatted = KernelTextTruncator.FormattedTruncate(content, DefaultModelTruncationChars);

        var payload = new Dictionary<string, object?>
        {
            ["output"] = formatted,
            ["metadata"] = new Dictionary<string, object?>
            {
                ["exit_code"] = execOutput.ExitCode,
                ["duration_seconds"] = durationSeconds,
            },
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static string FormatFreeform(KernelExecToolCallOutput execOutput)
    {
        var durationSeconds = RoundToSingleDecimal(execOutput.Duration.TotalSeconds);
        var content = BuildContentWithTimeout(execOutput);
        var totalLines = CountLines(content);
        var formattedOutput = KernelTextTruncator.Truncate(content, DefaultModelTruncationChars);
        var formattedLines = CountLines(formattedOutput);

        var sections = new List<string>(6)
        {
            $"Exit code: {execOutput.ExitCode}",
            $"Wall time: {durationSeconds:0.0} seconds",
        };

        if (totalLines != formattedLines)
        {
            sections.Add($"Total output lines: {totalLines}");
        }

        sections.Add("Output:");
        sections.Add(formattedOutput);

        return string.Join('\n', sections);
    }

    private static double RoundToSingleDecimal(double value)
        => Math.Round(value * 10, MidpointRounding.AwayFromZero) / 10;

    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 0;
        }

        var lines = 1;
        foreach (var ch in content)
        {
            if (ch == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static string BuildContentWithTimeout(KernelExecToolCallOutput execOutput)
    {
        if (!execOutput.TimedOut)
        {
            return execOutput.AggregatedOutput;
        }

        var durationMs = (long)Math.Max(0, execOutput.Duration.TotalMilliseconds);
        return $"command timed out after {durationMs} milliseconds\n{execOutput.AggregatedOutput}";
    }
}
