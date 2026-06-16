using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 命令执行审批阶段使用的标准化请求对象。
/// Normalized command approval request used by host approval flows.
/// </summary>
internal sealed record KernelCommandApprovalRequest(
    IReadOnlyList<string> Command,
    string CommandPreview,
    string Cwd,
    bool Tty = false,
    string? SandboxPermissionsValue = null,
    KernelPermissionGrantProfile? RequestedAdditionalPermissions = null)
{
    public bool RequestsFreshSandboxOverride(KernelPermissionGrantProfile? grantedPermissions)
    {
        var sandboxPermissions = NormalizeSandboxPermissionsValue(SandboxPermissionsValue);
        return sandboxPermissions switch
        {
            "require_escalated" => true,
            "with_additional_permissions" => RequestedAdditionalPermissions is { IsEmpty: false } requested
                                             && grantedPermissions?.Covers(requested) != true,
            _ => false,
        };
    }

    private static string? NormalizeSandboxPermissionsValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Replace('-', '_').ToLowerInvariant();
    }
}

/// <summary>
/// 将 shell / shell_command / exec_command 的参数统一投影为审批可读命令。
/// Projects shell-family tool arguments into a host-readable command approval request.
/// </summary>
internal static class KernelToolCommandApprovalResolver
{
    public static bool IsSupportedTool(string toolName)
    {
        return string.Equals(toolName, "shell", StringComparison.Ordinal)
               || string.Equals(toolName, "local_shell", StringComparison.Ordinal)
               || string.Equals(toolName, "container.exec", StringComparison.Ordinal)
               || string.Equals(toolName, "shell_command", StringComparison.Ordinal)
               || string.Equals(toolName, "exec_command", StringComparison.Ordinal);
    }

    public static bool TryResolve(
        string toolName,
        JsonElement arguments,
        bool allowLoginShell,
        string cwd,
        out KernelCommandApprovalRequest? request,
        out string? errorMessage)
    {
        request = null;
        errorMessage = null;

        if (string.Equals(toolName, "shell", StringComparison.Ordinal)
            || string.Equals(toolName, "local_shell", StringComparison.Ordinal)
            || string.Equals(toolName, "container.exec", StringComparison.Ordinal))
        {
            return TryResolveShell(arguments, cwd, out request, out errorMessage);
        }

        if (string.Equals(toolName, "shell_command", StringComparison.Ordinal))
        {
            return TryResolveShellCommand(arguments, allowLoginShell, cwd, out request, out errorMessage);
        }

        if (string.Equals(toolName, "exec_command", StringComparison.Ordinal))
        {
            return TryResolveExecCommand(arguments, allowLoginShell, cwd, out request, out errorMessage);
        }

        return false;
    }

    private static bool TryResolveShell(
        JsonElement arguments,
        string cwd,
        out KernelCommandApprovalRequest? request,
        out string? errorMessage)
    {
        request = null;
        errorMessage = null;

        if (!KernelToolArgumentParser.TryParse(arguments, out KernelShellToolCallArguments? parsedArguments, out errorMessage))
        {
            return false;
        }

        var command = KernelShellCommandBuilder.NormalizeExplicitCommand(
            NormalizeCommandArguments(parsedArguments!.Command));
        if (command.Count == 0)
        {
            errorMessage = "shell requires a non-empty command.";
            return false;
        }

        request = new KernelCommandApprovalRequest(
            command,
            string.Join(" ", command),
            cwd,
            false,
            parsedArguments.SandboxPermissions,
            TryResolveAdditionalPermissions(parsedArguments.AdditionalPermissions, cwd));
        return true;
    }

    private static bool TryResolveShellCommand(
        JsonElement arguments,
        bool allowLoginShell,
        string cwd,
        out KernelCommandApprovalRequest? request,
        out string? errorMessage)
    {
        request = null;
        errorMessage = null;

        if (!KernelToolArgumentParser.TryParse(arguments, out KernelShellCommandToolCallArguments? parsedArguments, out errorMessage))
        {
            return false;
        }

        var commandText = KernelToolJsonHelpers.Normalize(parsedArguments!.Command);
        if (string.IsNullOrWhiteSpace(commandText))
        {
            errorMessage = "shell_command requires a non-empty command.";
            return false;
        }

        if (!KernelShellCommandBuilder.TryResolveUseLoginShell(
                parsedArguments.Login,
                allowLoginShell,
                out var useLoginShell,
                out errorMessage))
        {
            return false;
        }

        request = new KernelCommandApprovalRequest(
            KernelShellCommandBuilder.BuildDefaultCommand(commandText!, useLoginShell),
            commandText!,
            cwd,
            false,
            parsedArguments.SandboxPermissions,
            TryResolveAdditionalPermissions(parsedArguments.AdditionalPermissions, cwd));
        return true;
    }

    private static bool TryResolveExecCommand(
        JsonElement arguments,
        bool allowLoginShell,
        string cwd,
        out KernelCommandApprovalRequest? request,
        out string? errorMessage)
    {
        request = null;
        errorMessage = null;

        var command = ReadCommandArray(arguments, "command");
        if (command.Count > 0)
        {
            request = new KernelCommandApprovalRequest(
                command,
                string.Join(" ", command),
                cwd,
                false,
                KernelToolJsonHelpers.ReadString(arguments, "sandbox_permissions"),
                TryResolveAdditionalPermissions(arguments, cwd));
            return true;
        }

        var commandText = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "cmd"))
            ?? KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "command"));
        if (string.IsNullOrWhiteSpace(commandText))
        {
            errorMessage = "exec_command requires a non-empty command.";
            return false;
        }

        var login = KernelToolJsonHelpers.ReadBool(arguments, "login");
        if (!KernelShellCommandBuilder.TryResolveUseLoginShell(
                login,
                allowLoginShell,
                out var useLoginShell,
                out errorMessage))
        {
            return false;
        }

        request = new KernelCommandApprovalRequest(
            KernelShellCommandBuilder.BuildDefaultCommand(commandText!, useLoginShell),
            commandText!,
            cwd,
            false,
            KernelToolJsonHelpers.ReadString(arguments, "sandbox_permissions"),
            TryResolveAdditionalPermissions(arguments, cwd));
        return true;
    }

    private static List<string> ReadCommandArray(JsonElement arguments, string propertyName)
    {
        var list = new List<string>();
        if (!arguments.TryGetProperty(propertyName, out var commandElement)
            || commandElement.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in commandElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var normalized = KernelToolJsonHelpers.Normalize(item.GetString());
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                list.Add(normalized!);
            }
        }

        return list;
    }

    private static List<string> NormalizeCommandArguments(IEnumerable<string>? values)
    {
        var normalized = new List<string>();
        if (values is null)
        {
            return normalized;
        }

        foreach (var value in values)
        {
            var text = KernelToolJsonHelpers.Normalize(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                normalized.Add(text!);
            }
        }

        return normalized;
    }

    private static KernelPermissionGrantProfile? TryResolveAdditionalPermissions(
        KernelAdditionalPermissionArguments? additionalPermissions,
        string cwd)
    {
        return KernelPermissionGrantProfile.TryCreateFromAdditionalPermissions(
            additionalPermissions,
            cwd,
            out var profile,
            out _)
            ? profile
            : null;
    }

    private static KernelPermissionGrantProfile? TryResolveAdditionalPermissions(JsonElement arguments, string cwd)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("additional_permissions", out var additionalPermissions)
            || additionalPermissions.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return KernelPermissionGrantProfile.TryParseAdditionalPermissions(
            additionalPermissions,
            cwd,
            out var profile,
            out _)
            ? profile
            : null;
    }
}
