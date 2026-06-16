using TianShu.Contracts.Catalog;

namespace TianShu.AppHost.Tests;

public sealed class KernelAppCatalogUtilitiesTests
{
    [Fact]
    public void BuildAppConfigState_ShouldReadEnabledAndAccessibleFlags()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apps.demo.enabled"] = "\"false\"",
            ["apps.demo.isAccessible"] = "true",
            ["apps.other.enabled"] = "true",
            ["model"] = "gpt-5",
        };

        var state = KernelAppCatalogUtilities.BuildAppConfigState(values);

        Assert.False(state.EnabledStates["demo"]);
        Assert.True(state.AccessibleStates["demo"]);
        Assert.True(state.EnabledStates["other"]);
        Assert.False(state.AccessibleStates.ContainsKey("other"));
    }

    [Fact]
    public void BuildAppsFromConfig_ShouldIncludeConfiguredAndPluginApps()
    {
        var state = new KernelAppConfigState(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["demo"] = false,
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["demo"] = true,
            });
        var pluginDisplayNames = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["plugin-only"] = ["Plugin Alias"],
        };

        var apps = KernelAppCatalogUtilities.BuildAppsFromConfig(state, pluginDisplayNames, ["plugin-only"]);

        Assert.Equal(2, apps.Count);

        var demo = apps.Single(static item => item.Id == "demo");
        Assert.True(demo.IsAccessible);
        Assert.False(demo.IsEnabled);

        var pluginOnly = apps.Single(static item => item.Id == "plugin-only");
        Assert.True(pluginOnly.IsAccessible);
        Assert.True(pluginOnly.IsEnabled);
        Assert.Equal("Plugin Alias", Assert.Single(pluginOnly.PluginDisplayNames));
    }

    [Fact]
    public void BuildAppsFromConnectors_ShouldMergeDirectoryAndAccessibleCatalogs()
    {
        var state = new KernelAppConfigState(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["demo"] = false,
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        var pluginDisplayNames = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["demo"] = ["Workspace Alias"],
        };
        var directory = new[]
        {
            new KernelPluginConnectorInfo(
                "demo",
                "Demo App",
                "Directory description",
                "https://chatgpt.com/apps/demo/demo",
                ["Directory Alias"],
                "https://example.com/logo-light.png",
                null,
                "directory",
                new { category = "productivity" },
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tier"] = "beta",
                }),
        };
        var accessible = new[]
        {
            new KernelPluginConnectorInfo(
                "demo",
                "Demo App",
                null,
                null,
                ["Accessible Alias"],
                null,
                "https://example.com/logo-dark.png",
                null,
                null,
                new { provider = "chatgpt" },
                null),
        };

        var apps = KernelAppCatalogUtilities.BuildAppsFromConnectors(directory, accessible, state, pluginDisplayNames, []);

        var app = Assert.Single(apps);
        Assert.Equal("demo", app.Id);
        Assert.True(app.IsAccessible);
        Assert.False(app.IsEnabled);
        Assert.Equal("directory", app.DistributionChannel);
        Assert.Equal("beta", Assert.Single(app.Labels!).Value);
        Assert.Equal(
            ["Accessible Alias", "Directory Alias", "Workspace Alias"],
            app.PluginDisplayNames.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void MergeConnector_ShouldPreferExistingValuesAndMergeLabels()
    {
        var existing = new KernelPluginConnectorInfo(
            "demo",
            "Demo App",
            "Existing description",
            "https://existing.example/app",
            ["Existing Alias"],
            "https://existing.example/light.png",
            null,
            "directory",
            new { category = "productivity" },
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tier"] = "beta",
            });
        var incoming = new KernelPluginConnectorInfo(
            "demo",
            "Demo App",
            "Incoming description",
            "https://incoming.example/app",
            ["Incoming Alias"],
            null,
            "https://incoming.example/dark.png",
            "workspace",
            null,
            new { provider = "chatgpt" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = "workspace",
            });

        var merged = KernelAppCatalogUtilities.MergeConnector(existing, incoming);

        Assert.Equal("Existing description", merged.Description);
        Assert.Equal("https://existing.example/app", merged.InstallUrl);
        Assert.Equal("https://existing.example/light.png", merged.LogoUrl);
        Assert.Equal("https://incoming.example/dark.png", merged.LogoUrlDark);
        Assert.Equal("directory", merged.DistributionChannel);
        Assert.Equal("beta", merged.Labels!["tier"]);
        Assert.Equal("workspace", merged.Labels["source"]);
        Assert.Equal(
            ["Existing Alias", "Incoming Alias"],
            merged.PluginDisplayNames.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ShouldSendAppListUpdatedNotification_ShouldHonorAccessibleEntriesAndLoadCompletion(bool accessibleLoaded, bool directoryLoaded, bool expected)
    {
        var inaccessibleItems = new[]
        {
            new ControlPlaneAppDescriptor
            {
                Id = "demo",
                Name = "Demo App",
                IsAccessible = false,
                IsEnabled = true,
            },
        };

        var result = KernelAppCatalogUtilities.ShouldSendAppListUpdatedNotification(inaccessibleItems, accessibleLoaded, directoryLoaded);

        Assert.Equal(expected, result);
        Assert.True(KernelAppCatalogUtilities.ShouldSendAppListUpdatedNotification(
            [inaccessibleItems[0] with { IsAccessible = true }],
            accessibleLoaded: false,
            directoryLoaded: false));
    }
}
