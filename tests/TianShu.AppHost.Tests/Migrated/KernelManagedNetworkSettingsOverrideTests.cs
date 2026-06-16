using TianShu.AppHost.Configuration;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelManagedNetworkSettingsOverrideTests
{
    [Fact]
    public void ResolveManagedNetworkSettingsWithSkillOverride_ShouldReplaceConfiguredDomainLists()
    {
        var settings = ResolveManagedNetworkSettingsWithSkillOverrideForTest(
            userConfigToml: """
            default_permissions = "trusted"

            [permissions.trusted.network]
            enabled = true
            allowed_domains = ["global.example.com"]
            denied_domains = ["blocked.global.example.com"]
            """,
            requirementsToml: """
            [experimental_network]
            enabled = true
            """,
            skillAllowedDomains: Array.Empty<string>(),
            skillDeniedDomains: ["blocked.skill.example.com"]);

        Assert.Empty(settings.AllowedDomains);
        Assert.Equal(["blocked.skill.example.com"], settings.DeniedDomains);
    }

    private static KernelManagedNetworkSettings ResolveManagedNetworkSettingsWithSkillOverrideForTest(
        string? userConfigToml,
        string? requirementsToml,
        IReadOnlyList<string>? skillAllowedDomains,
        IReadOnlyList<string>? skillDeniedDomains,
        string? cwd = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-managed-network-settings-tests", Guid.NewGuid().ToString("N"));
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Directory.CreateDirectory(root);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            if (userConfigToml is not null)
            {
                File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), userConfigToml);
            }

            if (requirementsToml is not null)
            {
                File.WriteAllText(Path.Combine(tianShuHome, "requirements.toml"), requirementsToml);
            }

            var snapshot = KernelConfigSnapshotUtilities.BuildConfigReadSnapshot(
                includeLayers: false,
                cwd,
                processOverrideValues: new Dictionary<string, string>(StringComparer.Ordinal),
                userConfigPath: Path.Combine(tianShuHome, "tianshu.toml"));

            return KernelManagedNetworkSettingsUtilities.ResolveManagedNetworkSettingsWithSkillOverride(
                snapshot,
                skillAllowedDomains,
                skillDeniedDomains);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
