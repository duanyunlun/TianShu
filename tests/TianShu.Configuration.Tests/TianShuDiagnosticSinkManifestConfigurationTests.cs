namespace TianShu.Configuration.Tests;

public sealed class TianShuDiagnosticSinkManifestConfigurationTests
{
    [Fact]
    public void Load_ScansDiagnosticSinkPackageManifests()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "diagnostics", "sinks", "builtin", "sink.toml"), "builtin", "turn-log");
        WriteManifest(Path.Combine(temp.Root, "modules", "diagnostics", "sinks", "company", "sink.toml"), "company", "company-sink");

        var projection = new TianShuDiagnosticSinkManifestConfiguration().Load(temp.Root);

        Assert.Equal(2, projection.Files.Count);
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("builtin", "sink.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("company", "sink.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("builtin", projection.SelectedPackage?.Id);
        Assert.Equal("turn-log", Assert.Single(projection.SelectedPackage!.Sinks).Id);
    }

    [Fact]
    public void SavePackage_UpdatesSinkManifestWithoutTouchingTianShuToml()
    {
        using var temp = TempTianShuHome.Create();
        var configPath = Path.Combine(temp.Root, "tianshu.toml");
        var manifestPath = Path.Combine(temp.Root, "modules", "diagnostics", "sinks", "builtin", "sink.toml");
        File.WriteAllText(configPath, "model = \"gpt-test\"\n");
        WriteManifest(manifestPath, "builtin", "turn-log");

        var configuration = new TianShuDiagnosticSinkManifestConfiguration();
        var package = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        package.Version = "1.1.0";
        package.MinTianShuVersion = "0.1.0";
        package.Capabilities = ["diagnostic:artifact"];
        package.Diagnostics = ["diagnostic:load"];
        package.Sinks =
        [
            .. package.Sinks,
            new DiagnosticSinkManifestValue
            {
                Id = "config-gui-smoke",
                DisplayName = "ConfigGUI Smoke Sink",
                Enabled = true,
                Type = "artifact-file",
                Target = "./smoke-artifacts",
                Level = "artifact",
                Modules = ["provider", "runtime"],
                MaxBytes = 4096,
                Priority = 99,
            },
        ];

        configuration.SavePackage(manifestPath, package);

        var saved = File.ReadAllText(manifestPath);
        Assert.Contains("id = \"config-gui-smoke\"", saved, StringComparison.Ordinal);
        Assert.Contains("version = \"1.1.0\"", saved, StringComparison.Ordinal);
        Assert.Contains("min_tianshu_version = \"0.1.0\"", saved, StringComparison.Ordinal);
        Assert.Contains("capabilities = [\"diagnostic:artifact\"]", saved, StringComparison.Ordinal);
        Assert.Contains("diagnostics = [\"diagnostic:load\"]", saved, StringComparison.Ordinal);
        Assert.Contains("display_name = \"ConfigGUI Smoke Sink\"", saved, StringComparison.Ordinal);
        Assert.Contains("type = \"artifact-file\"", saved, StringComparison.Ordinal);
        Assert.Contains("target = \"./smoke-artifacts\"", saved, StringComparison.Ordinal);
        Assert.Contains("level = \"artifact\"", saved, StringComparison.Ordinal);
        Assert.Contains("modules = [\"provider\", \"runtime\"]", saved, StringComparison.Ordinal);
        Assert.Contains("max_bytes = 4096", saved, StringComparison.Ordinal);
        Assert.Equal("model = \"gpt-test\"\n", File.ReadAllText(configPath));

        var reloaded = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        Assert.Equal("1.1.0", reloaded.Version);
        Assert.Equal("0.1.0", reloaded.MinTianShuVersion);
        Assert.Equal(["diagnostic:artifact"], reloaded.Capabilities);
        Assert.Equal(["diagnostic:load"], reloaded.Diagnostics);
    }

    [Fact]
    public void LoadEnabledPackages_WhenVersionIncompatible_SkipsPackage()
    {
        using var temp = TempTianShuHome.Create();
        var manifestPath = Path.Combine(temp.Root, "modules", "diagnostics", "sinks", "future", "sink.toml");
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

            [[sinks]]
            id = "future-sink"
            enabled = true
            type = "turn-log"
            """);

        var configuration = new TianShuDiagnosticSinkManifestConfiguration();
        var projection = configuration.Load(temp.Root, manifestPath);
        var packages = configuration.LoadEnabledPackages(temp.Root);

        Assert.Equal("unavailable", projection.SelectedPackage!.LoadStatus);
        Assert.Contains(projection.Issues, static issue => issue.Code == "diagnostic_sink_manifest.version_incompatible");
        Assert.Empty(packages);
    }

    [Fact]
    public void CreateCopyAndDeletePackage_OnlyWritesDiagnosticSinksDirectory()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuDiagnosticSinkManifestConfiguration();

        var createdPath = configuration.CreatePackage(temp.Root, "company-diagnostics");
        Assert.Equal(Path.Combine(temp.Root, "modules", "diagnostics", "sinks", "company-diagnostics", "sink.toml"), createdPath);
        Assert.True(File.Exists(createdPath));

        var copiedPath = configuration.CopyPackage(temp.Root, createdPath, "company-diagnostics-copy");
        Assert.Equal(Path.Combine(temp.Root, "modules", "diagnostics", "sinks", "company-diagnostics-copy", "sink.toml"), copiedPath);
        Assert.True(File.Exists(copiedPath));

        configuration.DeletePackage(temp.Root, copiedPath);
        Assert.False(File.Exists(copiedPath));
    }

    [Fact]
    public void ResolveSinkTargetFullPath_ResolvesRelativeToManifestDirectory()
    {
        using var temp = TempTianShuHome.Create();
        var manifestPath = Path.Combine(temp.Root, "modules", "diagnostics", "sinks", "builtin", "sink.toml");
        WriteManifest(manifestPath, "builtin", "provider-request-artifacts");

        var package = new TianShuDiagnosticSinkManifestConfiguration().Load(temp.Root, manifestPath).SelectedPackage!;
        var sink = Assert.Single(package.Sinks);

        Assert.Equal(
            Path.Combine(temp.Root, "modules", "diagnostics", "sinks", "builtin", "artifacts", "provider-requests"),
            TianShuDiagnosticSinkManifestConfiguration.ResolveSinkTargetFullPath(package, sink));
    }

    [Fact]
    public void CreatePackage_RejectsPathsOutsideDiagnosticSinkRoot()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuDiagnosticSinkManifestConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "..\\outside"));
        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "nested\\sink"));
    }

    private static void WriteManifest(string path, string packageId, string sinkId)
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

            [[sinks]]
            id = "{{sinkId}}"
            display_name = "{{sinkId}}"
            enabled = true
            type = "artifact-file"
            target = "./artifacts/provider-requests"
            level = "artifact"
            modules = ["provider"]
            max_bytes = 1048576
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
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-diagnostic-sink-manifest-config-{Guid.NewGuid():N}");
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

