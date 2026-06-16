using TianShu.Contracts.Configuration;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddModelConfigurationNavigationModule()
        => AddNavigationModule(
            "model_configuration",
            "模型",
            "统一管理模型选择、提供方实例、模型协议适配、默认协议规则、模型路由方案与协议适配器包。",
            ConfigurationCategoryIds.ConnectivityModel,
            ProviderCollectionCategoryId,
            ModelProtocolMappingCollectionCategoryId,
            DefaultModelProtocolRuleSetCollectionCategoryId,
            ModelRouteSetCollectionCategoryId,
            ProviderPackageCollectionCategoryId);
}
