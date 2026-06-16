using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelArtifactsRuntimeHelpersTests
{
    [Fact]
    public void ResolveConfiguredArtifactEnabled_ShouldReadFeaturesFlag()
    {
        var enabled = KernelArtifactsRuntimeHelpers.ResolveConfiguredArtifactEnabled(
            """
            [features]
            artifact = true
            """);

        Assert.True(enabled);
    }

    [Fact]
    public void ResolveArtifactsRuntimeOptions_ShouldFallbackToPinnedRuntimeVersion()
    {
        var options = KernelArtifactsRuntimeHelpers.ResolveArtifactsRuntimeOptions(
            tianShuHome: "d:\\tianshu-home",
            runtimeVersion: null,
            cacheRoot: "d:\\cache",
            preferredNodePath: "node");

        Assert.Equal("d:\\tianshu-home", options.TianShuHome);
        Assert.Equal(KernelArtifactsRuntimeManager.PinnedRuntimeVersion, options.RuntimeVersion);
        Assert.Equal("d:\\cache", options.CacheRoot);
        Assert.Equal("node", options.PreferredNodePath);
    }
}
