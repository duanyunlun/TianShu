namespace TianShu.Kernel;

/// <summary>
/// 模型路由种类注册表工厂，负责从内核阶段定义解析可用 route kind。
/// Model route-kind registry factory that resolves available route kinds from kernel stage definitions.
/// </summary>
public static class ModelRouteKindRegistryFactory
{
    /// <summary>
    /// 基于运行时配置解析可用于模型路由的 route kind 集合。
    /// Resolves the route kinds available for model routing from runtime configuration.
    /// </summary>
    public static IReadOnlyList<string> ResolveRegisteredRouteKinds(Dictionary<string, object?>? config)
    {
        var registry = StageRegistryRuntimeComposition.CreateRegistryFromConfig(config);
        if (!registry.IsValid)
        {
            var issue = registry.Issues.FirstOrDefault(static item =>
                    item.Severity == RuntimeStageRegistryIssueSeverity.Error)
                ?? registry.Issues.First();
            throw new InvalidOperationException($"Stage Registry 无效：{issue.Code}，{issue.Message}");
        }

        return registry.Stages
            .Select(static stage => stage.ModelRouteKind.Value)
            .ToArray();
    }
}
