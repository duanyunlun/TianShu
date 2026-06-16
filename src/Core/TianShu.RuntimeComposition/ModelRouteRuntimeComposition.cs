using TianShu.Configuration;

namespace TianShu.RuntimeComposition;

/// <summary>
/// 模型路由诊断的运行时组合入口。
/// Runtime composition entry point for model-route diagnostics.
/// </summary>
public static class ModelRouteRuntimeComposition
{
    /// <summary>
    /// 基于运行时配置创建模型路由诊断，CLI 等消费层不直接读取内核阶段注册细节。
    /// Creates a model-route diagnostic from runtime config so consumers do not read kernel stage registration details directly.
    /// </summary>
    public static TianShuModelRouteDiagnostic BuildRouteDiagnostic(
        Dictionary<string, object?>? config,
        string? routeKind,
        string? routeSetId = null)
    {
        var rawConfig = config ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        return TianShuModelRouteSetDefaults.BuildRouteDiagnostic(
            rawConfig,
            routeKind,
            routeSetId,
            BuildRegisteredRouteKinds(rawConfig));
    }

    /// <summary>
    /// 基于运行时配置解析模型路由可用种类。
    /// Resolves registered model route kinds from runtime config.
    /// </summary>
    public static IReadOnlyList<string> BuildRegisteredRouteKinds(Dictionary<string, object?>? config)
        => TianShuModelRouteSetDefaults.NormalizeRegisteredRouteKinds(
            TianShu.Kernel.ModelRouteKindRegistryFactory.ResolveRegisteredRouteKinds(config));
}
