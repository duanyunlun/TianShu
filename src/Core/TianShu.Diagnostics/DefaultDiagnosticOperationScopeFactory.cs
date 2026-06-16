using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.Diagnostics;

/// <summary>
/// 默认诊断操作 scope 工厂。
/// Default diagnostic operation scope factory.
/// </summary>
public sealed class DefaultDiagnosticOperationScopeFactory : IDiagnosticOperationScopeFactory
{
    private readonly IDiagnosticEventSink? eventSink;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<string> operationIdFactory;

    public DefaultDiagnosticOperationScopeFactory(
        IDiagnosticEventSink? eventSink = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<string>? operationIdFactory = null)
    {
        this.eventSink = eventSink;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.operationIdFactory = operationIdFactory ?? (() => $"diag-op-{Guid.NewGuid():N}");
    }

    public IDiagnosticOperationScope BeginOperation(DiagnosticOperationStart operationStart)
    {
        ArgumentNullException.ThrowIfNull(operationStart);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationStart.OperationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationStart.OperationKind);

        var context = new DiagnosticOperationContext
        {
            OperationId = operationIdFactory(),
            OperationName = operationStart.OperationName,
            OperationKind = operationStart.OperationKind,
            TraceId = operationStart.TraceId,
            ThreadId = operationStart.ThreadId,
            TurnId = operationStart.TurnId,
            RequestSequence = operationStart.RequestSequence,
            ParentOperationId = operationStart.ParentOperationId,
            StartedAt = utcNow(),
            Producer = operationStart.Producer,
            Metadata = operationStart.Metadata,
        };

        return new DefaultDiagnosticOperationScope(context, eventSink, utcNow);
    }

    private sealed class DefaultDiagnosticOperationScope(
        DiagnosticOperationContext context,
        IDiagnosticEventSink? eventSink,
        Func<DateTimeOffset> utcNow) : IDiagnosticOperationScope
    {
        private bool completed;

        public DiagnosticOperationContext Context { get; } = context;

        public async ValueTask CompleteAsync(DiagnosticOperationCompletion completion, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(completion);
            if (completed)
            {
                return;
            }

            completed = true;
            if (eventSink is null)
            {
                return;
            }

            var completedAt = completion.CompletedAt ?? utcNow();
            var payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["operationId"] = Context.OperationId,
                ["operationName"] = Context.OperationName,
                ["operationKind"] = Context.OperationKind,
                ["status"] = completion.Status,
                ["startedAt"] = Context.StartedAt.ToString("O"),
                ["completedAt"] = completedAt.ToString("O"),
                ["durationMs"] = Math.Max(0, (long)(completedAt - Context.StartedAt).TotalMilliseconds),
                ["reasonCodes"] = completion.ReasonCodes,
            });
            await eventSink.EmitAsync(new DiagnosticEventEnvelope
            {
                EventName = "diagnostics/operation/completed",
                Payload = payload,
                Operation = Context,
                Producer = Context.Producer,
                Metadata = completion.Metadata,
                Timestamp = completedAt,
            }, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask FailAsync(DiagnosticOperationFailure failure, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(failure);
            if (completed)
            {
                return;
            }

            completed = true;
            if (eventSink is null)
            {
                return;
            }

            var completedAt = failure.CompletedAt ?? utcNow();
            var payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["operationId"] = Context.OperationId,
                ["operationName"] = Context.OperationName,
                ["operationKind"] = Context.OperationKind,
                ["status"] = "failed",
                ["failureKind"] = failure.FailureKind,
                ["redactedMessage"] = failure.RedactedMessage,
                ["retryable"] = failure.Retryable,
                ["startedAt"] = Context.StartedAt.ToString("O"),
                ["completedAt"] = completedAt.ToString("O"),
                ["durationMs"] = Math.Max(0, (long)(completedAt - Context.StartedAt).TotalMilliseconds),
                ["reasonCodes"] = failure.ReasonCodes,
            });
            await eventSink.EmitAsync(new DiagnosticEventEnvelope
            {
                EventName = "diagnostics/operation/failed",
                Payload = payload,
                Operation = Context,
                Producer = Context.Producer,
                Metadata = failure.Metadata,
                Timestamp = completedAt,
            }, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
