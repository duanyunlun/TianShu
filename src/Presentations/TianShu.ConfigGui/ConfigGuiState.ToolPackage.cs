using TianShu.Contracts.Configuration;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddToolPackageNavigationModule()
        => AddNavigationModule(
            "tool_package",
            "工具",
            "管理 modules/tools/packages/<package>/tool.toml 工具包 manifest。",
            ConfigurationCategoryIds.CapabilitiesTools,
            ToolPackageCollectionCategoryId);
}
