using System.Text;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 对宿主侧命令进行只读/危险性分类，供审批与执行策略做前置判定。
/// Classifies host-side commands as read-only or dangerous for approval and execution policy checks.
/// </summary>
internal static class KernelCommandSafetyClassifier
{
    private static readonly HashSet<string> SafeExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "cat",
        "cd",
        "cut",
        "echo",
        "expr",
        "false",
        "grep",
        "head",
        "id",
        "ls",
        "nl",
        "paste",
        "pwd",
        "rev",
        "seq",
        "stat",
        "tail",
        "tr",
        "true",
        "uname",
        "uniq",
        "wc",
        "which",
        "whoami",
    };

    private static readonly HashSet<string> SafePowerShellCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "echo",
        "write-output",
        "write-host",
        "dir",
        "ls",
        "get-childitem",
        "gci",
        "cat",
        "type",
        "gc",
        "get-content",
        "select-string",
        "sls",
        "findstr",
        "measure-object",
        "measure",
        "get-location",
        "gl",
        "pwd",
        "test-path",
        "tp",
        "resolve-path",
        "rvpa",
        "select-object",
        "select",
        "get-item",
        "git",
        "rg",
    };

    private static readonly HashSet<string> BrowserExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "chrome.exe",
        "msedge",
        "msedge.exe",
        "firefox",
        "firefox.exe",
        "iexplore",
        "iexplore.exe",
    };

    public static bool IsKnownSafeCommand(IReadOnlyList<string> commandArgs)
    {
        if (commandArgs.Count == 0)
        {
            return false;
        }

        if (TryParsePowerShellCommands(commandArgs, out var powerShellCommands))
        {
            return powerShellCommands.Count > 0
                   && powerShellCommands.All(IsSafePowerShellCommand);
        }

        return IsSafeDirectCommand(commandArgs);
    }

    public static bool CommandMightBeDangerous(IReadOnlyList<string> commandArgs)
    {
        if (commandArgs.Count == 0)
        {
            return false;
        }

        if (TryParsePowerShellCommands(commandArgs, out var powerShellCommands)
            && powerShellCommands.Any(IsDangerousPowerShellCommand))
        {
            return true;
        }

        if (IsDangerousCmdCommand(commandArgs) || IsDangerousDirectCommand(commandArgs))
        {
            return true;
        }

        return false;
    }

    private static bool IsSafeDirectCommand(IReadOnlyList<string> commandArgs)
    {
        var executable = GetExecutableKey(commandArgs[0]);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        if (SafeExecutables.Contains(executable))
        {
            return true;
        }

        return executable switch
        {
            "base64" => IsSafeBase64Command(commandArgs),
            "find" => IsSafeFindCommand(commandArgs),
            "rg" => IsSafeRipgrepCommand(commandArgs),
            "git" => IsSafeGitCommand(commandArgs),
            "sed" => IsSafeSedCommand(commandArgs),
            _ => false,
        };
    }

    private static bool IsSafeBase64Command(IReadOnlyList<string> commandArgs)
    {
        return !commandArgs.Skip(1).Any(static arg =>
            string.Equals(arg, "-o", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--output", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--output=", StringComparison.OrdinalIgnoreCase)
            || (arg.StartsWith("-o", StringComparison.OrdinalIgnoreCase) && !string.Equals(arg, "-o", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsSafeFindCommand(IReadOnlyList<string> commandArgs)
    {
        return !commandArgs.Any(static arg =>
            arg is "-exec" or "-execdir" or "-ok" or "-okdir" or "-delete" or "-fls" or "-fprint" or "-fprint0" or "-fprintf");
    }

    private static bool IsSafeRipgrepCommand(IReadOnlyList<string> commandArgs)
    {
        return !commandArgs.Skip(1).Any(static arg =>
            arg is "--search-zip" or "-z"
            || string.Equals(arg, "--pre", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--hostname-bin", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--pre=", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--hostname-bin=", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafeGitCommand(IReadOnlyList<string> commandArgs)
    {
        var subcommandIndex = FindGitSubcommand(commandArgs, out var subcommand);
        if (subcommandIndex < 0 || string.IsNullOrWhiteSpace(subcommand))
        {
            return false;
        }

        var subcommandArgs = commandArgs.Skip(subcommandIndex + 1).ToArray();
        return subcommand switch
        {
            "status" or "log" or "diff" or "show" or "cat-file" => GitArgsAreReadOnly(subcommandArgs),
            "branch" => GitArgsAreReadOnly(subcommandArgs) && GitBranchArgsAreReadOnly(subcommandArgs),
            _ => false,
        };
    }

    private static bool IsSafeSedCommand(IReadOnlyList<string> commandArgs)
    {
        if (commandArgs.Count is < 3 or > 4)
        {
            return false;
        }

        return string.Equals(commandArgs[1], "-n", StringComparison.Ordinal)
               && IsValidSedPrintExpression(commandArgs[2]);
    }

    private static bool TryParsePowerShellCommands(
        IReadOnlyList<string> commandArgs,
        out IReadOnlyList<IReadOnlyList<string>> commands)
    {
        commands = Array.Empty<IReadOnlyList<string>>();
        if (commandArgs.Count == 0 || !IsPowerShellExecutable(commandArgs[0]))
        {
            return false;
        }

        if (!TryExtractPowerShellScript(commandArgs, out var script))
        {
            return false;
        }

        var segments = SplitCommandSequence(script);
        if (segments.Count == 0)
        {
            return false;
        }

        var parsedCommands = new List<IReadOnlyList<string>>(segments.Count);
        foreach (var segment in segments)
        {
            var tokens = Tokenize(segment);
            if (tokens.Count == 0)
            {
                return false;
            }

            parsedCommands.Add(tokens);
        }

        commands = parsedCommands;
        return true;
    }

    private static bool TryExtractPowerShellScript(IReadOnlyList<string> commandArgs, out string script)
    {
        script = string.Empty;
        if (commandArgs.Count < 2)
        {
            return false;
        }

        var index = 1;
        while (index < commandArgs.Count)
        {
            var arg = commandArgs[index];
            if (string.Equals(arg, "-NoLogo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-NoProfile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-NonInteractive", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-Mta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-Sta", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            if (string.Equals(arg, "-Command", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "/Command", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-c", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= commandArgs.Count || index + 2 != commandArgs.Count)
                {
                    return false;
                }

                script = commandArgs[index + 1];
                return !string.IsNullOrWhiteSpace(script);
            }

            if (arg.StartsWith("-Command:", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("/Command:", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 != commandArgs.Count)
                {
                    return false;
                }

                var separatorIndex = arg.IndexOf(':');
                if (separatorIndex < 0 || separatorIndex + 1 >= arg.Length)
                {
                    return false;
                }

                script = arg[(separatorIndex + 1)..];
                return !string.IsNullOrWhiteSpace(script);
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                return false;
            }

            script = JoinArgumentsAsScript(commandArgs.Skip(index));
            return !string.IsNullOrWhiteSpace(script);
        }

        return false;
    }

    private static bool IsSafePowerShellCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0 || tokens.Any(static token => token.Contains('>', StringComparison.Ordinal)))
        {
            return false;
        }

        foreach (var token in tokens)
        {
            var inner = token.Trim('(', ')').TrimStart('-');
            if (inner.Equals("set-content", StringComparison.OrdinalIgnoreCase)
                || inner.Equals("add-content", StringComparison.OrdinalIgnoreCase)
                || inner.Equals("out-file", StringComparison.OrdinalIgnoreCase)
                || inner.Equals("new-item", StringComparison.OrdinalIgnoreCase)
                || inner.Equals("remove-item", StringComparison.OrdinalIgnoreCase)
                || inner.Equals("move-item", StringComparison.OrdinalIgnoreCase)
                || inner.Equals("copy-item", StringComparison.OrdinalIgnoreCase)
                || inner.Equals("rename-item", StringComparison.OrdinalIgnoreCase)
                || inner.Equals("start-process", StringComparison.OrdinalIgnoreCase)
                || inner.Equals("stop-process", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var command = tokens[0].Trim('(', ')').TrimStart('-');
        if (command.Contains("outputencoding", StringComparison.OrdinalIgnoreCase)
            && command.Contains("[console]::", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!SafePowerShellCommands.Contains(command))
        {
            return false;
        }

        return command switch
        {
            "git" => IsSafeGitCommand(tokens),
            "rg" => IsSafeRipgrepCommand(tokens),
            _ => true,
        };
    }

    private static bool IsDangerousPowerShellCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        var lowerTokens = tokens
            .Select(static token => token.Trim('\'', '"').ToLowerInvariant())
            .ToArray();
        var hasUrl = lowerTokens.Any(ContainsUrl);

        if (hasUrl && lowerTokens.Any(static token =>
                token is "start-process" or "start" or "saps" or "invoke-item" or "ii"
                || token.Contains("shellexecute", StringComparison.Ordinal)
                || token.Contains("shell.application", StringComparison.Ordinal)))
        {
            return true;
        }

        var first = lowerTokens[0];
        if ((first is "rundll32" or "rundll32.exe")
            && lowerTokens.Any(static token => token.Contains("url.dll,fileprotocolhandler", StringComparison.Ordinal))
            && hasUrl)
        {
            return true;
        }

        if ((first is "mshta" or "mshta.exe") && hasUrl)
        {
            return true;
        }

        if ((first is "explorer" or "explorer.exe" || BrowserExecutables.Contains(first)) && hasUrl)
        {
            return true;
        }

        return HasForceDeletePowerShellCommand(lowerTokens);
    }

    private static bool HasForceDeletePowerShellCommand(IReadOnlyList<string> lowerTokens)
    {
        var segments = SplitSegments(lowerTokens);
        foreach (var segment in segments)
        {
            if (segment.Count == 0)
            {
                continue;
            }

            var command = segment[0];
            if ((command is "remove-item" or "ri" or "rm" or "del" or "erase")
                && segment.Any(static token => token is "-force" or "/f"))
            {
                return true;
            }

            if ((command is "rd" or "rmdir")
                && segment.Any(static token => token is "/s" or "-recurse")
                && segment.Any(static token => token is "/q" or "-force"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDangerousCmdCommand(IReadOnlyList<string> commandArgs)
    {
        if (!IsCmdExecutable(commandArgs[0]) || commandArgs.Count < 3)
        {
            return false;
        }

        var index = 1;
        while (index < commandArgs.Count)
        {
            var option = commandArgs[index];
            if (string.Equals(option, "/c", StringComparison.OrdinalIgnoreCase)
                || string.Equals(option, "/r", StringComparison.OrdinalIgnoreCase)
                || string.Equals(option, "-c", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (option.StartsWith("/", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            return false;
        }

        if (index >= commandArgs.Count - 1)
        {
            return false;
        }

        var body = commandArgs.Skip(index + 1).ToArray();
        var tokens = body.Length == 1
            ? Tokenize(body[0])
            : body.ToList();
        var segments = SplitSegments(tokens.Select(static token => token.ToLowerInvariant()).ToArray());
        foreach (var segment in segments)
        {
            if (segment.Count == 0)
            {
                continue;
            }

            var command = segment[0];
            if (command == "start" && segment.Any(ContainsUrl))
            {
                return true;
            }

            if ((command is "del" or "erase") && segment.Any(static token => token is "/f" or "-f"))
            {
                return true;
            }

            if ((command is "rd" or "rmdir")
                && segment.Any(static token => token == "/s")
                && segment.Any(static token => token == "/q"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDangerousDirectCommand(IReadOnlyList<string> commandArgs)
    {
        var executable = GetExecutableKey(commandArgs[0]);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        if (string.Equals(executable, "rm", StringComparison.OrdinalIgnoreCase))
        {
            var next = commandArgs.Skip(1).FirstOrDefault();
            return string.Equals(next, "-f", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(next, "-rf", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(next, "-fr", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(executable, "sudo", StringComparison.OrdinalIgnoreCase))
        {
            return CommandMightBeDangerous(commandArgs.Skip(1).ToArray());
        }

        if ((string.Equals(executable, "explorer", StringComparison.OrdinalIgnoreCase)
             || string.Equals(executable, "mshta", StringComparison.OrdinalIgnoreCase)
             || string.Equals(executable, "rundll32", StringComparison.OrdinalIgnoreCase)
             || BrowserExecutables.Contains(executable))
            && commandArgs.Skip(1).Any(ContainsUrl))
        {
            return true;
        }

        if (string.Equals(executable, "rundll32", StringComparison.OrdinalIgnoreCase)
            && commandArgs.Skip(1).Any(static token => token.Contains("url.dll,FileProtocolHandler", StringComparison.OrdinalIgnoreCase))
            && commandArgs.Skip(1).Any(ContainsUrl))
        {
            return true;
        }

        return false;
    }

    private static int FindGitSubcommand(IReadOnlyList<string> commandArgs, out string? subcommand)
    {
        subcommand = null;
        if (commandArgs.Count == 0 || !string.Equals(GetExecutableKey(commandArgs[0]), "git", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        var skipNext = false;
        for (var index = 1; index < commandArgs.Count; index++)
        {
            var arg = commandArgs[index];
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            if (IsGitGlobalOptionWithInlineValue(arg))
            {
                continue;
            }

            if (IsGitGlobalOptionWithSeparateValue(arg))
            {
                skipNext = true;
                continue;
            }

            if (string.Equals(arg, "--", StringComparison.Ordinal) || arg.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            subcommand = arg.ToLowerInvariant();
            return index;
        }

        return -1;
    }

    private static bool GitArgsAreReadOnly(IReadOnlyList<string> args)
    {
        return !args.Any(static arg =>
            arg is "--output" or "--ext-diff" or "--textconv" or "--exec" or "--paginate"
            || arg.StartsWith("--output=", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--exec=", StringComparison.OrdinalIgnoreCase));
    }

    private static bool GitBranchArgsAreReadOnly(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return true;
        }

        var sawReadOnlyFlag = false;
        foreach (var arg in args)
        {
            if (arg is "--list" or "-l" or "--show-current" or "-a" or "--all" or "-r" or "--remotes" or "-v" or "-vv" or "--verbose"
                || arg.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
            {
                sawReadOnlyFlag = true;
                continue;
            }

            return false;
        }

        return sawReadOnlyFlag;
    }

    private static bool IsGitGlobalOptionWithSeparateValue(string arg)
    {
        return arg is "-C" or "-c" or "--config-env" or "--exec-path" or "--git-dir" or "--namespace" or "--super-prefix" or "--work-tree";
    }

    private static bool IsGitGlobalOptionWithInlineValue(string arg)
    {
        return arg.StartsWith("--config-env=", StringComparison.OrdinalIgnoreCase)
               || arg.StartsWith("--exec-path=", StringComparison.OrdinalIgnoreCase)
               || arg.StartsWith("--git-dir=", StringComparison.OrdinalIgnoreCase)
               || arg.StartsWith("--namespace=", StringComparison.OrdinalIgnoreCase)
               || arg.StartsWith("--super-prefix=", StringComparison.OrdinalIgnoreCase)
               || arg.StartsWith("--work-tree=", StringComparison.OrdinalIgnoreCase)
               || ((arg.StartsWith("-C", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("-c", StringComparison.OrdinalIgnoreCase)) && arg.Length > 2);
    }

    private static bool IsValidSedPrintExpression(string value)
    {
        if (!value.EndsWith('p'))
        {
            return false;
        }

        var core = value[..^1];
        if (string.IsNullOrWhiteSpace(core))
        {
            return false;
        }

        var parts = core.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => int.TryParse(parts[0], out _),
            2 => int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _),
            _ => false,
        };
    }

    private static string? GetExecutableKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var fileName = Path.GetFileName(raw);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = raw;
        }

        var normalized = fileName.ToLowerInvariant();
        foreach (var suffix in new[] { ".exe", ".cmd", ".bat", ".com" })
        {
            if (normalized.EndsWith(suffix, StringComparison.Ordinal))
            {
                return normalized[..^suffix.Length];
            }
        }

        return normalized;
    }

    private static bool IsPowerShellExecutable(string raw)
    {
        var fileName = Path.GetFileName(raw) ?? raw;
        return fileName.Equals("powershell", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCmdExecutable(string raw)
    {
        var fileName = Path.GetFileName(raw) ?? raw;
        return fileName.Equals("cmd", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsUrl(string text)
    {
        return text.Contains("http://", StringComparison.OrdinalIgnoreCase)
               || text.Contains("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static List<IReadOnlyList<string>> SplitSegments(IReadOnlyList<string> tokens)
    {
        var segments = new List<IReadOnlyList<string>>();
        var current = new List<string>();
        foreach (var token in tokens)
        {
            if (token is ";" or "|" or "||" or "&" or "&&")
            {
                if (current.Count > 0)
                {
                    segments.Add(current.ToArray());
                    current.Clear();
                }

                continue;
            }

            current.Add(token);
        }

        if (current.Count > 0)
        {
            segments.Add(current.ToArray());
        }

        return segments;
    }

    private static List<string> SplitCommandSequence(string script)
    {
        var segments = new List<string>();
        var builder = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = 0; index < script.Length; index++)
        {
            var current = script[index];
            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (current is ';' or '\n' or '\r')
                {
                    AppendSegment(builder, segments);
                    continue;
                }

                if ((current == '&' || current == '|')
                    && index + 1 < script.Length
                    && script[index + 1] == current)
                {
                    AppendSegment(builder, segments);
                    index++;
                    continue;
                }

                if (current == '|')
                {
                    AppendSegment(builder, segments);
                    continue;
                }
            }

            builder.Append(current);
        }

        AppendSegment(builder, segments);
        return segments;
    }

    private static void AppendSegment(StringBuilder builder, List<string> segments)
    {
        var value = builder.ToString().Trim();
        builder.Clear();
        if (!string.IsNullOrWhiteSpace(value))
        {
            segments.Add(value);
        }
    }

    private static List<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && char.IsWhiteSpace(current))
            {
                FlushToken(builder, tokens);
                continue;
            }

            builder.Append(current);
        }

        FlushToken(builder, tokens);
        return tokens;
    }

    private static void FlushToken(StringBuilder builder, List<string> tokens)
    {
        if (builder.Length == 0)
        {
            return;
        }

        tokens.Add(builder.ToString());
        builder.Clear();
    }

    private static string JoinArgumentsAsScript(IEnumerable<string> args)
    {
        static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "''";
            }

            if (value.All(static ch => !char.IsWhiteSpace(ch)))
            {
                return value;
            }

            return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
        }

        return string.Join(" ", args.Select(Quote));
    }
}
