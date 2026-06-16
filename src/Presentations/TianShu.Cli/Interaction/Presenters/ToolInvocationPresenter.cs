using TianShu.Cli.Interaction.Events;

namespace TianShu.Cli.Interaction.Presenters;

internal static class ToolInvocationPresenter
{
    public static ToolInvocationBlock? BuildCompleted(
        string toolName,
        string? inputText,
        string? outputText,
        string? status)
        => BuildCompleted(
            toolName,
            inputText,
            outputText,
            status,
            ToolInvocationPayload.Create(toolName, inputText, outputText, status));

    public static ToolInvocationBlock? BuildCompleted(
        string toolName,
        string? inputText,
        string? outputText,
        string? status,
        ToolInvocationPayload? payload)
    {
        var subject = payload?.Input?.Subject ?? BuildInputSummary(toolName, inputText);
        var kind = payload?.Kind ?? ToolInvocationPayload.ResolveKind(toolName);
        var summary = payload?.Output?.Summary
                      ?? BuildOutputSummary(toolName, outputText, status)
                      ?? BuildStatusSummary(status, hasSubject: !string.IsNullOrWhiteSpace(subject));
        if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return new ToolInvocationBlock(
            kind,
            new ToolInvocationTitle(ResolveTitle(kind, toolName)),
            string.IsNullOrWhiteSpace(subject)
                ? null
                : new ToolInvocationSubject(Truncate(subject, 120)!, AlwaysShow: kind == ToolPresentationKind.Command),
            string.IsNullOrWhiteSpace(summary)
                ? null
                : new ToolInvocationResult(Truncate(summary, 160)!),
            ResolveStatus(status, payload),
            ErrorText: null);
    }

    public static string RenderPlain(ToolInvocationBlock block)
    {
        var lines = new List<string> { $"● {block.TitleText}" };
        if (!string.IsNullOrWhiteSpace(block.SubjectText))
        {
            lines.Add($"  {block.SubjectText}");
        }

        if (!string.IsNullOrWhiteSpace(block.Summary))
        {
            lines.Add($"  {ResolveMarker(block.Status)} {block.Summary}");
        }

        if (!string.IsNullOrWhiteSpace(block.ErrorText))
        {
            lines.Add($"  {block.ErrorText}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string? BuildInputSummary(string toolName, string? inputText)
        => ToolInvocationSummaryBuilder.BuildInputSummary(toolName, inputText);

    public static string? BuildOutputSummary(string toolName, string? outputText, string? status)
        => ToolInvocationSummaryBuilder.BuildOutputSummary(toolName, outputText, status);

    private static string ResolveTitle(ToolPresentationKind kind, string toolName)
        => kind switch
        {
            ToolPresentationKind.Command => "执行命令",
            ToolPresentationKind.CodePatch => "修改代码",
            ToolPresentationKind.FileChange => "修改文件",
            ToolPresentationKind.PlanUpdate => "更新计划",
            ToolPresentationKind.WebSearch => "搜索网页",
            ToolPresentationKind.ImageGeneration => "生成图片",
            ToolPresentationKind.ImageView => "查看图片",
            ToolPresentationKind.Search => "搜索工具",
            _ => $"使用 {toolName}",
        };

    private static string? BuildStatusSummary(string? status, bool hasSubject)
        => string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
            ? "失败"
            : string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
              && !hasSubject
                ? "完成"
                : null;

    private static ToolPresentationStatus ResolveStatus(string? status, bool isCancellation)
        => isCancellation
            ? ToolPresentationStatus.Canceled
            : string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
                ? ToolPresentationStatus.Failed
                : string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
                    ? ToolPresentationStatus.Completed
                    : ToolPresentationStatus.Running;

    private static ToolPresentationStatus ResolveStatus(string? status, ToolInvocationPayload? payload)
        => ResolveStatus(status, payload?.Output?.IsCancellation == true);

    private static string ResolveMarker(ToolPresentationStatus status)
        => status switch
        {
            ToolPresentationStatus.Failed => "✗",
            ToolPresentationStatus.Running => "▶",
            ToolPresentationStatus.Canceled => "⊘",
            _ => "✓",
        };

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
