using TianShu.ControlPlane.Abstractions;
using TianShu.Execution.Runtime;

namespace TianShu.ControlPlane;

/// <summary>
/// TianShu 正式控制平面创建扩展。
/// Factory extensions for the formal TianShu control-plane shell.
/// </summary>
public static class TianShuControlPlaneExtensions
{
    /// <summary>
    /// 从 execution runtime 创建正式 control-plane 组合壳。
    /// Creates the formal control-plane composition shell from an execution runtime.
    /// </summary>
    public static ITianShuControlPlane AsControlPlane(this IExecutionRuntime runtime)
        => new TianShuControlPlane(runtime);
}
