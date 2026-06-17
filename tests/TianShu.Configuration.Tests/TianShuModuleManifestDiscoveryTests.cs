using TianShu.Contracts.Modules;

namespace TianShu.Configuration.Tests;

public sealed class TianShuModuleManifestDiscoveryTests
{
    [Fact]
    public void Load_ShouldScanGenericModuleTomlAndSelectCandidates()
    {
        using var temp = TempTianShuHome.Create();
        WriteModuleManifest(
            Path.Combine(temp.Root, "modules", "company-provider", "module.toml"),
            "provider.company",
            "Provider",
            enabled: true,
            priority: 5);

        var snapshot = new TianShuModuleManifestDiscovery().Load(temp.Root);

        var candidate = Assert.Single(snapshot.SelectedCandidates);
        Assert.Equal("provider.company", candidate.Manifest.ModuleId);
        Assert.Equal(ModuleKind.Provider, candidate.Manifest.Kind);
        Assert.Equal(ModuleDiscoverySourceKind.UserHome, candidate.Manifest.Source.Root.SourceKind);
        Assert.Equal(ModuleTrustLevel.UserInstalled, candidate.Manifest.Source.Root.TrustLevel);
        Assert.Equal(5, candidate.Manifest.Priority);
        Assert.Contains("provider:chat", candidate.Manifest.Capabilities);
        Assert.Equal("0.6.0", candidate.Manifest.SdkContractVersion);
        Assert.Equal("1.0.0", candidate.Manifest.CapabilitySchemaVersion);
    }

    [Fact]
    public void Load_ShouldPreferBuiltInDescriptorWhenLocalManifestDuplicatesModuleId()
    {
        using var temp = TempTianShuHome.Create();
        WriteModuleManifest(
            Path.Combine(temp.Root, "modules", "diagnostics-override", "module.toml"),
            "diagnostics",
            "Diagnostics",
            enabled: true,
            priority: -100);

        var snapshot = new TianShuModuleManifestDiscovery().Load(
            temp.Root,
            builtInDescriptors: [BuiltInModuleDescriptors.Diagnostics()]);

        var selected = Assert.Single(snapshot.SelectedCandidates);
        Assert.Equal("diagnostics", selected.Manifest.ModuleId);
        Assert.Equal(ModuleDiscoverySourceKind.BuiltIn, selected.Manifest.Source.Root.SourceKind);
        Assert.Contains(snapshot.Candidates, static candidate => candidate.Status == ModuleDiscoveryCandidateStatus.DuplicateRejected);
        Assert.Contains(snapshot.Issues, static issue => issue.Code == "module_discovery.duplicate_rejected");
    }

    [Fact]
    public void Load_ShouldKeepDisabledManifestOutOfSelectedCandidates()
    {
        using var temp = TempTianShuHome.Create();
        WriteModuleManifest(
            Path.Combine(temp.Root, "modules", "disabled-tool", "module.toml"),
            "tool.disabled",
            "Tool",
            enabled: false);

        var snapshot = new TianShuModuleManifestDiscovery().Load(temp.Root);

        var candidate = Assert.Single(snapshot.Candidates);
        Assert.Equal(ModuleDiscoveryCandidateStatus.Disabled, candidate.Status);
        Assert.Empty(snapshot.SelectedCandidates);
        Assert.Contains(snapshot.Issues, static issue => issue.Code == "module_discovery.disabled");
    }

    [Fact]
    public void Load_ShouldReportMalformedManifestWithoutThrowing()
    {
        using var temp = TempTianShuHome.Create();
        var path = Path.Combine(temp.Root, "modules", "broken", "module.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            """
            id = "broken.module"
            kind = "Tool"
            enabled = true
            """);

        var snapshot = new TianShuModuleManifestDiscovery().Load(temp.Root);

        Assert.Empty(snapshot.Candidates);
        Assert.Empty(snapshot.SelectedCandidates);
        Assert.Contains(snapshot.Issues, static issue => issue.Code == "module_manifest.parse_failed");
    }

    private static void WriteModuleManifest(
        string path,
        string moduleId,
        string kind,
        bool enabled,
        int priority = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            id = "{{moduleId}}"
            kind = "{{kind}}"
            display_name = "{{moduleId}}"
            version = "1.0.0"
            enabled = {{enabled.ToString().ToLowerInvariant()}}
            priority = {{priority}}
            min_tianshu_version = "0.6.0"
            sdk_contract_version = "0.6.0"
            capability_schema_version = "1.0.0"
            capabilities = ["provider:chat"]
            diagnostics = ["module:discovery"]

            [implementation]
            project = "TianShu.Samples.Provider"
            type = "TianShu.Samples.Provider.Module"
            package_id = "{{moduleId}}"
            """);
    }

    private sealed class TempTianShuHome : IDisposable
    {
        private TempTianShuHome(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TempTianShuHome Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-module-manifest-discovery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TempTianShuHome(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
