namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddProviderPackageNavigationModule()
        => AddNavigationModule(
            "provider_package",
            "协议适配器包",
            "管理 modules/model/provider-adapters/<package>/provider.toml 模型提供方 adapter manifest。",
            ProviderPackageCollectionCategoryId);
}
