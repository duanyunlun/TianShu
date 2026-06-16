using TianShu.AppHost.Tools.Runtime;

namespace TianShu.RuntimeComposition;

/// <summary>
/// 工具能力包运行时组合入口。
/// Runtime composition entry point for tool capability packages.
/// </summary>
internal static class ToolRuntimeComposition
{
    /// <summary>
    /// 创建默认工具注册表，并应用工具包 manifest 与工具 profile 选择。
    /// Creates the default tool registry and applies tool package manifests plus tool profile selections.
    /// </summary>
    public static KernelToolRegistry CreateDefaultToolRegistry(
        IReadOnlyDictionary<string, string>? configValues,
        string? workspacePath)
        => KernelToolRegistryFactory.CreateDefaultRegistry(configValues, workspacePath);

    /// <summary>
    /// 创建默认工具注册表，并接受原始对象型配置快照。
    /// Creates the default tool registry from a raw object-valued configuration snapshot.
    /// </summary>
    public static KernelToolRegistry CreateDefaultToolRegistry(
        IReadOnlyDictionary<string, object?>? configValues,
        string? workspacePath)
        => KernelToolRegistryFactory.CreateDefaultRegistry(configValues, workspacePath);
}
