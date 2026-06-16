using TianShu.Contracts.Catalog;

namespace TianShu.ControlPlane.Abstractions.Catalog;

public interface ICatalogControlPlane
{
    Task<ControlPlaneConfigSnapshotResult> ReadConfigAsync(ControlPlaneConfigReadQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneConfigRequirementsResult> ReadConfigRequirementsAsync(ControlPlaneConfigRequirementsQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneConfigWriteResult> WriteConfigValueAsync(ControlPlaneConfigValueWriteCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneConfigWriteResult> WriteConfigBatchAsync(ControlPlaneConfigBatchWriteCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneModelCatalogResult> ListModelsAsync(ControlPlaneModelCatalogQuery query, CancellationToken cancellationToken);

    Task<CapabilityCatalogSnapshot> GetCapabilityCatalogAsync(
        GetCapabilityCatalog query,
        CancellationToken cancellationToken);

    Task<ResolvedEngineBinding> ResolveEngineBindingAsync(
        ResolveEngineBinding query,
        CancellationToken cancellationToken);

    Task<ControlPlaneSkillCatalogResult> ListSkillsAsync(ControlPlaneSkillCatalogQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneSkillConfigWriteResult> WriteSkillConfigAsync(ControlPlaneSkillConfigWriteCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneRemoteSkillCatalogResult> ListRemoteSkillsAsync(ControlPlaneRemoteSkillCatalogQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneRemoteSkillExportResult> ExportRemoteSkillAsync(ControlPlaneRemoteSkillExportCommand command, CancellationToken cancellationToken);

    Task<ControlPlanePluginCatalogResult> ListPluginsAsync(ControlPlanePluginCatalogQuery query, CancellationToken cancellationToken);

    Task<ControlPlanePluginReadResult> ReadPluginAsync(ControlPlanePluginReadQuery query, CancellationToken cancellationToken);

    Task<ControlPlanePluginInstallResult> InstallPluginAsync(ControlPlanePluginInstallCommand command, CancellationToken cancellationToken);

    Task<ControlPlanePluginUninstallResult> UninstallPluginAsync(ControlPlanePluginUninstallCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneAppCatalogResult> ListAppsAsync(ControlPlaneAppCatalogQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneExperimentalFeatureCatalogResult> ListExperimentalFeaturesAsync(ControlPlaneExperimentalFeatureQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneCollaborationModeCatalogResult> ListCollaborationModesAsync(CancellationToken cancellationToken);

    Task<ControlPlaneMcpServerCatalogResult> ListMcpServerStatusAsync(ControlPlaneMcpServerStatusQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneMcpServerReloadResult> ReloadMcpServersAsync(CancellationToken cancellationToken);

    Task<ControlPlaneProviderPackageReloadResult> ReloadProviderPackagesAsync(CancellationToken cancellationToken);

    Task<ControlPlaneMcpServerOauthLoginStartResult> StartMcpServerOauthLoginAsync(ControlPlaneMcpServerOauthLoginStartCommand command, CancellationToken cancellationToken);
}
