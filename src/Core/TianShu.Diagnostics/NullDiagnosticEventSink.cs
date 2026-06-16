using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// 空诊断事件 sink，用于没有启用输出端时保持 contracts 入口稳定。
/// Null diagnostic event sink used when no output sink is enabled.
/// </summary>
public sealed class NullDiagnosticEventSink : IDiagnosticEventSink
{
    public ValueTask EmitAsync(DiagnosticEventEnvelope diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        return ValueTask.CompletedTask;
    }
}
