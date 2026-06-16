using TianShu.Contracts.Configuration;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddPolicyStrategyPackageNavigationModule()
        => AddNavigationModule(
            "policy_strategy_package",
            "审批策略",
            "管理 modules/policies/strategies/<package>/policy.toml 受控审批策略。",
            ConfigurationCategoryIds.SecurityGovernance,
            PolicyStrategyPackageCollectionCategoryId);
}
