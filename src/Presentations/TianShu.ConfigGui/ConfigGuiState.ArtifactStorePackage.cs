namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddArtifactStorePackageNavigationModule()
        => AddNavigationModule(
            "artifact_store_package",
            "过程生成物",
            "管理 modules/artifacts/stores/<package>/store.toml artifact store manifest。",
            ArtifactStorePackageCollectionCategoryId);
}
