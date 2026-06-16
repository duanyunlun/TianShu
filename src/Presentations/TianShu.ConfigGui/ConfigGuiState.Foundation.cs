using TianShu.Contracts.Configuration;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddFoundationNavigationModule()
        => AddNavigationModule(
            "foundation",
            "基础配置",
            "只管理不属于代理、模型、记忆、提示词、技能、工具、MCP服务、过程生成物、诊断输出、工作空间或审批策略的基础配置。",
            ConfigurationCategoryIds.Foundation,
            ConfigurationCategoryIds.Experience,
            ConfigurationCategoryIds.ExtensionsImports);
}
