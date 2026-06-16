using System.Text.Json;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// permission profile 解析阶段使用的宿主配置载体。
/// Host-configuration carriers used while resolving permission profiles.
/// </summary>
internal enum KernelPermissionConfigSyntax
{
    Legacy,
    Profiles,
}

internal enum KernelConfiguredShellEnvironmentPolicyInherit
{
    Core,
    All,
    None,
}

internal sealed record KernelConfiguredShellEnvironmentPolicySettings(
    KernelConfiguredShellEnvironmentPolicyInherit Inherit,
    bool IgnoreDefaultExcludes,
    IReadOnlyList<string> ExcludePatterns,
    IReadOnlyDictionary<string, string> SetVariables,
    IReadOnlyList<string> IncludeOnlyPatterns,
    bool UseProfile)
{
    public static KernelConfiguredShellEnvironmentPolicySettings Default { get; } = new(
        KernelConfiguredShellEnvironmentPolicyInherit.All,
        IgnoreDefaultExcludes: true,
        Array.Empty<string>(),
        new Dictionary<string, string>(StringComparer.Ordinal),
        Array.Empty<string>(),
        UseProfile: false);
}

internal sealed record KernelResolvedPermissionConfiguration(
    object? ApprovalPolicyValue,
    JsonElement SandboxPolicy,
    string SandboxMode,
    bool AllowLoginShell,
    KernelConfiguredShellEnvironmentPolicySettings ShellEnvironmentPolicy);

internal sealed class KernelCompiledPermissionState
{
    public bool IncludePlatformDefaults { get; set; }

    public bool FullDiskRead { get; set; }

    public bool FullDiskWrite { get; set; }

    public bool RootWriteRequested { get; set; }

    public bool WorkspaceRootWrite { get; set; }

    public bool TmpdirWritable { get; set; }

    public bool SlashTmpWritable { get; set; }

    public bool HasExplicitDenyEntries { get; set; }

    public bool HasWriteNarrowingEntries { get; set; }

    public List<string> WritableRoots { get; } = new();

    public List<string> ReadableRoots { get; } = new();
}
