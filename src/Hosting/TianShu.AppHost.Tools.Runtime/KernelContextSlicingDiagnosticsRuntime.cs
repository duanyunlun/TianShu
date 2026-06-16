using System.Text.Json;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;
using TianShu.Diagnostics;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Context slicing 诊断捕获运行时，统一发布上下文裁剪报告。
/// Runtime that emits context slicing diagnostic operations and report events.
/// </summary>
internal sealed class KernelContextSlicingDiagnosticsRuntime
{
    private readonly IDiagnosticEventSink diagnosticEventSink;
    private readonly IDiagnosticOperationScopeFactory diagnosticOperationScopeFactory;
    private readonly JsonSerializerOptions jsonOptions;

    public KernelContextSlicingDiagnosticsRuntime(
        IDiagnosticEventSink diagnosticEventSink,
        IDiagnosticOperationScopeFactory diagnosticOperationScopeFactory,
        JsonSerializerOptions jsonOptions)
    {
        this.diagnosticEventSink = diagnosticEventSink ?? throw new ArgumentNullException(nameof(diagnosticEventSink));
        this.diagnosticOperationScopeFactory = diagnosticOperationScopeFactory ?? throw new ArgumentNullException(nameof(diagnosticOperationScopeFactory));
        this.jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
    }

    public async Task EmitReportAsync(
        TurnOperationState state,
        TurnRequestContext context,
        ContextSlicingReport report,
        int requestSequence,
        string model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var runtimeReport = ContextSlicingRuntimeHelpers.WithRuntimeIdentity(
            report,
            state.ThreadId,
            state.TurnId,
            model,
            context.ModelProvider);
        var payload = ContextSlicingRuntimeHelpers.BuildDiagnosticPayload(
            runtimeReport,
            requestSequence,
            model,
            context.ModelProvider);

        await using var operation = diagnosticOperationScopeFactory.BeginOperation(new DiagnosticOperationStart
        {
            OperationName = "context_slicing_report",
            OperationKind = "turn.context_slicing",
            ThreadId = state.ThreadId,
            TurnId = state.TurnId,
            RequestSequence = requestSequence,
            Producer = nameof(KernelContextSlicingDiagnosticsRuntime),
        });
        var metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Context),
            ["status"] = StructuredValue.FromString("completed"),
            ["summary"] = StructuredValue.FromString($"context slicing request #{requestSequence}"),
        });
        await diagnosticEventSink.EmitAsync(
            DiagnosticEventEnvelopeFactory.FromStats(
                    ContextSlicingRuntimeHelpers.DiagnosticsNotificationMethod,
                    payload,
                    operation.Context,
                    jsonOptions)
                with
                {
                    Metadata = metadata,
                },
            cancellationToken).ConfigureAwait(false);
        await operation.CompleteAsync(new DiagnosticOperationCompletion(), cancellationToken).ConfigureAwait(false);
    }
}
