using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

public interface IExecutionRuntimeDiagnostics : IExecutionRuntime
{
    Task<StructuredValue> InvokeDiagnosticRpcAsync(string method, StructuredValue? parameters, CancellationToken cancellationToken);
}
