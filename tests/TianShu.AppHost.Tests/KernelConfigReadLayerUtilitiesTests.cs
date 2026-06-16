using System.Text.Json;
using TianShu.AppHost.Configuration;
namespace TianShu.AppHost.Tests;

public sealed class KernelConfigReadLayerUtilitiesTests
{
    [Fact]
    public void BuildProjectDocScopedConfig_ShouldKeepOnlyNonProjectRootMarkers()
    {
        var systemLayer = new KernelConfigReadLayer(
            new
            {
                type = "user",
            },
            Version: "v1",
            Config: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["project_root_markers"] = new List<object?> { ".git", ".hg" },
                ["approval_policy"] = "on-request",
            });
        var projectLayer = new KernelConfigReadLayer(
            new
            {
                type = "project",
            },
            Version: "v2",
            Config: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["project_root_markers"] = new List<object?> { "custom.marker" },
                ["approval_policy"] = "never",
            });
        var snapshot = new KernelConfigReadSnapshot(
            Config: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["project_root_markers"] = new List<object?> { "custom.marker" },
                ["approval_policy"] = "never",
            },
            Origins: new Dictionary<string, object?>(StringComparer.Ordinal),
            Layers: null,
            HasPersistentConfig: true,
            OrderedLayers: [systemLayer, projectLayer]);

        var scoped = KernelConfigReadLayerUtilities.BuildProjectDocScopedConfig(snapshot);

        var markers = Assert.IsAssignableFrom<List<object?>>(scoped["project_root_markers"]);
        Assert.Equal([".git", ".hg"], markers.Cast<string?>());
        Assert.Equal("never", scoped["approval_policy"]);
    }

    [Fact]
    public void BuildConfigObjectFromOverridePayload_ShouldConvertNestedJsonValues()
    {
        var config = KernelConfigReadLayerUtilities.BuildConfigObjectFromOverrideElement(JsonSerializer.SerializeToElement(new
        {
            profile = "deep",
            managed_network = new
            {
                enabled = true,
                ports = new[] { 8080, 1080 },
            },
        }));

        Assert.Equal("deep", config["profile"]);
        var managedNetwork = Assert.IsType<Dictionary<string, object?>>(config["managed_network"]);
        Assert.Equal(true, managedNetwork["enabled"]);
        var ports = Assert.IsAssignableFrom<List<object?>>(managedNetwork["ports"]);
        Assert.Equal([8080L, 1080L], ports.Cast<long>());
    }
}
