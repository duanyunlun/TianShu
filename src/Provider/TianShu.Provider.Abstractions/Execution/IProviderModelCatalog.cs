namespace TianShu.Provider.Abstractions;

/// <summary>
/// provider 模型能力目录抽象。
/// Provider model capability catalog abstraction.
/// </summary>
public interface IProviderModelCatalog
{
    /// <summary>
    /// 列出当前 provider 暴露的模型描述。
    /// Lists model descriptors exposed by the current provider.
    /// </summary>
    IReadOnlyList<ProviderModelDescriptor> ListModels();

    /// <summary>
    /// 尝试解析指定模型的能力描述。
    /// Tries to resolve the capability descriptor for the specified model.
    /// </summary>
    bool TryGetModel(string? model, out ProviderModelDescriptor descriptor);

    /// <summary>
    /// 判断模型是否支持并行工具调用。
    /// Determines whether the model supports parallel tool calls.
    /// </summary>
    bool SupportsParallelToolCalls(string? model);

    /// <summary>
    /// 判断模型是否支持搜索工具。
    /// Determines whether the model supports search tools.
    /// </summary>
    bool SupportsSearchTool(string? model);

    /// <summary>
    /// 判断模型是否支持图像输入。
    /// Determines whether the model supports image input.
    /// </summary>
    bool SupportsImageInput(string? model);

    /// <summary>
    /// 判断模型是否支持 original 细节级别的图像查看。
    /// Determines whether the model supports original-detail image viewing.
    /// </summary>
    bool SupportsImageDetailOriginal(string? model);

    /// <summary>
    /// 判断模型的 web search 工具是否支持图文混合内容。
    /// Determines whether the web-search tool supports mixed text-image content.
    /// </summary>
    bool SupportsWebSearchImageContent(string? model);

    /// <summary>
    /// 获取模型默认 shell tool 类型标识。
    /// Gets the default shell-tool kind token for the model.
    /// </summary>
    string? GetShellToolType(string? model);

    /// <summary>
    /// 判断模型是否支持 reasoning summary。
    /// Determines whether the model supports reasoning summaries.
    /// </summary>
    bool SupportsReasoningSummaries(string? model);

    /// <summary>
    /// 获取模型默认 reasoning effort。
    /// Gets the default reasoning effort for the model.
    /// </summary>
    string GetDefaultReasoningEffort(string? model);

    /// <summary>
    /// 获取模型默认 reasoning summary。
    /// Gets the default reasoning summary for the model.
    /// </summary>
    string? GetDefaultReasoningSummary(string? model);

    /// <summary>
    /// 判断模型是否支持 verbosity。
    /// Determines whether the model supports verbosity controls.
    /// </summary>
    bool SupportsVerbosity(string? model);

    /// <summary>
    /// 判断模型是否偏好 Responses WebSocket 传输。
    /// Determines whether the model prefers Responses WebSocket transport.
    /// </summary>
    bool PrefersResponsesWebsockets(string? model);

    /// <summary>
    /// 获取模型默认 verbosity。
    /// Gets the default verbosity for the model.
    /// </summary>
    string? GetDefaultVerbosity(string? model);

    /// <summary>
    /// 获取模型基础指令模板。
    /// Gets the base instruction template for the model.
    /// </summary>
    string GetBaseInstructions(string? model);

    /// <summary>
    /// 判断模型是否使用 freeform apply_patch 工具。
    /// Determines whether the model uses the freeform apply_patch tool.
    /// </summary>
    bool UsesFreeformApplyPatchTool(string? model);

    /// <summary>
    /// 获取模型声明的 apply_patch 工具类型。
    /// Gets the declared apply_patch tool kind for the model.
    /// </summary>
    string? GetApplyPatchToolType(string? model);

    /// <summary>
    /// 获取模型声明的实验性工具集合。
    /// Gets the experimental supported-tools set declared by the model.
    /// </summary>
    IReadOnlyList<string>? GetExperimentalSupportedTools(string? model);
}
