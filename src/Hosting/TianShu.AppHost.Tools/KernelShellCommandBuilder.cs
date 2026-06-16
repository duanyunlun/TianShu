namespace TianShu.AppHost.Tools;

/// <summary>
/// 构建宿主侧 shell 命令行参数，并统一处理登录 shell 与 PowerShell 编码细节。
/// Builds host-side shell command arguments and normalizes login-shell plus PowerShell encoding behavior.
/// </summary>
internal static class KernelShellCommandBuilder
{
    private const string PowerShellUtf8OutputPrefix = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;\n";

    public static bool TryResolveUseLoginShell(bool? requestedLogin, bool allowLoginShell, out bool useLoginShell, out string? error)
    {
        if (!allowLoginShell && requestedLogin == true)
        {
            useLoginShell = false;
            error = "login shell is disabled by config; omit `login` or set it to false.";
            return false;
        }

        useLoginShell = requestedLogin ?? allowLoginShell;
        error = null;
        return true;
    }

    public static List<string> BuildDefaultCommand(string commandText, bool useLoginShell)
        => BuildCommand(ResolveDefaultShellExecutable(), commandText, useLoginShell);

    public static List<string> BuildCommand(string shellExecutable, string commandText, bool useLoginShell)
    {
        var fileName = Path.GetFileName(shellExecutable) ?? shellExecutable;
        if (IsCmdShell(fileName))
        {
            return [shellExecutable, "/c", commandText];
        }

        var command = new List<string> { shellExecutable };
        if (IsPowerShellShell(fileName) && !useLoginShell)
        {
            command.Add("-NoProfile");
        }

        command.Add("-Command");
        command.Add(commandText);
        return command;
    }

    public static List<string> NormalizeExplicitCommand(IReadOnlyList<string> command)
    {
        if (!TryParsePowerShellCommand(command, out var commandFlagIndex, out var scriptIndex, out var hasNoProfile))
        {
            return command.ToList();
        }

        var normalized = new List<string>(command.Count + (hasNoProfile ? 0 : 1));
        normalized.Add(command[0]);
        if (!hasNoProfile)
        {
            normalized.Add("-NoProfile");
        }

        for (var i = 1; i < command.Count; i++)
        {
            if (i == scriptIndex)
            {
                normalized.Add(PrefixPowerShellScriptWithUtf8(command[i]));
                continue;
            }

            normalized.Add(command[i]);
        }

        return normalized;
    }

    public static string ResolveDefaultShellExecutable()
    {
        return FindExecutableOnPath("pwsh.exe")
               ?? FindExecutableOnPath("pwsh")
               ?? FindExecutableOnPath("powershell.exe")
               ?? "powershell.exe";
    }

    private static bool IsPowerShellShell(string fileName)
        => fileName.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("powershell", StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsCmdShell(string fileName)
        => fileName.Equals("cmd", StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);

    private static bool TryParsePowerShellCommand(
        IReadOnlyList<string> command,
        out int commandFlagIndex,
        out int scriptIndex,
        out bool hasNoProfile)
    {
        commandFlagIndex = -1;
        scriptIndex = -1;
        hasNoProfile = false;

        if (command.Count < 3)
        {
            return false;
        }

        var fileName = Path.GetFileName(command[0]) ?? command[0];
        if (!IsPowerShellShell(fileName))
        {
            return false;
        }

        for (var i = 1; i < command.Count; i++)
        {
            var arg = command[i];
            if (string.Equals(arg, "-NoProfile", StringComparison.OrdinalIgnoreCase))
            {
                hasNoProfile = true;
                continue;
            }

            if (string.Equals(arg, "-Command", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-c", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= command.Count)
                {
                    return false;
                }

                commandFlagIndex = i;
                scriptIndex = i + 1;
                return true;
            }
        }

        return false;
    }

    private static string PrefixPowerShellScriptWithUtf8(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return script;
        }

        return script.TrimStart().StartsWith(PowerShellUtf8OutputPrefix, StringComparison.Ordinal)
            ? script
            : string.Concat(PowerShellUtf8OutputPrefix, script);
    }

    private static string? FindExecutableOnPath(string executable)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in paths)
        {
            var candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
