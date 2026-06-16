using System.Security.Cryptography;
using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// Unified exec 工具的请求解析、schema 与输出 payload 构造辅助方法。
/// Request parsing, schema, and output payload helpers for unified exec tools.
/// </summary>
internal static class KernelUnifiedExecToolHelpers
{
    public static readonly JsonElement OutputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            chunk_id = new { type = "string" },
            wall_time_seconds = new { type = "number" },
            exit_code = new { type = "number" },
            session_id = new { type = "number" },
            original_token_count = new { type = "number" },
            output = new { type = "string" },
        },
        required = new[] { "wall_time_seconds", "output" },
        additionalProperties = false,
    });

    public static bool TryResolveCommand(
        JsonElement arguments,
        bool allowLoginShell,
        out List<string> command,
        out string? error)
    {
        command = ReadCommandArray(arguments, "command");
        if (command.Count > 0)
        {
            error = null;
            return true;
        }

        var commandText = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "cmd"))
            ?? KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "command"));
        if (string.IsNullOrWhiteSpace(commandText))
        {
            error = "exec_command requires a non-empty command.";
            return false;
        }

        var login = KernelToolJsonHelpers.ReadBool(arguments, "login");
        if (!KernelShellCommandBuilder.TryResolveUseLoginShell(login, allowLoginShell, out var useLoginShell, out error))
        {
            return false;
        }

        command = KernelShellCommandBuilder.BuildDefaultCommand(commandText!, useLoginShell);
        error = null;
        return true;
    }

    public static List<string> ReadCommandArray(JsonElement arguments, string propertyName)
    {
        var list = new List<string>();
        if (!arguments.TryGetProperty(propertyName, out var commandElement) || commandElement.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in commandElement.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            var normalized = KernelToolJsonHelpers.Normalize(text);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                list.Add(normalized!);
            }
        }

        return list;
    }

    public static string ResolveCwd(string contextCwd, string? requestedCwd)
    {
        if (string.IsNullOrWhiteSpace(requestedCwd))
        {
            return contextCwd;
        }

        return Path.IsPathRooted(requestedCwd)
            ? Path.GetFullPath(requestedCwd)
            : Path.GetFullPath(Path.Combine(contextCwd, requestedCwd));
    }

    public static string BuildPayload(
        int sessionId,
        bool hasExited,
        int? exitCode,
        string output,
        int? originalTokenCount,
        TimeSpan wallTime)
    {
        var payload = new Dictionary<string, object?>
        {
            ["chunk_id"] = GenerateChunkId(),
            ["wall_time_seconds"] = wallTime.TotalSeconds,
            ["output"] = output,
        };
        if (hasExited && exitCode is not null)
        {
            payload["exit_code"] = exitCode.Value;
        }
        else
        {
            payload["session_id"] = sessionId;
        }

        if (originalTokenCount is not null)
        {
            payload["original_token_count"] = originalTokenCount.Value;
        }

        return JsonSerializer.Serialize(payload);
    }

    private static string GenerateChunkId()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
    }
}
