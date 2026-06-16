using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// 将诊断事件 fan-out 到多个 sink，单个 sink 失败不应阻断后续 sink。
/// Fans out diagnostic events to multiple sinks without letting one sink block the rest.
/// </summary>
public sealed class CompositeDiagnosticEventSink(IReadOnlyList<IDiagnosticEventSink> sinks) : IDiagnosticEventSink
{
    private readonly IReadOnlyList<IDiagnosticEventSink> sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));

    public async ValueTask EmitAsync(DiagnosticEventEnvelope diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        foreach (var sink in sinks)
        {
            try
            {
                await sink.EmitAsync(diagnosticEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // 诊断输出端不能反向破坏业务路径；单个 sink 失败时继续尝试后续 sink。
                // Diagnostic sinks must not break the business path; continue after one sink fails.
            }
        }
    }
}
