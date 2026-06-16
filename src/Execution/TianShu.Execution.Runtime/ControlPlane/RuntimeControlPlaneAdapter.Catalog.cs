using TianShu.Contracts.Catalog;

namespace TianShu.Execution.Runtime.ControlPlane;

public sealed partial class RuntimeControlPlaneAdapter
{
    public async Task<ControlPlaneConfigSnapshotResult> ReadConfigAsync(ControlPlaneConfigReadQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.ReadConfigAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneConfigRequirementsResult> ReadConfigRequirementsAsync(
        ControlPlaneConfigRequirementsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.ReadConfigRequirementsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneConfigWriteResult> WriteConfigValueAsync(
        ControlPlaneConfigValueWriteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await runtime.WriteConfigValueAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneConfigWriteResult> WriteConfigBatchAsync(
        ControlPlaneConfigBatchWriteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await runtime.WriteConfigBatchAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneModelCatalogResult> ListModelsAsync(ControlPlaneModelCatalogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.ListModelsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public Task<CapabilityCatalogSnapshot> GetCapabilityCatalogAsync(
        GetCapabilityCatalog query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetCapabilityCatalogAsync(query, cancellationToken);
    }

    public Task<ResolvedEngineBinding> ResolveEngineBindingAsync(
        ResolveEngineBinding query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ResolveEngineBindingAsync(query, cancellationToken);
    }

    public async Task<ControlPlaneSkillCatalogResult> ListSkillsAsync(ControlPlaneSkillCatalogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.ListSkillsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneSkillConfigWriteResult> WriteSkillConfigAsync(
        ControlPlaneSkillConfigWriteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await runtime.WriteSkillsConfigAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneRemoteSkillCatalogResult> ListRemoteSkillsAsync(
        ControlPlaneRemoteSkillCatalogQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.ListRemoteSkillsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneRemoteSkillExportResult> ExportRemoteSkillAsync(
        ControlPlaneRemoteSkillExportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await runtime.ExportRemoteSkillAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlanePluginCatalogResult> ListPluginsAsync(ControlPlanePluginCatalogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.ListPluginsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlanePluginReadResult> ReadPluginAsync(ControlPlanePluginReadQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.ReadPluginAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlanePluginInstallResult> InstallPluginAsync(
        ControlPlanePluginInstallCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await runtime.InstallPluginAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public Task<ControlPlanePluginUninstallResult> UninstallPluginAsync(ControlPlanePluginUninstallCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.UninstallPluginAsync(command, cancellationToken);
    }

    public async Task<ControlPlaneAppCatalogResult> ListAppsAsync(ControlPlaneAppCatalogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.ListAppsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneExperimentalFeatureCatalogResult> ListExperimentalFeaturesAsync(
        ControlPlaneExperimentalFeatureQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.ListExperimentalFeaturesAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public Task<ControlPlaneCollaborationModeCatalogResult> ListCollaborationModesAsync(CancellationToken cancellationToken)
        => runtime.ListCollaborationModesAsync(cancellationToken);

    public async Task<ControlPlaneMcpServerCatalogResult> ListMcpServerStatusAsync(
        ControlPlaneMcpServerStatusQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.ListMcpServerStatusAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public Task<ControlPlaneMcpServerReloadResult> ReloadMcpServersAsync(CancellationToken cancellationToken)
        => runtime.ReloadMcpServersAsync(new ControlPlaneMcpServerReloadCommand(), cancellationToken);

    public Task<ControlPlaneProviderPackageReloadResult> ReloadProviderPackagesAsync(CancellationToken cancellationToken)
        => runtime.ReloadProviderPackagesAsync(new ControlPlaneProviderPackageReloadCommand(), cancellationToken);

    public Task<ControlPlaneMcpServerOauthLoginStartResult> StartMcpServerOauthLoginAsync(
        ControlPlaneMcpServerOauthLoginStartCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.StartMcpServerOauthLoginAsync(command, cancellationToken);
    }
}
