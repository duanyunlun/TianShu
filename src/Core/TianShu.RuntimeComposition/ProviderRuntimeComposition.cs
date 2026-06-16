using TianShu.Contracts.Catalog;
using TianShu.Configuration;
using TianShu.Provider.Abstractions;

namespace TianShu.RuntimeComposition;

/// <summary>
/// Provider 能力包运行时组合入口。
/// Runtime composition entry point for provider capability packages.
/// </summary>
public static class ProviderRuntimeComposition
{
    /// <summary>
    /// 预加载用户级 Provider 包中启用的程序集。
    /// Preloads enabled assemblies declared by user-level provider packages.
    /// </summary>
    /// <param name="homeDirectory">TianShu home；为空时按环境推导。TianShu home; when blank, derives it from environment.</param>
    /// <returns>程序集预加载结果。Assembly preload result.</returns>
    public static ProviderPackageAssemblyPreloadResult PreloadProviderPackages(string? homeDirectory = null)
    {
        var home = string.IsNullOrWhiteSpace(homeDirectory)
            ? TianShuHomePathUtilities.ResolveTianShuHomePath()
            : Path.GetFullPath(homeDirectory);
        var providerRoot = TianShuProviderManifestConfiguration.ResolveProviderRootDirectory(home);
        return ProviderPackageAssemblyPreloader.TryLoadProviderPackagesFromRoot(home, providerRoot);
    }

    /// <summary>
    /// 重新加载 Provider 包并重建 Provider runtime registry。
    /// Reloads provider packages and rebuilds provider runtime registries.
    /// </summary>
    /// <param name="homeDirectory">TianShu home；为空时按环境推导。TianShu home; when blank, derives it from environment.</param>
    /// <returns>控制平面可投影的 reload 结果。Control-plane reload projection.</returns>
    public static ControlPlaneProviderPackageReloadResult ReloadProviderPackages(string? homeDirectory = null)
    {
        var preloadResult = PreloadProviderPackages(homeDirectory);
        var supportedProtocolAdapterIds = ProviderRuntimeBootstrapRegistry.Reload();
        var supportedWireApis = ProviderResponsesComponentBootstraps.Reload();

        return new ControlPlaneProviderPackageReloadResult
        {
            LoadedAssemblyCount = preloadResult.LoadedAssemblies.Count,
            IssueCount = preloadResult.Issues.Count,
            SupportedProtocolAdapterIds = supportedProtocolAdapterIds,
            SupportedWireApis = supportedWireApis,
            Issues = preloadResult.Issues.Select(static issue => issue.Message).ToArray(),
        };
    }
}
