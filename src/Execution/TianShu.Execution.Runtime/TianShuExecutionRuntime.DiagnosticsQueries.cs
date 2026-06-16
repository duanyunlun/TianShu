using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Execution.Runtime;

public sealed partial class TianShuExecutionRuntime
{
    public async Task<ExecutionTrace?> GetExecutionTraceAsync(GetExecutionTrace query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var trace = await projectionRuntimeStores.ExecutionTraces
            .GetAsync(query.TraceId, cancellationToken)
            .ConfigureAwait(false);
        if (trace is not null || process is null)
        {
            return trace;
        }

        var rpcResult = await InvokeDiagnosticRpcAsync(
                "diagnostics/trace/read",
                StructuredValue.FromPlainObject(new Dictionary<string, object?>
                {
                    ["traceId"] = query.TraceId.Value,
                }),
                cancellationToken)
            .ConfigureAwait(false);
        return DeserializeStructuredValue<ExecutionTrace>(rpcResult);
    }

    public async Task<IReadOnlyList<AttemptSummary>> ListAttemptSummariesAsync(ListAttemptSummaries query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var attempts = await projectionRuntimeStores.ExecutionTraces
            .ListAttemptsAsync(query.ExecutionId, cancellationToken)
            .ConfigureAwait(false);
        if (attempts.Count > 0 || process is null)
        {
            return attempts;
        }

        var rpcResult = await InvokeDiagnosticRpcAsync(
                "diagnostics/attempts/list",
                StructuredValue.FromPlainObject(new Dictionary<string, object?>
                {
                    ["executionId"] = query.ExecutionId.Value,
                }),
                cancellationToken)
            .ConfigureAwait(false);
        return DeserializeStructuredValue<IReadOnlyList<AttemptSummary>>(rpcResult) ?? [];
    }

    public async Task<ControlPlaneDebugClearMemoriesResult> ClearDebugMemoriesAsync(CancellationToken cancellationToken)
    {
        var rpcResult = await InvokeDiagnosticRpcAsync(
                "tianshu/debug/clear-memories",
                null,
                cancellationToken)
            .ConfigureAwait(false);
        return DeserializeStructuredValue<ControlPlaneDebugClearMemoriesResult>(rpcResult)
               ?? new ControlPlaneDebugClearMemoriesResult();
    }

    private static T? DeserializeStructuredValue<T>(StructuredValue value)
    {
        if (value.Kind == StructuredValueKind.Null)
        {
            return default;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(value.ToPlainObject(), StructuredJsonOptions);
        return System.Text.Json.JsonSerializer.Deserialize<T>(json, StructuredJsonOptions);
    }
}
