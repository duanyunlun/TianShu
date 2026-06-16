using System.Collections.Concurrent;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// provider 模型能力目录入口。
/// Entry point for provider model capability catalogs.
/// </summary>
public static class ProviderModelCatalogs
{
    private static readonly ConcurrentDictionary<string, Lazy<IProviderModelCatalog>> Catalogs =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 解析指定协议适配器的模型能力目录；空值回退到默认 provider。
    /// Resolves the model capability catalog for the specified protocol adapter; blank values fall back to the default provider.
    /// </summary>
    /// <param name="protocolAdapterId">协议适配器标识。Protocol adapter identifier.</param>
    /// <returns>对应 provider 的模型能力目录。Model capability catalog for the provider.</returns>
    public static IProviderModelCatalog Resolve(string? protocolAdapterId = null)
    {
        var resolvedProtocolAdapterId = string.IsNullOrWhiteSpace(protocolAdapterId)
            ? ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId()
            : ProviderRuntimeBootstrapRegistry.NormalizeProtocolAdapterId(protocolAdapterId, "provider model catalog");

        return Catalogs.GetOrAdd(
            resolvedProtocolAdapterId,
            static adapterId => new Lazy<IProviderModelCatalog>(
                () => ProviderRuntimeBootstrapRegistry.CreateRuntimeState(adapterId).Bootstrap.CreateModelCatalog(),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>
    /// 列出指定 provider 的模型描述；空值回退到默认 provider。
    /// Lists model descriptors for the specified provider; blank values fall back to the default provider.
    /// </summary>
    public static IReadOnlyList<ProviderModelDescriptor> ListModels(string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).ListModels();

    /// <summary>
    /// 尝试解析指定模型的能力描述。
    /// Tries to resolve the capability descriptor for the specified model.
    /// </summary>
    public static bool TryGetModel(
        string? model,
        out ProviderModelDescriptor descriptor,
        string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).TryGetModel(model, out descriptor);

    public static bool SupportsParallelToolCalls(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).SupportsParallelToolCalls(model);

    public static bool SupportsSearchTool(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).SupportsSearchTool(model);

    public static bool SupportsImageInput(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).SupportsImageInput(model);

    public static bool SupportsImageDetailOriginal(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).SupportsImageDetailOriginal(model);

    public static bool SupportsWebSearchImageContent(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).SupportsWebSearchImageContent(model);

    public static string? GetShellToolType(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).GetShellToolType(model);

    public static bool SupportsReasoningSummaries(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).SupportsReasoningSummaries(model);

    public static string GetDefaultReasoningEffort(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).GetDefaultReasoningEffort(model);

    public static string? GetDefaultReasoningSummary(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).GetDefaultReasoningSummary(model);

    public static bool SupportsVerbosity(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).SupportsVerbosity(model);

    public static bool PrefersResponsesWebsockets(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).PrefersResponsesWebsockets(model);

    public static string? GetDefaultVerbosity(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).GetDefaultVerbosity(model);

    public static string GetBaseInstructions(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).GetBaseInstructions(model);

    public static bool UsesFreeformApplyPatchTool(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).UsesFreeformApplyPatchTool(model);

    public static string? GetApplyPatchToolType(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).GetApplyPatchToolType(model);

    public static IReadOnlyList<string>? GetExperimentalSupportedTools(string? model, string? protocolAdapterId = null)
        => Resolve(protocolAdapterId).GetExperimentalSupportedTools(model);
}
