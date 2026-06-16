namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddSkillPackageNavigationModule()
        => AddNavigationModule(
            "skill_package",
            "技能",
            "管理 modules/skills/<package>/SKILL.md 技能包投影与启用状态。",
            SkillPackageCollectionCategoryId);
}
