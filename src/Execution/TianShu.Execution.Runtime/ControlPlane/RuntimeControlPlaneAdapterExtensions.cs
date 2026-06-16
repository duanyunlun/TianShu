using TianShu.Execution.Runtime.ControlPlane;
using TianShu.ControlPlane.Abstractions;

namespace TianShu.Execution.Runtime;

public static class RuntimeControlPlaneAdapterExtensions
{
    public static ITianShuControlPlane AsRuntimeControlPlane(this IExecutionRuntime runtime)
        => new RuntimeControlPlaneAdapter(runtime);
}
