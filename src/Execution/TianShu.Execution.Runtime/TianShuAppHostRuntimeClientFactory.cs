namespace TianShu.Execution.Runtime;

/// <summary>
/// 创建正式的 AppHost runtime client。该客户端负责启动并访问 AppHost companion，消费层不得直接构造具体实现。
/// </summary>
public static class TianShuAppHostRuntimeClientFactory
{
    public static IExecutionRuntime Create()
        => new TianShuExecutionRuntime();

    public static IExecutionRuntimeDiagnostics CreateDiagnostics()
        => new TianShuExecutionRuntime();
}
