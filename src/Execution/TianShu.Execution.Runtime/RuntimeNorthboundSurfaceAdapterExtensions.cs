namespace TianShu.Execution.Runtime;

public static class RuntimeNorthboundSurfaceAdapterExtensions
{
    public static IRuntimeNorthboundSurface AsNorthboundSurface(this IExecutionRuntime runtime)
        => new RuntimeNorthboundSurfaceAdapter(runtime);
}
