namespace TianShu.AppHost.Tools;

/// <summary>
/// 表示宿主工具输出中的单个内容项，供本地 shell、代码执行与图像查看等工具复用。
/// Represents a single host-tool output content item shared by shell, code execution, and image-view flows.
/// </summary>
internal sealed record KernelToolOutputContentItem(
    string Type,
    string? Text = null,
    string? ImageUrl = null,
    string? Detail = null);
