using TianShu.Cli.Interaction.Events;

namespace TianShu.Cli.Interaction.Presenters;

/// <summary>
/// Legacy adapter for callers that still ask for plain tool summaries.
/// 兼容旧调用方的工具摘要适配器；真实解析规则统一委托给 typed payload。
/// </summary>
internal static class ToolInvocationSummaryBuilder
{
    public static string? BuildInputSummary(string toolName, string? inputText)
        => ToolInvocationPayload.Create(toolName, inputText, outputText: null, status: null)
            .Input
            ?.Subject;

    public static string? BuildOutputSummary(string toolName, string? outputText, string? status)
        => ToolInvocationPayload.Create(toolName, inputText: null, outputText, status)
            .Output
            ?.Summary;
}
