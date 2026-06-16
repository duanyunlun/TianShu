using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// 内存诊断事件接收器，供测试和本地组合使用。
/// In-memory diagnostic event sink for tests and local composition.
/// </summary>
public sealed class InMemoryDiagnosticEventSink : IDiagnosticEventSink
{
    private readonly object gate = new();
    private readonly List<DiagnosticEventEnvelope> events = [];

    public ValueTask EmitAsync(DiagnosticEventEnvelope diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            events.Add(diagnosticEvent);
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<DiagnosticEventEnvelope> Snapshot()
    {
        lock (gate)
        {
            return events.ToArray();
        }
    }
}
