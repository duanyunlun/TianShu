using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// 将正式工具契约结果投影到旧 AppHost 工具结果。
/// Projects formal tool contract results into the legacy AppHost tool result bridge.
/// </summary>
internal static class KernelToolInvocationResultMapper
{
    public static KernelToolResult FromInvocationResult(ToolInvocationResult result)
    {
        var projection = ToolInvocationResultProjector.Project(result);

        var outputContentItems = projection.OutputContentItems.Count == 0
            ? null
            : projection.OutputContentItems
                .Select(static item => new KernelToolOutputContentItem(item.Type, item.Text, item.ImageUrl, item.Detail))
                .ToArray();
        var rawOutputContentItems = projection.RawOutputContentItems.Count == 0
            ? null
            : projection.RawOutputContentItems.Select(static item => item.Clone()).ToArray();

        return new KernelToolResult(
            projection.Success,
            projection.OutputText,
            outputContentItems,
            rawOutputContentItems,
            projection.StructuredOutput);
    }
}
