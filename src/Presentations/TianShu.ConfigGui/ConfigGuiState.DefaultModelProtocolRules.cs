using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddDefaultModelProtocolRulesNavigationModule()
        => AddNavigationModule(
            "default_model_protocol_rules",
            "默认协议规则",
            "管理 modules/model/protocol-rules/<id>.toml 中跨提供方生效的默认模型协议通配规则。",
            DefaultModelProtocolRuleSetCollectionCategoryId);

    private string CurrentDefaultModelProtocolRuleSetSaveTarget()
    {
        var ruleSetId = DefaultModelProtocolRuleSetId.Value.Trim();
        if (!IsValidModelRouteSetId(ruleSetId))
        {
            ruleSetId = selectedDefaultModelProtocolRuleSetId ?? "default";
        }

        return ResolveDefaultModelProtocolRuleSetPath(ruleSetId);
    }

    private string ResolveDefaultModelProtocolRuleSetPath(string ruleSetId)
        => Path.Combine(
            TianShuHomePathUtilities.ResolveModulePathFromConfig(ConfigPath, "model", "protocol-rules"),
            $"{ruleSetId}.toml");

    private ConfigurationApplyResult SaveDefaultModelProtocolRuleSetModuleFile(ModelProtocolRuleSetDraft ruleSet)
        => applier.Apply(ResolveDefaultModelProtocolRuleSetPath(ruleSet.Id), new ConfigurationChangeSet
        {
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"model_protocol_rule_sets.{ruleSet.Id}.display_name",
                    Value = StructuredValue.FromString(ruleSet.DisplayName),
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"model_protocol_rule_sets.{ruleSet.Id}.description",
                    Value = StructuredValue.FromString(ruleSet.Description),
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"model_protocol_rule_sets.{ruleSet.Id}.rules",
                    Value = BuildModelProtocolRulesValue(ruleSet.Rules),
                },
            ],
        });

    private ConfigurationApplyResult SaveActiveDefaultModelProtocolRuleSetReference(ModelProtocolRuleSetDraft ruleSet)
        => applier.Apply(ConfigPath, new ConfigurationChangeSet
        {
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = "model_protocol_rule_set",
                    Value = StructuredValue.FromString(ruleSet.Id),
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Unset,
                    Key = $"model_protocol_rule_sets.{ruleSet.Id}",
                },
            ],
        });
}
