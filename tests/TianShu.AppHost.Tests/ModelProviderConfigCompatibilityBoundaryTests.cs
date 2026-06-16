namespace TianShu.AppHost.Tests;

public sealed class ModelProviderConfigCompatibilityBoundaryTests
{
    [Fact]
    public void ProviderConfigReaders_ShouldNotReferenceLegacyProviderKeys()
    {
        var repoRoot = FindRepoRoot();
        var catalogSurfaceSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Catalog",
            "KernelCatalogSurfaceAppHostRuntime.cs"));
        var modelStatusSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "ModelStatus",
            "ModelStatusCommandHandler.cs"));
        var providerUtilitiesSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelModelProviderConfigUtilities.cs"));
        var publicReaderSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "TianShuModelProviderConfigReader.cs"));

        var legacyKeys = new[]
        {
            "model_" + "provider",
            "model_" + "providers",
            "env" + "_" + "key",
            "wire" + "_" + "api",
            "provider" + "_" + "wire" + "_" + "api",
        };

        foreach (var source in new[] { catalogSurfaceSource, modelStatusSource, providerUtilitiesSource, publicReaderSource })
        {
            foreach (var legacyKey in legacyKeys)
            {
                Assert.DoesNotContain(legacyKey, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ModelProviderConfigUtilities_ShouldNotReadCompatibilityConfigDirectly()
    {
        var providerUtilitiesSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelModelProviderConfigUtilities.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", providerUtilitiesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", providerUtilitiesSource, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
