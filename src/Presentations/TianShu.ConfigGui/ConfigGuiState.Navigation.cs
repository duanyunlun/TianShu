using TianShu.Contracts.Configuration;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void BuildNavigationModules()
    {
        NavigationModules.Clear();
        AddFoundationNavigationModule();
        AddProfileCompositionNavigationModule();
        AddAgentNavigationModule();
        AddModelConfigurationNavigationModule();
        AddMemoryNavigationModule();
        AddPromptPackNavigationModule();
        AddSkillPackageNavigationModule();
        AddToolPackageNavigationModule();
        AddMcpServerPackageNavigationModule();
        AddArtifactStorePackageNavigationModule();
        AddDiagnosticSinkPackageNavigationModule();
        AddWorkspaceResolverPackageNavigationModule();
        AddPolicyStrategyPackageNavigationModule();
    }

    private void AddNavigationModule(string id, string displayName, string description, params string[] pageIds)
    {
        var pages = pageIds
            .Select(pageId => Categories.FirstOrDefault(page => string.Equals(page.Id, pageId, StringComparison.OrdinalIgnoreCase)))
            .OfType<ConfigCategoryRow>()
            .ToArray();
        if (pages.Length == 0)
        {
            return;
        }

        NavigationModules.Add(new ConfigNavigationModuleRow(id, displayName, description, pages, NavigationModules.Count == 0));
    }
}
