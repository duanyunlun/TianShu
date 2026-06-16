namespace TianShu.Configuration.Tests;

public sealed class TianShuProviderManifestConfigurationTests
{
    [Fact]
    public void Load_ScansProviderPackageManifests()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "model", "provider-adapters", "builtin", "provider.toml"), "builtin", "openai_responses");
        WriteManifest(Path.Combine(temp.Root, "modules", "model", "provider-adapters", "company", "provider.toml"), "company", "company_protocol");

        var projection = new TianShuProviderManifestConfiguration().Load(temp.Root);

        Assert.Equal(2, projection.Files.Count);
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("builtin", "provider.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("company", "provider.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("builtin", projection.SelectedPackage?.Id);
        Assert.Equal("openai_responses", Assert.Single(projection.SelectedPackage!.Adapters).Id);
    }

    [Fact]
    public void SavePackage_UpdatesAdapterManifestWithoutTouchingTianShuToml()
    {
        using var temp = TempTianShuHome.Create();
        var configPath = Path.Combine(temp.Root, "tianshu.toml");
        var manifestPath = Path.Combine(temp.Root, "modules", "model", "provider-adapters", "builtin", "provider.toml");
        File.WriteAllText(configPath, "model = \"gpt-test\"\n");
        WriteManifest(manifestPath, "builtin", "openai_responses");

        var configuration = new TianShuProviderManifestConfiguration();
        var package = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        package.Version = "1.2.3";
        package.MinTianShuVersion = "0.1.0";
        package.Capabilities = ["provider:chat", "provider:reasoning"];
        package.Diagnostics = ["provider:load"];
        package.Adapters =
        [
            .. package.Adapters,
            new ProviderAdapterManifestValue
            {
                Id = "config_gui_smoke",
                DisplayName = "ConfigGUI Smoke Provider",
                Enabled = true,
                Type = "assembly",
                AssemblyPath = "./smoke/TianShu.Provider.Smoke.dll",
                Priority = 99,
            },
        ];

        configuration.SavePackage(manifestPath, package);

        var saved = File.ReadAllText(manifestPath);
        Assert.Contains("id = \"config_gui_smoke\"", saved, StringComparison.Ordinal);
        Assert.Contains("version = \"1.2.3\"", saved, StringComparison.Ordinal);
        Assert.Contains("min_tianshu_version = \"0.1.0\"", saved, StringComparison.Ordinal);
        Assert.Contains("capabilities = [\"provider:chat\", \"provider:reasoning\"]", saved, StringComparison.Ordinal);
        Assert.Contains("diagnostics = [\"provider:load\"]", saved, StringComparison.Ordinal);
        Assert.Contains("display_name = \"ConfigGUI Smoke Provider\"", saved, StringComparison.Ordinal);
        Assert.Contains("assembly_path = \"./smoke/TianShu.Provider.Smoke.dll\"", saved, StringComparison.Ordinal);
        Assert.Equal("model = \"gpt-test\"\n", File.ReadAllText(configPath));

        var reloaded = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        Assert.Equal("1.2.3", reloaded.Version);
        Assert.Equal("0.1.0", reloaded.MinTianShuVersion);
        Assert.Equal(["provider:chat", "provider:reasoning"], reloaded.Capabilities);
        Assert.Equal(["provider:load"], reloaded.Diagnostics);
    }

    [Fact]
    public void Load_WhenVersionIncompatible_ReportsUnavailableIssue()
    {
        using var temp = TempTianShuHome.Create();
        var manifestPath = Path.Combine(temp.Root, "modules", "model", "provider-adapters", "future", "provider.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(
            manifestPath,
            """
            id = "future"
            display_name = "future"
            enabled = true
            type = "assembly"
            priority = 0
            min_tianshu_version = "99.0.0"

            [[adapters]]
            id = "future_protocol"
            enabled = true
            type = "assembly"
            assembly_path = "./future/Provider.dll"
            """);

        var projection = new TianShuProviderManifestConfiguration().Load(temp.Root, manifestPath);

        Assert.Equal("future", projection.SelectedPackage?.Id);
        Assert.True(projection.SelectedPackage!.Enabled);
        Assert.Equal("unavailable", projection.SelectedPackage.LoadStatus);
        Assert.Contains(projection.Issues, static issue => issue.Code == "provider_manifest.version_incompatible");
    }

    [Fact]
    public void CreateCopyAndDeletePackage_OnlyWritesProvidersDirectory()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuProviderManifestConfiguration();

        var createdPath = configuration.CreatePackage(temp.Root, "company-providers");
        Assert.Equal(Path.Combine(temp.Root, "modules", "model", "provider-adapters", "company-providers", "provider.toml"), createdPath);
        Assert.True(File.Exists(createdPath));

        var copiedPath = configuration.CopyPackage(temp.Root, createdPath, "company-providers-copy");
        Assert.Equal(Path.Combine(temp.Root, "modules", "model", "provider-adapters", "company-providers-copy", "provider.toml"), copiedPath);
        Assert.True(File.Exists(copiedPath));

        configuration.DeletePackage(temp.Root, copiedPath);
        Assert.False(File.Exists(copiedPath));
    }

    [Fact]
    public void CreatePackage_RejectsPathsOutsideProvidersRoot()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuProviderManifestConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "..\\outside"));
        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "nested\\provider"));
    }

    private static void WriteManifest(string path, string packageId, string adapterId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            id = "{{packageId}}"
            display_name = "{{packageId}}"
            enabled = true
            type = "builtin"
            priority = 0

            [[adapters]]
            id = "{{adapterId}}"
            display_name = "{{adapterId}}"
            enabled = true
            type = "assembly"
            assembly_path = "./{{adapterId}}/Provider.dll"
            priority = 10
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
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-provider-manifest-config-{Guid.NewGuid():N}");
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
