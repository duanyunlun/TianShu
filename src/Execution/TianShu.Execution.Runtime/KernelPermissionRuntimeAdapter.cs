using System.Text.Json;

namespace TianShu.Execution.Runtime;

internal enum KernelRuntimeConfiguredShellEnvironmentPolicyInherit
{
    Core,
    All,
    None,
}

internal sealed record KernelRuntimeConfiguredShellEnvironmentPolicySettings(
    KernelRuntimeConfiguredShellEnvironmentPolicyInherit Inherit,
    bool IgnoreDefaultExcludes,
    IReadOnlyList<string> ExcludePatterns,
    IReadOnlyDictionary<string, string> SetVariables,
    IReadOnlyList<string> IncludeOnlyPatterns,
    bool UseProfile);

/// <summary>
/// 将宿主配置阶段的权限解析结果翻译为 runtime 可直接消费的权限设置。
/// Translates host-configuration permission results into runtime-facing permission settings.
/// </summary>
internal readonly record struct KernelResolvedPermissionRuntimeSettings(
    KernelApprovalPolicy ApprovalPolicy,
    JsonElement SandboxPolicy,
    string SandboxMode,
    bool AllowLoginShell,
    KernelShellEnvironmentPolicy ShellEnvironmentPolicy);

/// <summary>
/// 权限配置结果与 runtime 权限对象之间的适配器。
/// Adapter between resolved permission configuration and runtime permission objects.
/// </summary>
internal static class KernelPermissionRuntimeAdapter
{
    public static KernelResolvedPermissionRuntimeSettings CreateResolvedPermissionSettings(
        object? approvalPolicyValue,
        JsonElement sandboxPolicy,
        string sandboxMode,
        bool allowLoginShell,
        KernelRuntimeConfiguredShellEnvironmentPolicySettings shellEnvironmentPolicy,
        KernelApprovalPolicy defaultApprovalPolicy)
    {
        var approvalPolicy = TryReadApprovalPolicy(approvalPolicyValue, out var configuredApprovalPolicy)
            ? configuredApprovalPolicy ?? defaultApprovalPolicy
            : defaultApprovalPolicy;

        return new KernelResolvedPermissionRuntimeSettings(
            approvalPolicy,
            sandboxPolicy,
            sandboxMode,
            allowLoginShell,
            CreateShellEnvironmentPolicy(shellEnvironmentPolicy));
    }

    public static KernelShellEnvironmentPolicy CreateShellEnvironmentPolicy(KernelRuntimeConfiguredShellEnvironmentPolicySettings settings)
    {
        return new KernelShellEnvironmentPolicy(
            settings.Inherit switch
            {
                KernelRuntimeConfiguredShellEnvironmentPolicyInherit.Core => KernelShellEnvironmentPolicyInherit.Core,
                KernelRuntimeConfiguredShellEnvironmentPolicyInherit.None => KernelShellEnvironmentPolicyInherit.None,
                _ => KernelShellEnvironmentPolicyInherit.All,
            },
            settings.IgnoreDefaultExcludes,
            settings.ExcludePatterns,
            settings.SetVariables,
            settings.IncludeOnlyPatterns,
            settings.UseProfile);
    }

    private static bool TryReadApprovalPolicy(object? rawValue, out KernelApprovalPolicy? approvalPolicy)
    {
        approvalPolicy = null;
        try
        {
            switch (rawValue)
            {
                case null:
                    return false;
                case KernelApprovalPolicy typedPolicy:
                    approvalPolicy = typedPolicy;
                    return true;
                case string text:
                    approvalPolicy = KernelApprovalPolicy.Parse(text);
                    return true;
                case JsonElement element when element.ValueKind is JsonValueKind.String or JsonValueKind.Object:
                    approvalPolicy = JsonSerializer.Deserialize<KernelApprovalPolicy>(element.GetRawText());
                    return approvalPolicy is not null;
                case Dictionary<string, object?> dictionary:
                    approvalPolicy = JsonSerializer.Deserialize<KernelApprovalPolicy>(JsonSerializer.Serialize(dictionary));
                    return approvalPolicy is not null;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            approvalPolicy = null;
            return false;
        }
    }
}
