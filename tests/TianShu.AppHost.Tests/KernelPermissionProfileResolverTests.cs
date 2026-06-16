using TianShu.AppHost.Configuration;
using TianShu.Configuration;

namespace TianShu.AppHost.Tests;

public sealed class KernelPermissionProfileResolverTests
{
    [Fact]
    public void ResolvePermissionConfigSyntax_ShouldIgnoreLegacyCamelCaseSandboxConfigKeys()
    {
        var config = BuildLegacyCamelCaseSandboxConfig();

        var syntax = KernelPermissionProfileResolver.ResolvePermissionConfigSyntax(
            [new KernelConfigReadLayer(new { type = "user" }, "v1", config)],
            config);

        Assert.Null(syntax);
    }

    [Fact]
    public void ResolveConfiguredPermissionConfiguration_ShouldNotUseLegacyCamelCaseSandboxConfigKeys()
    {
        var config = BuildLegacyCamelCaseSandboxConfig();
        var snapshot = new KernelConfigReadSnapshot(
            config,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            Layers: null,
            HasPersistentConfig: true,
            OrderedLayers:
            [
                new KernelConfigReadLayer(new { type = "user" }, "v1", config),
            ]);

        var resolved = KernelPermissionProfileResolver.ResolveConfiguredPermissionConfiguration(
            snapshot,
            cwd: Environment.CurrentDirectory,
            defaultApprovalPolicyValue: null,
            tianShuHome: Path.Combine(Path.GetTempPath(), $"tianshu-home-{Guid.NewGuid():N}"),
            policyStrategyDefaults: new PolicyStrategyEffectiveDefaults(
                ApprovalPolicy: null,
                SandboxMode: "read-only",
                NetworkAccess: null,
                AllowLoginShell: null));

        Assert.Equal("readOnly", resolved.SandboxMode);
        Assert.Equal("readOnly", resolved.SandboxPolicy.GetProperty("type").GetString());
    }

    private static Dictionary<string, object?> BuildLegacyCamelCaseSandboxConfig()
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sandboxPolicy"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "danger-full-access",
                ["networkAccess"] = true,
            },
            ["sandboxWorkspaceWrite"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["writableRoots"] = new[] { "D:/legacy-write" },
                ["networkAccess"] = true,
                ["excludeTmpdirEnvVar"] = true,
                ["excludeSlashTmp"] = true,
            },
        };
    }
}
