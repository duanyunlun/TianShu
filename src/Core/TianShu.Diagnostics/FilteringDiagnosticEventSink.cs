using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// 按诊断采集策略过滤事件的 sink 装饰器。
/// Event sink decorator that filters events by diagnostic collection policy.
/// </summary>
public sealed class FilteringDiagnosticEventSink(
    IDiagnosticEventSink inner,
    IDiagnosticCollectionPolicy policy,
    Func<DiagnosticEventEnvelope, string?>? resolveModuleName = null,
    Func<DiagnosticEventEnvelope, DiagnosticCollectionLevel>? resolveRequiredLevel = null) : IDiagnosticEventSink
{
    public ValueTask EmitAsync(DiagnosticEventEnvelope diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        var requiredLevel = resolveRequiredLevel?.Invoke(diagnosticEvent) ?? DiagnosticCollectionLevel.Stats;
        var moduleName = resolveModuleName?.Invoke(diagnosticEvent);
        var decision = policy.ShouldCollect(
            diagnosticEvent.EventName,
            moduleName,
            requiredLevel,
            diagnosticEvent.Operation,
            diagnosticEvent.Metadata);

        return decision.ShouldCollect
            ? inner.EmitAsync(diagnosticEvent, cancellationToken)
            : ValueTask.CompletedTask;
    }
}
