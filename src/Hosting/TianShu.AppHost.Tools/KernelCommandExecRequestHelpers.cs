using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// `command/exec` 请求的参数读取、校验与环境合并原语。
/// Provider-neutral request parsing, validation, and environment merging primitives for `command/exec`.
/// </summary>
internal static class KernelCommandExecRequestHelpers
{
    public static bool ShouldUseTrackedCommandExec(string? processId, bool tty, bool streamStdin, bool streamStdoutStderr)
        => !string.IsNullOrWhiteSpace(processId) || tty || streamStdin || streamStdoutStderr;

    public static bool TryValidateCommandExecV2Params(
        string? processId,
        bool tty,
        bool streamStdin,
        bool streamStdoutStderr,
        bool background,
        bool disableTimeout,
        int? timeoutMs,
        bool disableOutputCap,
        int? outputBytesCap,
        bool hasSize,
        out int errorCode,
        out string? errorMessage)
    {
        errorCode = 0;
        errorMessage = null;

        if (background && ShouldUseTrackedCommandExec(processId, tty, streamStdin, streamStdoutStderr))
        {
            errorCode = -32602;
            errorMessage = "command/exec background mode cannot be combined with processId, tty, or streaming";
            return false;
        }

        if (disableTimeout && timeoutMs is not null)
        {
            errorCode = -32602;
            errorMessage = "command/exec cannot set both timeoutMs and disableTimeout";
            return false;
        }

        if (timeoutMs is < 0)
        {
            errorCode = -32602;
            errorMessage = $"command/exec timeoutMs must be non-negative, got {timeoutMs.Value}";
            return false;
        }

        if (disableOutputCap && outputBytesCap is not null)
        {
            errorCode = -32602;
            errorMessage = "command/exec cannot set both outputBytesCap and disableOutputCap";
            return false;
        }

        if (outputBytesCap is < 0)
        {
            errorCode = -32602;
            errorMessage = $"command/exec outputBytesCap must be non-negative, got {outputBytesCap.Value}";
            return false;
        }

        if (ShouldUseTrackedCommandExec(processId, tty, streamStdin, streamStdoutStderr)
            && string.IsNullOrWhiteSpace(processId))
        {
            errorCode = -32600;
            errorMessage = "command/exec tty or streaming requires a client-supplied processId";
            return false;
        }

        if (hasSize && !tty)
        {
            errorCode = -32602;
            errorMessage = "command/exec size requires tty: true";
            return false;
        }

        return true;
    }

    public static bool TryReadCommandExecEnvOverrides(
        JsonElement @params,
        out Dictionary<string, string?>? overrides,
        out string? errorMessage)
    {
        overrides = null;
        errorMessage = null;
        if (!@params.TryGetProperty("env", out var envElement) || envElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (envElement.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "command/exec env must be an object";
            return false;
        }

        overrides = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in envElement.EnumerateObject())
        {
            var key = KernelToolJsonHelpers.Normalize(property.Name);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    overrides[key!] = property.Value.GetString();
                    break;
                case JsonValueKind.Null:
                    overrides[key!] = null;
                    break;
                default:
                    errorMessage = $"command/exec env[{property.Name}] must be string or null";
                    return false;
            }
        }

        return true;
    }

    public static Dictionary<string, string> MergeCommandExecEnvironment(
        IReadOnlyDictionary<string, string> baseEnvironment,
        IReadOnlyDictionary<string, string?>? overrides)
    {
        var merged = new Dictionary<string, string>(baseEnvironment, StringComparer.Ordinal);
        if (overrides is null)
        {
            return merged;
        }

        foreach (var entry in overrides)
        {
            if (entry.Value is null)
            {
                merged.Remove(entry.Key);
                continue;
            }

            merged[entry.Key] = entry.Value;
        }

        return merged;
    }

    public static bool TryReadCommandExecTerminalSize(
        JsonElement sizeElement,
        out KernelCommandExecTerminalSize? size,
        out string? errorMessage)
    {
        size = null;
        errorMessage = null;

        if (sizeElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (sizeElement.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "command/exec size must be an object";
            return false;
        }

        var rows = KernelToolJsonHelpers.ReadInt(sizeElement, "rows") ?? 0;
        var cols = KernelToolJsonHelpers.ReadInt(sizeElement, "cols") ?? 0;
        if (rows <= 0 || cols <= 0)
        {
            errorMessage = "command/exec size rows and cols must be greater than 0";
            return false;
        }

        size = new KernelCommandExecTerminalSize(rows, cols);
        return true;
    }
}

/// <summary>
/// `command/exec` 的终端窗口尺寸契约。
/// Terminal window size contract used by tracked `command/exec` sessions.
/// </summary>
internal readonly record struct KernelCommandExecTerminalSize(int Rows, int Cols)
{
    public static KernelCommandExecTerminalSize Default => new(24, 80);
}
