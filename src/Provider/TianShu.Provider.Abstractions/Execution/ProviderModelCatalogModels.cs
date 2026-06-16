namespace TianShu.Provider.Abstractions;

/// <summary>
/// provider 模型能力描述。
/// Provider model capability descriptor.
/// </summary>
/// <param name="Id">模型标识。Model identifier.</param>
/// <param name="Model">模型名称。Model name.</param>
/// <param name="DisplayName">展示名称。Display name.</param>
/// <param name="Description">描述。Description.</param>
/// <param name="Hidden">是否在模型列表中隐藏。Whether the model is hidden from listings.</param>
/// <param name="SupportedInApi">是否在公开 API 中可用。Whether the model is available in the public API.</param>
/// <param name="AvailabilityNuxMessage">可用性提示。Availability NUX message.</param>
/// <param name="BaseInstructions">基础指令模板。Base instruction template.</param>
/// <param name="ApplyPatchToolType">apply_patch 工具类型。apply_patch tool kind.</param>
/// <param name="DefaultReasoningEffort">默认推理强度。Default reasoning effort.</param>
/// <param name="SupportedReasoningEfforts">支持的推理强度集合。Supported reasoning effort set.</param>
/// <param name="WebSearchToolType">web search 工具类型。web-search tool kind.</param>
/// <param name="InputModalities">输入模态集合。Input modalities.</param>
/// <param name="ExperimentalSupportedTools">实验性支持工具。Experimental supported tools.</param>
/// <param name="SupportsImageDetailOriginal">是否支持 original 图像细节。Whether original image detail is supported.</param>
/// <param name="SupportsPersonality">是否支持 personality。Whether personality is supported.</param>
/// <param name="SupportsParallelToolCalls">是否支持并行工具调用。Whether parallel tool calls are supported.</param>
/// <param name="ShellToolType">默认 shell tool 类型标识。Default shell-tool kind token.</param>
/// <param name="SupportsSearchTool">是否支持搜索工具。Whether the search tool is supported.</param>
/// <param name="SupportsReasoningSummaries">是否支持 reasoning summaries。Whether reasoning summaries are supported.</param>
/// <param name="DefaultReasoningSummary">默认 reasoning summary。Default reasoning summary.</param>
/// <param name="PreferWebsockets">是否偏好 WebSocket 传输。Whether WebSocket transport is preferred.</param>
/// <param name="SupportsVerbosity">是否支持 verbosity。Whether verbosity is supported.</param>
/// <param name="DefaultVerbosity">默认 verbosity。Default verbosity.</param>
/// <param name="Priority">列表排序优先级。Ordering priority.</param>
/// <param name="UpgradeModel">升级目标模型。Upgrade target model.</param>
/// <param name="UpgradeMigrationMarkdown">升级迁移说明。Upgrade migration markdown.</param>
public sealed record ProviderModelDescriptor(
    string Id,
    string Model,
    string DisplayName,
    string Description,
    bool Hidden,
    bool SupportedInApi,
    string? AvailabilityNuxMessage,
    string BaseInstructions,
    string? ApplyPatchToolType,
    string DefaultReasoningEffort,
    IReadOnlyList<ProviderReasoningEffortDescriptor> SupportedReasoningEfforts,
    string WebSearchToolType,
    IReadOnlyList<string> InputModalities,
    IReadOnlyList<string>? ExperimentalSupportedTools,
    bool SupportsImageDetailOriginal,
    bool SupportsPersonality,
    bool SupportsParallelToolCalls,
    string? ShellToolType,
    bool SupportsSearchTool,
    bool SupportsReasoningSummaries,
    string? DefaultReasoningSummary,
    bool PreferWebsockets,
    bool SupportsVerbosity,
    string? DefaultVerbosity,
    int Priority,
    string? UpgradeModel,
    string? UpgradeMigrationMarkdown);

/// <summary>
/// provider 模型支持的 reasoning effort 描述。
/// Provider model supported reasoning-effort descriptor.
/// </summary>
/// <param name="Effort">推理强度标识。Reasoning effort identifier.</param>
/// <param name="Description">说明文案。Description text.</param>
public sealed record ProviderReasoningEffortDescriptor(
    string Effort,
    string Description);
