namespace TianShu.AppHost.Tools;

/// <summary>
/// 工具推荐流程使用的 discoverable connector 载荷。
/// Discoverable connector payload used by the tool suggestion flow.
/// </summary>
internal sealed record KernelToolSuggestConnectorInfo(
    string Id,
    string Name,
    string? Description,
    string? InstallUrl);
