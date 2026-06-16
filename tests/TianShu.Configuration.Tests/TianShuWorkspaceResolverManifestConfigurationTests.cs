namespace TianShu.Configuration.Tests;

public sealed class TianShuWorkspaceResolverManifestConfigurationTests
{
    [Fact]
    public void Load_ScansWorkspaceResolverPackageManifests()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "workspace", "resolvers", "builtin", "resolver.toml"), "builtin", "default", [".git"]);
        WriteManifest(Path.Combine(temp.Root, "modules", "workspace", "resolvers", "company", "resolver.toml"), "company", "dotnet", ["*.sln"]);

        var projection = new TianShuWorkspaceResolverManifestConfiguration().Load(temp.Root);

        Assert.Equal(2, projection.Files.Count);
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("builtin", "resolver.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("company", "resolver.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("builtin", projection.SelectedPackage?.Id);
        Assert.Equal("default", Assert.Single(projection.SelectedPackage!.Resolvers).Id);
    }

    [Fact]
    public void SavePackage_UpdatesResolverManifestWithoutTouchingTianShuToml()
    {
        using var temp = TempTianShuHome.Create();
        var configPath = Path.Combine(temp.Root, "tianshu.toml");
        var manifestPath = Path.Combine(temp.Root, "modules", "workspace", "resolvers", "builtin", "resolver.toml");
        File.WriteAllText(configPath, "model = \"gpt-test\"\n");
        WriteManifest(manifestPath, "builtin", "default", [".git"]);

        var configuration = new TianShuWorkspaceResolverManifestConfiguration();
        var package = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        package.Version = "1.3.0";
        package.MinTianShuVersion = "0.1.0";
        package.Capabilities = ["workspace:markers"];
        package.Diagnostics = ["workspace:load"];
        package.Resolvers =
        [
            .. package.Resolvers,
            new WorkspaceResolverManifestValue
            {
                Id = "dotnet",
                DisplayName = "Dotnet Workspace Resolver",
                Enabled = true,
                Type = "marker",
                RootMarkers = ["*.sln", "Directory.Build.props"],
                Profile = "default",
                TrustPolicy = "prompt",
                ArtifactRoot = ".tianshu/artifacts",
                StateRoot = ".tianshu/state",
                IgnoreGlobs = ["bin/**", "obj/**"],
                LanguageMarkers = ["*.csproj"],
                FrameworkMarkers = ["*.sln"],
                Priority = 10,
            },
        ];

        configuration.SavePackage(manifestPath, package);

        var saved = File.ReadAllText(manifestPath);
        Assert.Contains("id = \"dotnet\"", saved, StringComparison.Ordinal);
        Assert.Contains("version = \"1.3.0\"", saved, StringComparison.Ordinal);
        Assert.Contains("min_tianshu_version = \"0.1.0\"", saved, StringComparison.Ordinal);
        Assert.Contains("capabilities = [\"workspace:markers\"]", saved, StringComparison.Ordinal);
        Assert.Contains("diagnostics = [\"workspace:load\"]", saved, StringComparison.Ordinal);
        Assert.Contains("display_name = \"Dotnet Workspace Resolver\"", saved, StringComparison.Ordinal);
        Assert.Contains("root_markers = [\"*.sln\", \"Directory.Build.props\"]", saved, StringComparison.Ordinal);
        Assert.Contains("ignore_globs = [\"bin/**\", \"obj/**\"]", saved, StringComparison.Ordinal);
        Assert.Equal("model = \"gpt-test\"\n", File.ReadAllText(configPath));

        var reloaded = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        Assert.Equal("1.3.0", reloaded.Version);
        Assert.Equal("0.1.0", reloaded.MinTianShuVersion);
        Assert.Equal(["workspace:markers"], reloaded.Capabilities);
        Assert.Equal(["workspace:load"], reloaded.Diagnostics);
    }

    [Fact]
    public void ResolveEffectiveRootMarkers_MergesEnabledResolverMarkers()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "workspace", "resolvers", "builtin", "resolver.toml"), "builtin", "default", [".git", ".tianshu"]);
        WriteManifest(Path.Combine(temp.Root, "modules", "workspace", "resolvers", "company", "resolver.toml"), "company", "dotnet", ["*.sln"]);

        var markers = TianShuWorkspaceResolverManifestConfiguration.ResolveEffectiveRootMarkers(temp.Root, [".project-root"]);

        Assert.Equal([".project-root", ".git", ".tianshu", "*.sln"], markers);
    }

    [Fact]
    public void ResolveEffectivePolicy_MergesAdvancedResolverStrategies()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "workspace", "resolvers", "builtin", "resolver.toml"), "builtin", "default", [".git", ".tianshu"]);
        WriteManifest(Path.Combine(temp.Root, "modules", "workspace", "resolvers", "company", "resolver.toml"), "company", "dotnet", ["*.sln"]);
        var disabledManifestPath = Path.Combine(temp.Root, "modules", "workspace", "resolvers", "disabled", "resolver.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(disabledManifestPath)!);
        File.WriteAllText(
            disabledManifestPath,
            """
            id = "disabled"
            enabled = false
            type = "package"

            [[resolvers]]
            id = "ignored"
            enabled = true
            root_markers = [".disabled"]
            ignore_globs = ["ignored/**"]
            """);

        var policy = TianShuWorkspaceResolverManifestConfiguration.ResolveEffectivePolicy(temp.Root, [".project-root"]);

        Assert.Equal([".project-root", ".git", ".tianshu", "*.sln"], policy.RootMarkers);
        Assert.Equal("default", policy.DefaultProfile);
        Assert.Equal("prompt", policy.TrustPolicy);
        Assert.Equal(
            Path.Combine(temp.Root, "modules", "workspace", "resolvers", "builtin", ".tianshu", "artifacts"),
            policy.ArtifactRoot);
        Assert.Equal(
            Path.Combine(temp.Root, "modules", "workspace", "resolvers", "builtin", ".tianshu", "state"),
            policy.StateRoot);
        Assert.Equal(["bin/**", "obj/**"], policy.IgnoreGlobs);
        Assert.Equal(["*.csproj"], policy.LanguageMarkers);
        Assert.Equal(["*.sln"], policy.FrameworkMarkers);
        Assert.Equal(["builtin", "company"], policy.Resolvers.Select(static resolver => resolver.PackageId));
    }

    [Fact]
    public void LoadEnabledPackages_WhenVersionIncompatible_SkipsPackage()
    {
        using var temp = TempTianShuHome.Create();
        var manifestPath = Path.Combine(temp.Root, "modules", "workspace", "resolvers", "future", "resolver.toml");
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

            [[resolvers]]
            id = "future"
            enabled = true
            type = "marker"
            root_markers = [".future"]
            """);

        var configuration = new TianShuWorkspaceResolverManifestConfiguration();
        var projection = configuration.Load(temp.Root, manifestPath);
        var markers = TianShuWorkspaceResolverManifestConfiguration.ResolveEffectiveRootMarkers(temp.Root, [".project-root"]);

        Assert.Equal("unavailable", projection.SelectedPackage!.LoadStatus);
        Assert.Contains(projection.Issues, static issue => issue.Code == "workspace_resolver_manifest.version_incompatible");
        Assert.Equal([".project-root"], markers);
    }

    [Fact]
    public void CreateCopyAndDeletePackage_OnlyWritesWorkspaceResolversDirectory()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuWorkspaceResolverManifestConfiguration();

        var createdPath = configuration.CreatePackage(temp.Root, "company-workspace");
        Assert.Equal(Path.Combine(temp.Root, "modules", "workspace", "resolvers", "company-workspace", "resolver.toml"), createdPath);
        Assert.True(File.Exists(createdPath));

        var copiedPath = configuration.CopyPackage(temp.Root, createdPath, "company-workspace-copy");
        Assert.Equal(Path.Combine(temp.Root, "modules", "workspace", "resolvers", "company-workspace-copy", "resolver.toml"), copiedPath);
        Assert.True(File.Exists(copiedPath));

        configuration.DeletePackage(temp.Root, copiedPath);
        Assert.False(File.Exists(copiedPath));
    }

    [Fact]
    public void ResolveArtifactAndStateRoots_ResolveRelativeToManifestDirectory()
    {
        using var temp = TempTianShuHome.Create();
        var manifestPath = Path.Combine(temp.Root, "modules", "workspace", "resolvers", "builtin", "resolver.toml");
        WriteManifest(manifestPath, "builtin", "default", [".git"]);

        var package = new TianShuWorkspaceResolverManifestConfiguration().Load(temp.Root, manifestPath).SelectedPackage!;
        var resolver = Assert.Single(package.Resolvers);

        Assert.Equal(
            Path.Combine(temp.Root, "modules", "workspace", "resolvers", "builtin", ".tianshu", "artifacts"),
            TianShuWorkspaceResolverManifestConfiguration.ResolveArtifactRootFullPath(package, resolver));
        Assert.Equal(
            Path.Combine(temp.Root, "modules", "workspace", "resolvers", "builtin", ".tianshu", "state"),
            TianShuWorkspaceResolverManifestConfiguration.ResolveStateRootFullPath(package, resolver));
    }

    [Fact]
    public void CreatePackage_RejectsPathsOutsideWorkspaceResolverRoot()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuWorkspaceResolverManifestConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "..\\outside"));
        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "nested\\resolver"));
    }

    private static void WriteManifest(string path, string packageId, string resolverId, IReadOnlyList<string> rootMarkers)
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

            [[resolvers]]
            id = "{{resolverId}}"
            display_name = "{{resolverId}}"
            enabled = true
            type = "marker"
            priority = 0
            root_markers = [{{string.Join(", ", rootMarkers.Select(static marker => $"\"{marker}\""))}}]
            profile = "default"
            trust_policy = "prompt"
            artifact_root = ".tianshu/artifacts"
            state_root = ".tianshu/state"
            ignore_globs = ["bin/**", "obj/**"]
            language_markers = ["*.csproj"]
            framework_markers = ["*.sln"]
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
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-workspace-resolver-manifest-config-{Guid.NewGuid():N}");
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

