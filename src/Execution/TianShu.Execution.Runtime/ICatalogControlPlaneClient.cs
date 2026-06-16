using TianShu.Contracts.Catalog;

namespace TianShu.Execution.Runtime;

public interface ICatalogControlPlaneClient
{
    Task<ControlPlaneModelCatalogResult> ListModelsAsync(ControlPlaneModelCatalogQuery request, CancellationToken cancellationToken);

    Task<CapabilityCatalogSnapshot> GetCapabilityCatalogAsync(
        GetCapabilityCatalog request,
        CancellationToken cancellationToken);

    Task<ResolvedEngineBinding> ResolveEngineBindingAsync(
        ResolveEngineBinding request,
        CancellationToken cancellationToken);

    Task<ControlPlaneSkillCatalogResult> ListSkillsAsync(ControlPlaneSkillCatalogQuery request, CancellationToken cancellationToken);

    Task<ControlPlaneSkillConfigWriteResult> WriteSkillsConfigAsync(ControlPlaneSkillConfigWriteCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneRemoteSkillCatalogResult> ListRemoteSkillsAsync(ControlPlaneRemoteSkillCatalogQuery request, CancellationToken cancellationToken);

    Task<ControlPlaneRemoteSkillExportResult> ExportRemoteSkillAsync(ControlPlaneRemoteSkillExportCommand request, CancellationToken cancellationToken);

    Task<ControlPlanePluginCatalogResult> ListPluginsAsync(ControlPlanePluginCatalogQuery request, CancellationToken cancellationToken);

    Task<ControlPlanePluginReadResult> ReadPluginAsync(ControlPlanePluginReadQuery request, CancellationToken cancellationToken);

    Task<ControlPlanePluginInstallResult> InstallPluginAsync(ControlPlanePluginInstallCommand request, CancellationToken cancellationToken);

    Task<ControlPlanePluginUninstallResult> UninstallPluginAsync(ControlPlanePluginUninstallCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneAppCatalogResult> ListAppsAsync(ControlPlaneAppCatalogQuery request, CancellationToken cancellationToken);

    Task<ControlPlaneExperimentalFeatureCatalogResult> ListExperimentalFeaturesAsync(ControlPlaneExperimentalFeatureQuery request, CancellationToken cancellationToken);

    Task<ControlPlaneMcpServerCatalogResult> ListMcpServerStatusAsync(ControlPlaneMcpServerStatusQuery request, CancellationToken cancellationToken);

    Task<ControlPlaneMcpServerReloadResult> ReloadMcpServersAsync(ControlPlaneMcpServerReloadCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneProviderPackageReloadResult> ReloadProviderPackagesAsync(ControlPlaneProviderPackageReloadCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneMcpServerOauthLoginStartResult> StartMcpServerOauthLoginAsync(ControlPlaneMcpServerOauthLoginStartCommand request, CancellationToken cancellationToken);
}
