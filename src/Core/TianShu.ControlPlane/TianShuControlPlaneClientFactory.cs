using TianShu.ControlPlane.Abstractions;
using TianShu.Execution.Runtime;

namespace TianShu.ControlPlane;

/// <summary>
/// 面向消费层的控制平面客户端工厂。
/// </summary>
public static class TianShuControlPlaneClientFactory
{
    /// <summary>
    /// 基于正式 runtime client 创建控制平面客户端。
    /// </summary>
    public static ITianShuControlPlane Create(IExecutionRuntime runtime)
        => new TianShuControlPlane(runtime);
}
