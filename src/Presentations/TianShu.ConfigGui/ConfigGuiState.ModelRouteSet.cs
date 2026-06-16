using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddModelRouteSetNavigationModule()
        => AddNavigationModule(
            "model_route_set",
            "模型路由方案",
            "管理 modules/model/route-sets/<id>.toml 中的 route、候选模型与 fallback 顺序。",
            ModelRouteSetCollectionCategoryId);

    private string CurrentModelRouteSetSaveTarget()
    {
        var routeSetId = ModelRouteSetId.Value.Trim();
        if (!IsValidModelRouteSetId(routeSetId))
        {
            routeSetId = selectedModelRouteSetId ?? "default";
        }

        return ResolveModelRouteSetPath(routeSetId);
    }

    private string ResolveModelRouteSetPath(string routeSetId)
        => Path.Combine(
            TianShuHomePathUtilities.ResolveModulePathFromConfig(ConfigPath, "model", "route-sets"),
            $"{routeSetId}.toml");

    private ConfigurationApplyResult SaveModelRouteSetModuleFile(ModelRouteSetConfigItem routeSet)
        => applier.Apply(ResolveModelRouteSetPath(routeSet.Id), new ConfigurationChangeSet
        {
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"model_route_sets.{routeSet.Id}.display_name",
                    Value = StructuredValue.FromString(routeSet.DisplayName),
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"model_route_sets.{routeSet.Id}.description",
                    Value = StructuredValue.FromString(routeSet.Description),
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = $"model_route_sets.{routeSet.Id}.routes",
                    Value = BuildModelRouteSetRoutesValue(routeSet),
                },
            ],
        });

    private ConfigurationApplyResult SaveActiveModelRouteSetReference(ModelRouteSetConfigItem routeSet)
        => applier.Apply(ConfigPath, new ConfigurationChangeSet
        {
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    Key = "model_route_set",
                    Value = StructuredValue.FromString(routeSet.Id),
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Unset,
                    Key = $"model_route_sets.{routeSet.Id}",
                },
            ],
        });
}





