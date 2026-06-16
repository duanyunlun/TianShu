namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddWorkspaceResolverPackageNavigationModule()
        => AddNavigationModule(
            "workspace_resolver_package",
            "工作空间",
            "管理 modules/workspace/resolvers/<package>/resolver.toml 项目根解析策略。",
            WorkspaceCollectionCategoryId,
            WorkspaceResolverPackageCollectionCategoryId);
}
