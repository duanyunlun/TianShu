using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 命令执行审批请求附带的技能元数据。
/// Skill metadata attached to command execution approval requests.
/// </summary>
internal sealed record KernelCommandExecutionApprovalSkillMetadata(string PathToSkillsMd);

/// <summary>
/// 供宿主审批链路复用的命令审批与沙箱判定辅助件。
/// Shared helpers for host-side command approval and sandbox gating flows.
/// </summary>
internal static class KernelCommandApprovalUtilities
{
    public static string? BuildCommandApprovalSessionKey(KernelCommandApprovalRequest? request)
    {
        return request is null
            ? null
            : BuildCommandApprovalSessionKey(
                request.Command,
                request.Cwd,
                request.Tty,
                request.SandboxPermissionsValue,
                request.RequestedAdditionalPermissions);
    }

    public static string? BuildCommandApprovalSessionKey(
        IReadOnlyList<string> commandArgs,
        string? cwd,
        bool tty,
        string? sandboxPermissionsValue,
        KernelPermissionGrantProfile? additionalPermissions)
    {
        var normalizedCommand = commandArgs
            .Select(KernelToolJsonHelpers.Normalize)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        var normalizedCwd = KernelToolJsonHelpers.Normalize(cwd);
        if (normalizedCommand.Length == 0 || string.IsNullOrWhiteSpace(normalizedCwd))
        {
            return null;
        }

        var payload = new Dictionary<string, object?>
        {
            ["command"] = normalizedCommand,
            ["cwd"] = Path.GetFullPath(normalizedCwd!),
            ["tty"] = tty,
            ["sandbox_permissions"] = NormalizeSandboxPermissionsForApprovalKey(sandboxPermissionsValue),
            ["additional_permissions"] = additionalPermissions?.BuildServerPayload(),
        };
        return JsonSerializer.Serialize(payload);
    }

    public static string NormalizeSandboxPermissionsForApprovalKey(string? sandboxPermissionsValue)
    {
        var normalized = KernelToolJsonHelpers.Normalize(sandboxPermissionsValue);
        return string.IsNullOrWhiteSpace(normalized)
            ? "use_default"
            : normalized.Replace('-', '_').ToLowerInvariant();
    }

    public static KernelCommandExecutionApprovalSkillMetadata? TryResolveCommandExecutionApprovalSkillMetadata(
        IReadOnlyList<string> commandArgs,
        string cwd)
    {
        var resolved = KernelSkillMetadataResolver.TryResolveForCommand(commandArgs, cwd);
        return resolved is null
            ? null
            : new KernelCommandExecutionApprovalSkillMetadata(resolved.PathToSkillsMd);
    }

    public static IReadOnlyList<object?> BuildCommandExecutionAvailableDecisions(KernelExecPolicyAmendment? proposedAmendment)
    {
        var decisions = new List<object?>
        {
            "accept",
            "acceptForSession",
        };

        if (proposedAmendment is not null)
        {
            decisions.Add(new
            {
                acceptWithExecpolicyAmendment = new
                {
                    execpolicy_amendment = proposedAmendment.CommandPrefix.ToArray(),
                },
            });
        }

        decisions.Add("decline");
        decisions.Add("cancel");
        return decisions;
    }

    public static string BuildCommandApprovalDeclinedMessage(string reason, string? sandboxMode)
    {
        var normalized = KernelToolJsonHelpers.Normalize(reason) ?? string.Empty;
        if (normalized.StartsWith("approval_policy_requires_confirmation", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("exec_policy_rule_requires_confirmation", StringComparison.OrdinalIgnoreCase))
        {
            return "命令未获批准（审批策略）。";
        }

        if (normalized.StartsWith("sandbox_policy_denied:", StringComparison.OrdinalIgnoreCase))
        {
            var mode = normalized["sandbox_policy_denied:".Length..];
            return $"命令被沙箱策略阻止：{KernelToolJsonHelpers.Normalize(mode) ?? sandboxMode ?? "unknown"}";
        }

        return BuildCommandPolicyDeniedMessage(reason, sandboxMode);
    }

    public static string BuildCommandPolicyDeniedMessage(string reason, string? sandboxMode)
    {
        var normalized = KernelToolJsonHelpers.Normalize(reason) ?? string.Empty;
        if (normalized.Equals("exec_policy_rule_denied", StringComparison.OrdinalIgnoreCase))
        {
            return "命令被 ExecPolicy 规则阻止。";
        }

        if (normalized.StartsWith("sandbox_policy_denied:", StringComparison.OrdinalIgnoreCase))
        {
            var mode = normalized["sandbox_policy_denied:".Length..];
            return $"命令被沙箱策略阻止：{KernelToolJsonHelpers.Normalize(mode) ?? sandboxMode ?? "unknown"}";
        }

        if (normalized.Contains("approval", StringComparison.OrdinalIgnoreCase))
        {
            return $"命令被策略阻止：{normalized}";
        }

        return $"命令被策略阻止：{normalized}";
    }

    public static bool TryResolveCommandApprovalDecision(string? decision, out bool approvedForSession)
    {
        approvedForSession = false;
        var normalized = NormalizeApprovalDecision(decision);
        if (string.Equals(normalized, "acceptForSession", StringComparison.OrdinalIgnoreCase))
        {
            approvedForSession = true;
            return true;
        }

        return string.Equals(normalized, "accept", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "acceptWithExecpolicyAmendment", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresCommandApproval(string? approvalPolicy)
    {
        var policy = KernelToolJsonHelpers.Normalize(approvalPolicy);
        if (string.IsNullOrWhiteSpace(policy))
        {
            return false;
        }

        return policy.Equals("on-request", StringComparison.OrdinalIgnoreCase)
               || policy.Equals("onrequest", StringComparison.OrdinalIgnoreCase)
               || policy.Equals("always", StringComparison.OrdinalIgnoreCase)
               || policy.Equals("untrusted", StringComparison.OrdinalIgnoreCase)
               || policy.Equals("on-failure", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCommandAllowedBySandbox(
        IReadOnlyList<string> commandArgs,
        string commandPreview,
        string? sandboxMode)
    {
        var mode = KernelToolJsonHelpers.Normalize(sandboxMode) ?? "workspaceWrite";
        if (mode.Equals("danger-full-access", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("dangerFullAccess", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!mode.Contains("read", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (commandArgs.Count == 0)
        {
            return false;
        }

        var first = commandArgs[0];
        if (first.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
            || first.Equals("/bin/sh", StringComparison.OrdinalIgnoreCase)
            || first.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || first.Equals("powershell", StringComparison.OrdinalIgnoreCase))
        {
            first = commandPreview;
        }

        return !LooksMutatingCommand(first);
    }

    public static bool LooksMutatingCommand(string commandText)
    {
        var text = KernelToolJsonHelpers.Normalize(commandText)?.ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var mutatingPatterns = new[]
        {
            " rm ",
            " del ",
            " move ",
            " copy ",
            " cp ",
            " mv ",
            " mkdir ",
            " rmdir ",
            " git add",
            " git commit",
            " git push",
            " git reset",
            " git checkout",
            " dotnet add",
            " dotnet new",
            " dotnet publish",
            " dotnet build",
            " npm install",
            " pnpm add",
            " yarn add",
            " cargo build",
            " cargo test",
            " touch ",
            " >",
            ">>",
        };

        var padded = $" {text} ";
        return mutatingPatterns.Any(padded.Contains);
    }

    private static string? NormalizeApprovalDecision(string? decision)
    {
        var normalized = KernelToolJsonHelpers.Normalize(decision);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.ToLowerInvariant() switch
        {
            "accept" => "accept",
            "approved" => "accept",
            "approve" => "accept",
            "acceptforsession" => "acceptForSession",
            "accept_for_session" => "acceptForSession",
            "acceptandremember" => "acceptAndRemember",
            "accept_and_remember" => "acceptAndRemember",
            "acceptwithexecpolicyamendment" => "acceptWithExecpolicyAmendment",
            "accept_with_execpolicy_amendment" => "acceptWithExecpolicyAmendment",
            "applynetworkpolicyamendment" => "applyNetworkPolicyAmendment",
            "apply_network_policy_amendment" => "applyNetworkPolicyAmendment",
            "decline" => "decline",
            "denied" => "decline",
            "deny" => "decline",
            "reject" => "decline",
            "rejected" => "decline",
            "cancel" => "cancel",
            _ => normalized,
        };
    }
}
