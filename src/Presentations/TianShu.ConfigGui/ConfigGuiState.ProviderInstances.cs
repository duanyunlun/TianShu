using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddProviderInstancesNavigationModule()
        => AddNavigationModule(
            "provider_instances",
            "提供方实例",
            "管理 modules/model/provider-instances/<id>.toml 中的 providers.<id> endpoint、secret env 与协议解析默认值。",
            ProviderCollectionCategoryId);

    private string CurrentProviderInstanceSaveTarget()
        => ResolveProviderInstancePath(CurrentProviderInstanceSetId());

    private string CurrentProviderInstanceSetId()
    {
        var configured = allFields.FirstOrDefault(static row => string.Equals(row.Key, "provider_instances", StringComparison.OrdinalIgnoreCase))?.CurrentValue;
        return IsValidModelRouteSetId(configured ?? string.Empty) ? configured!.Trim() : "default";
    }

    private string ResolveProviderInstancePath(string providerInstanceSetId)
        => Path.Combine(
            TianShuHomePathUtilities.ResolveModulePathFromConfig(ConfigPath, "model", "provider-instances"),
            $"{providerInstanceSetId}.toml");

    private ConfigurationApplyResult SaveProviderInstanceModuleFile(
        string providerId,
        string baseUrl,
        string apiKeyEnv,
        string defaultProtocol)
        => applier.Apply(CurrentProviderInstanceSaveTarget(), new ConfigurationChangeSet
        {
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"providers.{providerId}.base_url",
                    Value = StructuredValue.FromString(baseUrl),
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"providers.{providerId}.api_key_env",
                    Value = StructuredValue.FromString(apiKeyEnv),
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"providers.{providerId}.default_protocol",
                    Value = StructuredValue.FromString(defaultProtocol),
                },
            ],
        });

    private ConfigurationApplyResult SaveProviderProtocolMappingsModuleFile(ModelProtocolProviderDraft provider)
        => applier.Apply(CurrentProviderInstanceSaveTarget(), new ConfigurationChangeSet
        {
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"providers.{provider.Id}.model_overrides",
                    Value = BuildModelProtocolOverridesValue(provider),
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"providers.{provider.Id}.protocol_rules",
                    Value = BuildModelProtocolRulesValue(provider),
                },
            ],
        });

    private ConfigurationApplyResult DeleteProviderInstanceModuleEntry(string providerId)
        => applier.Apply(CurrentProviderInstanceSaveTarget(), new ConfigurationChangeSet
        {
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Unset,
                    Key = $"providers.{providerId}",
                },
            ],
        });

    private ConfigurationApplyResult SaveActiveProviderInstanceReference()
        => applier.Apply(ConfigPath, new ConfigurationChangeSet
        {
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = "provider_instances",
                    Value = StructuredValue.FromString(CurrentProviderInstanceSetId()),
                },
            ],
        });
}
