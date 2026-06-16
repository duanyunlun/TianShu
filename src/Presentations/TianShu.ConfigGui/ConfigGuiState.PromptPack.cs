namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddPromptPackNavigationModule()
        => AddNavigationModule(
            "prompt_pack",
            "提示词",
            "管理 modules/prompts/<package>/prompt.toml 提示词包。",
            PromptCollectionCategoryId);
}
