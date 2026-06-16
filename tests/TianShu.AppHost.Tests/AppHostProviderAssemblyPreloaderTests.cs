using System.Runtime.Loader;
using TianShu.AppHost;

namespace TianShu.AppHost.Tests;

public sealed class AppHostProviderAssemblyPreloaderTests
{
    [Fact]
    public void TryLoadPackagedProviders_LoadsBuiltInProviderAssemblies()
    {
        AppHostProviderAssemblyPreloader.TryLoadPackagedProviders();

        var loadedProviderAssemblies = AssemblyLoadContext.All
            .SelectMany(static context => context.Assemblies)
            .Select(static assembly => assembly.GetName().Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("TianShu.Provider.OpenAICompatible", loadedProviderAssemblies);
        Assert.Contains("TianShu.Provider.OpenAI", loadedProviderAssemblies);
    }
}
