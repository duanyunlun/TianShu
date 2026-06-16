namespace TianShu.Configuration.Tests;

public sealed class TianShuToolManifestConfigurationTests
{
    [Fact]
    public void Load_ScansModuleToolPackageManifests()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "tools", "packages", "builtin", "tool.toml"), "builtin", "search");
        WriteManifest(Path.Combine(temp.Root, "modules", "tools", "packages", "company", "tool.toml"), "company", "company_provider");

        var projection = new TianShuToolManifestConfiguration().Load(temp.Root);

        Assert.Equal(2, projection.Files.Count);
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("builtin", "tool.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("company", "tool.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("builtin", projection.SelectedPackage?.Id);
        Assert.Equal("search", Assert.Single(projection.SelectedPackage!.Providers).Id);
    }

    [Fact]
    public void SavePackage_UpdatesProviderManifestWithoutTouchingTianShuToml()
    {
        using var temp = TempTianShuHome.Create();
        var configPath = Path.Combine(temp.Root, "tianshu.toml");
        var manifestPath = Path.Combine(temp.Root, "modules", "tools", "packages", "builtin", "tool.toml");
        File.WriteAllText(configPath, "model = \"gpt-test\"\n");
        WriteManifest(manifestPath, "builtin", "search");

        var configuration = new TianShuToolManifestConfiguration();
        var package = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        package.Version = "2.0.0";
        package.MinTianShuVersion = "0.1.0";
        package.Capabilities = ["tool:shell", "tool:memory"];
        package.Diagnostics = ["tool:catalog"];
        package.Providers =
        [
            .. package.Providers,
            new ToolProviderManifestValue
            {
                Id = "config_gui_smoke",
                Enabled = true,
                Type = "assembly",
                AssemblyPath = "./smoke/TianShu.Tools.Smoke.dll",
                ProviderType = "TianShu.Tools.Smoke.SmokeToolProvider",
                Priority = 99,
                ReplaceExisting = true,
            },
        ];

        configuration.SavePackage(manifestPath, package);

        var saved = File.ReadAllText(manifestPath);
        Assert.Contains("id = \"config_gui_smoke\"", saved, StringComparison.Ordinal);
        Assert.Contains("version = \"2.0.0\"", saved, StringComparison.Ordinal);
        Assert.Contains("min_tianshu_version = \"0.1.0\"", saved, StringComparison.Ordinal);
        Assert.Contains("capabilities = [\"tool:shell\", \"tool:memory\"]", saved, StringComparison.Ordinal);
        Assert.Contains("diagnostics = [\"tool:catalog\"]", saved, StringComparison.Ordinal);
        Assert.Contains("assembly_path = \"./smoke/TianShu.Tools.Smoke.dll\"", saved, StringComparison.Ordinal);
        Assert.Contains("provider_type = \"TianShu.Tools.Smoke.SmokeToolProvider\"", saved, StringComparison.Ordinal);
        Assert.Equal("model = \"gpt-test\"\n", File.ReadAllText(configPath));

        var reloaded = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        Assert.Equal("2.0.0", reloaded.Version);
        Assert.Equal("0.1.0", reloaded.MinTianShuVersion);
        Assert.Equal(["tool:shell", "tool:memory"], reloaded.Capabilities);
        Assert.Equal(["tool:catalog"], reloaded.Diagnostics);
    }

    [Fact]
    public void Load_WhenVersionIncompatible_ReportsUnavailableIssue()
    {
        using var temp = TempTianShuHome.Create();
        var manifestPath = Path.Combine(temp.Root, "modules", "tools", "packages", "future", "tool.toml");
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

            [[providers]]
            id = "future_tool"
            enabled = true
            type = "assembly"
            assembly_path = "./future/Tool.dll"
            provider_type = "Example.FutureToolProvider"
            """);

        var projection = new TianShuToolManifestConfiguration().Load(temp.Root, manifestPath);

        Assert.Equal("future", projection.SelectedPackage?.Id);
        Assert.True(projection.SelectedPackage!.Enabled);
        Assert.Equal("unavailable", projection.SelectedPackage.LoadStatus);
        Assert.Contains(projection.Issues, static issue => issue.Code == "tool_manifest.version_incompatible");
    }

    [Fact]
    public void CreateCopyAndDeletePackage_OnlyWritesModuleToolsDirectory()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuToolManifestConfiguration();

        var createdPath = configuration.CreatePackage(temp.Root, "company-tools");
        Assert.Equal(Path.Combine(temp.Root, "modules", "tools", "packages", "company-tools", "tool.toml"), createdPath);
        Assert.True(File.Exists(createdPath));

        var copiedPath = configuration.CopyPackage(temp.Root, createdPath, "company-tools-copy");
        Assert.Equal(Path.Combine(temp.Root, "modules", "tools", "packages", "company-tools-copy", "tool.toml"), copiedPath);
        Assert.True(File.Exists(copiedPath));

        configuration.DeletePackage(temp.Root, copiedPath);
        Assert.False(File.Exists(copiedPath));
    }

    [Fact]
    public void CreatePackage_RejectsPathsOutsideToolsRoot()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuToolManifestConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "..\\outside"));
        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "nested\\tool"));
    }

    private static void WriteManifest(string path, string packageId, string providerId)
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

            [[providers]]
            id = "{{providerId}}"
            enabled = true
            type = "assembly"
            assembly_path = "./{{providerId}}/Tool.dll"
            provider_type = "Example.{{providerId}}"
            priority = 10
            replace_existing = true
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
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-tool-manifest-config-{Guid.NewGuid():N}");
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
