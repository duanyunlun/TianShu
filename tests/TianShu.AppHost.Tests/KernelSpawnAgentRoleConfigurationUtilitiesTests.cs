using TianShu.AppHost.Configuration;

namespace TianShu.AppHost.Tests;

public sealed class KernelSpawnAgentRoleConfigurationUtilitiesTests
{
    [Fact]
    public void ResolveSpawnAgentRoleDefinitions_ShouldReadExactAgentRoleConfig()
    {
        var config = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["agents"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["custom"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["description"] = "Custom role",
                    ["config_file"] = "custom-role.toml",
                    ["nickname_candidates"] = new List<object?> { "Atlas", "Noether" },
                },
            },
        };
        var snapshot = new KernelConfigReadSnapshot(
            config,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            Layers: null,
            HasPersistentConfig: true,
            OrderedLayers:
            [
                new KernelConfigReadLayer(new { type = "user" }, "v1", config),
            ]);

        var roles = KernelSpawnAgentRoleConfigurationUtilities.ResolveSpawnAgentRoleDefinitions(
            snapshot,
            Environment.CurrentDirectory);

        Assert.True(roles.TryGetValue("custom", out var role));
        Assert.Equal("Custom role", role.Description);
        Assert.Equal(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "custom-role.toml")), role.ResolvedConfigFilePath);
        Assert.Equal(["Atlas", "Noether"], role.NicknameCandidates);
    }
}
