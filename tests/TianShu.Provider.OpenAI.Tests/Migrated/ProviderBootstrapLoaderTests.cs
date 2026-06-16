using System.Reflection;
using TianShu.Provider.Abstractions;
using TianShu.Provider.BootstrapLoaderFixtures;
using TianShu.RuntimeComposition;

namespace TianShu.Provider.OpenAI.Tests;

public sealed class ProviderBootstrapLoaderTests
{
    [Fact]
    public void LoadBootstraps_WhenAssemblySelfDeclaresBootstrap_LoadsFromAlreadyLoadedAssemblies()
    {
        var bootstraps = ProviderBootstrapLoader.LoadBootstraps<ILoadedFixtureBootstrap>(
            static bootstrap => bootstrap.Key,
            static key => $"duplicate:{key}",
            "empty");

        var bootstrap = Assert.Single(bootstraps);
        Assert.Equal("loaded-bootstrap", bootstrap.Key);
        Assert.Equal("TianShu.Provider.BootstrapLoaderFixtures", bootstrap.Value.GetType().Assembly.GetName().Name);
    }

    [Fact]
    public void LoadBootstraps_WhenDuplicateKeysExist_ThrowsCanonicalMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProviderBootstrapLoader.LoadBootstraps<IDuplicateFixtureBootstrap>(
                static bootstrap => bootstrap.Key,
                static key => $"duplicate:{key}",
                "empty"));

        Assert.Equal("duplicate:same-key", exception.Message);
    }

    [Fact]
    public void LoadBootstraps_WhenNoMatchingBootstrapExists_ThrowsEmptyMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProviderBootstrapLoader.LoadBootstraps<IUnregisteredBootstrap>(
                static bootstrap => bootstrap.Key,
                static key => $"duplicate:{key}",
                "empty-message"));

        Assert.Equal("empty-message", exception.Message);
    }

    [Fact]
    public void LoadProviderAssembliesFromLoadedDirectories_WhenProviderDllExists_AddsCandidateAssembly()
    {
        var method = typeof(ProviderBootstrapLoader).GetMethod(
            "LoadProviderAssembliesFromLoadedDirectories",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        Dictionary<string, Assembly> assemblies = new(StringComparer.OrdinalIgnoreCase);
        Queue<AssemblyName> pendingReferences = [];
        HashSet<string> queuedReferences = new(StringComparer.OrdinalIgnoreCase);

        method.Invoke(null, [assemblies, pendingReferences, queuedReferences]);

        Assert.True(assemblies.ContainsKey("TianShu.Provider.OpenAI"));
        Assert.Equal("TianShu.Provider.OpenAI", assemblies["TianShu.Provider.OpenAI"].GetName().Name);
    }

    [Fact]
    public void ProviderPackageAssemblyPreloader_WhenManifestDeclaresAssembly_LoadsEnabledAssembly()
    {
        var home = CreateTemporaryHome();
        var manifestDirectory = Path.Combine(home, "modules", "model", "provider-adapters", "fixture");
        Directory.CreateDirectory(manifestDirectory);
        var fixtureAssemblyPath = typeof(LoadedFixtureBootstrap).Assembly.Location.Replace("\\", "\\\\", StringComparison.Ordinal);
        File.WriteAllText(
            Path.Combine(manifestDirectory, ProviderPackageAssemblyPreloader.ManifestFileName),
            $$"""
            id = "fixture"
            enabled = true
            type = "assembly"

            [[adapters]]
            id = "loaded-bootstrap"
            enabled = true
            type = "assembly"
            assembly_path = "{{fixtureAssemblyPath}}"
            """);

        var result = ProviderPackageAssemblyPreloader.TryLoadProviderPackages(home);

        var loaded = Assert.Single(result.LoadedAssemblies);
        Assert.Equal(Path.GetFullPath(typeof(LoadedFixtureBootstrap).Assembly.Location), loaded);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ProviderPackageAssemblyPreloader_WhenAdapterDisabled_SkipsAssembly()
    {
        var home = CreateTemporaryHome();
        var manifestDirectory = Path.Combine(home, "modules", "model", "provider-adapters", "fixture");
        Directory.CreateDirectory(manifestDirectory);
        File.WriteAllText(
            Path.Combine(manifestDirectory, ProviderPackageAssemblyPreloader.ManifestFileName),
            """
            id = "fixture"
            enabled = true
            type = "assembly"

            [[adapters]]
            id = "loaded-bootstrap"
            enabled = false
            type = "assembly"
            assembly_path = "./missing.dll"
            """);

        var result = ProviderPackageAssemblyPreloader.TryLoadProviderPackages(home);

        Assert.Empty(result.LoadedAssemblies);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ProviderPackageAssemblyPreloader_WhenVersionIncompatible_SkipsPackageAndReportsIssue()
    {
        var home = CreateTemporaryHome();
        var manifestDirectory = Path.Combine(home, "modules", "model", "provider-adapters", "fixture");
        Directory.CreateDirectory(manifestDirectory);
        var fixtureAssemblyPath = typeof(LoadedFixtureBootstrap).Assembly.Location.Replace("\\", "\\\\", StringComparison.Ordinal);
        File.WriteAllText(
            Path.Combine(manifestDirectory, ProviderPackageAssemblyPreloader.ManifestFileName),
            $$"""
            id = "fixture"
            enabled = true
            type = "assembly"
            min_tianshu_version = "99.0.0"

            [[adapters]]
            id = "loaded-bootstrap"
            enabled = true
            type = "assembly"
            assembly_path = "{{fixtureAssemblyPath}}"
            """);

        var result = ProviderPackageAssemblyPreloader.TryLoadProviderPackages(home);

        Assert.Empty(result.LoadedAssemblies);
        var issue = Assert.Single(result.Issues);
        Assert.Null(issue.AdapterId);
        Assert.Equal("version_incompatible", issue.Code);
    }

    [Fact]
    public void ProviderPackageAssemblyPreloader_WhenAssemblyMissing_ReportsIssue()
    {
        var home = CreateTemporaryHome();
        var manifestDirectory = Path.Combine(home, "modules", "model", "provider-adapters", "fixture");
        Directory.CreateDirectory(manifestDirectory);
        File.WriteAllText(
            Path.Combine(manifestDirectory, ProviderPackageAssemblyPreloader.ManifestFileName),
            """
            id = "fixture"
            enabled = true
            type = "assembly"

            [[adapters]]
            id = "missing-adapter"
            enabled = true
            type = "assembly"
            assembly_path = "./missing.dll"
            """);

        var result = ProviderPackageAssemblyPreloader.TryLoadProviderPackages(home);

        Assert.Empty(result.LoadedAssemblies);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("missing-adapter", issue.AdapterId);
        Assert.Equal("assembly_not_found", issue.Code);
    }

    [Fact]
    public void ProviderRuntimeComposition_ReloadProviderPackages_ReturnsControlPlaneProjection()
    {
        var home = CreateTemporaryHome();

        var result = ProviderRuntimeComposition.ReloadProviderPackages(home);

        Assert.Equal(0, result.LoadedAssemblyCount);
        Assert.Equal(0, result.IssueCount);
        Assert.Empty(result.Issues);
        Assert.NotEmpty(result.SupportedProtocolAdapterIds);
        Assert.NotEmpty(result.SupportedWireApis);
    }

    private static string CreateTemporaryHome()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-provider-preload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public interface IUnregisteredBootstrap
    {
        string Key { get; }
    }
}
