namespace TianShu.Configuration.Tests;

public sealed class TianShuArtifactStoreManifestConfigurationTests
{
    [Fact]
    public void Load_ScansArtifactStorePackageManifests()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "artifacts", "stores", "builtin", "store.toml"), "builtin", "local-filesystem");
        WriteManifest(Path.Combine(temp.Root, "modules", "artifacts", "stores", "company", "store.toml"), "company", "company-store");

        var projection = new TianShuArtifactStoreManifestConfiguration().Load(temp.Root);

        Assert.Equal(2, projection.Files.Count);
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("builtin", "store.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("company", "store.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("builtin", projection.SelectedPackage?.Id);
        Assert.Equal("local-filesystem", Assert.Single(projection.SelectedPackage!.Stores).Id);
    }

    [Fact]
    public void SavePackage_UpdatesStoreManifestWithoutTouchingTianShuToml()
    {
        using var temp = TempTianShuHome.Create();
        var configPath = Path.Combine(temp.Root, "tianshu.toml");
        var manifestPath = Path.Combine(temp.Root, "modules", "artifacts", "stores", "builtin", "store.toml");
        File.WriteAllText(configPath, "model = \"gpt-test\"\n");
        WriteManifest(manifestPath, "builtin", "local-filesystem");

        var configuration = new TianShuArtifactStoreManifestConfiguration();
        var package = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        package.Version = "1.0.1";
        package.MinTianShuVersion = "0.1.0";
        package.Capabilities = ["artifact:local"];
        package.Diagnostics = ["artifact:sync"];
        package.Stores =
        [
            .. package.Stores,
            new ArtifactStoreManifestValue
            {
                Id = "config-gui-smoke",
                DisplayName = "ConfigGUI Smoke Store",
                Enabled = true,
                Type = "filesystem",
                Root = "./smoke-data",
                MaxHistoryVersions = 7,
                EnableCrossProcessSync = true,
                Priority = 99,
            },
        ];

        configuration.SavePackage(manifestPath, package);

        var saved = File.ReadAllText(manifestPath);
        Assert.Contains("id = \"config-gui-smoke\"", saved, StringComparison.Ordinal);
        Assert.Contains("version = \"1.0.1\"", saved, StringComparison.Ordinal);
        Assert.Contains("min_tianshu_version = \"0.1.0\"", saved, StringComparison.Ordinal);
        Assert.Contains("capabilities = [\"artifact:local\"]", saved, StringComparison.Ordinal);
        Assert.Contains("diagnostics = [\"artifact:sync\"]", saved, StringComparison.Ordinal);
        Assert.Contains("display_name = \"ConfigGUI Smoke Store\"", saved, StringComparison.Ordinal);
        Assert.Contains("root = \"./smoke-data\"", saved, StringComparison.Ordinal);
        Assert.Contains("max_history_versions = 7", saved, StringComparison.Ordinal);
        Assert.Contains("enable_cross_process_sync = true", saved, StringComparison.Ordinal);
        Assert.Equal("model = \"gpt-test\"\n", File.ReadAllText(configPath));

        var reloaded = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        Assert.Equal("1.0.1", reloaded.Version);
        Assert.Equal("0.1.0", reloaded.MinTianShuVersion);
        Assert.Equal(["artifact:local"], reloaded.Capabilities);
        Assert.Equal(["artifact:sync"], reloaded.Diagnostics);
    }

    [Fact]
    public void Load_WhenVersionIncompatible_ReportsUnavailableIssue()
    {
        using var temp = TempTianShuHome.Create();
        var manifestPath = Path.Combine(temp.Root, "modules", "artifacts", "stores", "future", "store.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(
            manifestPath,
            """
            id = "future"
            display_name = "future"
            enabled = true
            type = "package"
            priority = 0
            min_tianshu_version = "99.0.0"

            [[stores]]
            id = "future-store"
            enabled = true
            type = "filesystem"
            root = "./data"
            """);

        var projection = new TianShuArtifactStoreManifestConfiguration().Load(temp.Root, manifestPath);

        Assert.Equal("future", projection.SelectedPackage?.Id);
        Assert.Equal("unavailable", projection.SelectedPackage!.LoadStatus);
        Assert.Contains(projection.Issues, static issue => issue.Code == "artifact_store_manifest.version_incompatible");
    }

    [Fact]
    public void CreateCopyAndDeletePackage_OnlyWritesArtifactStoresDirectory()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuArtifactStoreManifestConfiguration();

        var createdPath = configuration.CreatePackage(temp.Root, "company-artifacts");
        Assert.Equal(Path.Combine(temp.Root, "modules", "artifacts", "stores", "company-artifacts", "store.toml"), createdPath);
        Assert.True(File.Exists(createdPath));

        var copiedPath = configuration.CopyPackage(temp.Root, createdPath, "company-artifacts-copy");
        Assert.Equal(Path.Combine(temp.Root, "modules", "artifacts", "stores", "company-artifacts-copy", "store.toml"), copiedPath);
        Assert.True(File.Exists(copiedPath));

        configuration.DeletePackage(temp.Root, copiedPath);
        Assert.False(File.Exists(copiedPath));
    }

    [Fact]
    public void ResolveStoreRootFullPath_ResolvesRelativeToManifestDirectory()
    {
        using var temp = TempTianShuHome.Create();
        var manifestPath = Path.Combine(temp.Root, "modules", "artifacts", "stores", "builtin", "store.toml");
        WriteManifest(manifestPath, "builtin", "local-filesystem");

        var package = new TianShuArtifactStoreManifestConfiguration().Load(temp.Root, manifestPath).SelectedPackage!;
        var store = Assert.Single(package.Stores);

        Assert.Equal(
            Path.Combine(temp.Root, "modules", "artifacts", "stores", "builtin", "data"),
            TianShuArtifactStoreManifestConfiguration.ResolveStoreRootFullPath(package, store));
    }

    [Fact]
    public void CreatePackage_RejectsPathsOutsideArtifactStoresRoot()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuArtifactStoreManifestConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "..\\outside"));
        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "nested\\store"));
    }

    private static void WriteManifest(string path, string packageId, string storeId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var packageType = string.Equals(packageId, "builtin", StringComparison.OrdinalIgnoreCase) ? "builtin" : "package";
        File.WriteAllText(
            path,
            $$"""
            id = "{{packageId}}"
            display_name = "{{packageId}}"
            enabled = true
            type = "{{packageType}}"
            priority = 0

            [[stores]]
            id = "{{storeId}}"
            display_name = "{{storeId}}"
            enabled = true
            type = "filesystem"
            root = "./data"
            max_history_versions = 20
            enable_cross_process_sync = true
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
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-artifact-store-manifest-config-{Guid.NewGuid():N}");
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

