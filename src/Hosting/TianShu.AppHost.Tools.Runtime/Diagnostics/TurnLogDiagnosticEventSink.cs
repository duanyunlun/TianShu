using TianShu.Contracts.Diagnostics;

namespace TianShu.AppHost.Tools.Runtime.Diagnostics;

/// <summary>
/// 将统一 Diagnostics 事件桥接到现有 turn log 与 runtime notification。
/// Bridges unified Diagnostics events to the existing turn log and runtime notification surfaces.
/// </summary>
public sealed class TurnLogDiagnosticEventSink(
    Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync,
    Func<string, object, CancellationToken, Task> writeNotificationAsync) : IDiagnosticEventSink
{
    public async ValueTask EmitAsync(DiagnosticEventEnvelope diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        if (!string.IsNullOrWhiteSpace(diagnosticEvent.Operation?.ThreadId)
            && !string.IsNullOrWhiteSpace(diagnosticEvent.Operation?.TurnId))
        {
            await persistTurnLogAsync(
                    diagnosticEvent.Operation.ThreadId,
                    diagnosticEvent.Operation.TurnId,
                    diagnosticEvent.EventName,
                    ResolveStatus(diagnosticEvent),
                    ResolveSummary(diagnosticEvent),
                    diagnosticEvent,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await writeNotificationAsync(
                diagnosticEvent.EventName,
                diagnosticEvent.Payload.ToPlainObject() ?? new { },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ResolveStatus(DiagnosticEventEnvelope diagnosticEvent)
        => diagnosticEvent.Metadata.TryGetValue("status", out var status) && status is not null
            ? status.StringValue ?? "info"
            : "info";

    private static string? ResolveSummary(DiagnosticEventEnvelope diagnosticEvent)
        => diagnosticEvent.Metadata.TryGetValue("summary", out var summary) && summary is not null
            ? summary.StringValue
            : diagnosticEvent.EventName;
}
