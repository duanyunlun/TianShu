using TianShu.Contracts.Provider;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider 工具事件工厂边界。
/// Typed factory boundary for provider tool lifecycle events.
/// </summary>
public interface IProviderToolEventFactory
{
    /// <summary>
    /// 创建 provider 工具指令事件。
    /// Creates a provider tool directive event.
    /// </summary>
    ProviderToolDirectiveEvent CreateToolDirective(ProviderToolDirectiveRequest request);

    /// <summary>
    /// 创建 provider 工具输出增量事件。
    /// Creates a provider tool output-delta event.
    /// </summary>
    ProviderToolOutputDeltaEvent CreateToolOutputDelta(ProviderToolOutputDeltaRequest request);

    /// <summary>
    /// 创建 provider 工具终态事件。
    /// Creates a provider tool terminal result event.
    /// </summary>
    ProviderToolResultEvent CreateToolResult(ProviderToolResultRequest request);
}

/// <summary>
/// provider 工具指令事件的 typed 输入模型。
/// Typed input model for provider tool directive events.
/// </summary>
public sealed record ProviderToolDirectiveRequest(
    string? CallId,
    string? ItemId,
    string? ToolName,
    string? InputText,
    bool RequiresApproval);

/// <summary>
/// provider 工具输出增量事件的 typed 输入模型。
/// Typed input model for provider tool output-delta events.
/// </summary>
public sealed record ProviderToolOutputDeltaRequest(
    string? CallId,
    string? ItemId,
    string? ToolName,
    string? ItemType,
    string? InputText,
    string? OutputText,
    bool? RequiresApproval);

/// <summary>
/// provider 工具终态事件的 typed 输入模型。
/// Typed input model for provider tool terminal result events.
/// </summary>
public sealed record ProviderToolResultRequest(
    string? CallId,
    string? ItemId,
    string? ToolName,
    string? ItemType,
    string? InputText,
    string? OutputText,
    bool? RequiresApproval);
