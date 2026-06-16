using System.Text.Json;
using TianShu.Contracts.Diagnostics;
using TianShu.Diagnostics;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Provider request 诊断捕获运行时，统一写入 operation、payload artifact 与 context stats。
/// Runtime that captures provider request operations, payload artifacts, and context statistics.
/// </summary>
internal sealed class KernelProviderRequestDiagnosticsRuntime
{
    private readonly IDiagnosticEventSink diagnosticEventSink;
    private readonly IDiagnosticOperationScopeFactory diagnosticOperationScopeFactory;
    private readonly IDiagnosticArtifactWriter? providerRequestPayloadArtifactWriter;
    private readonly JsonSerializerOptions jsonOptions;

    public KernelProviderRequestDiagnosticsRuntime(
        IDiagnosticEventSink diagnosticEventSink,
        IDiagnosticOperationScopeFactory diagnosticOperationScopeFactory,
        IDiagnosticArtifactWriter? providerRequestPayloadArtifactWriter,
        JsonSerializerOptions jsonOptions)
    {
        this.diagnosticEventSink = diagnosticEventSink ?? throw new ArgumentNullException(nameof(diagnosticEventSink));
        this.diagnosticOperationScopeFactory = diagnosticOperationScopeFactory ?? throw new ArgumentNullException(nameof(diagnosticOperationScopeFactory));
        this.providerRequestPayloadArtifactWriter = providerRequestPayloadArtifactWriter;
        this.jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
    }

    public async Task CaptureAsync(
        TurnOperationState state,
        IReadOnlyDictionary<string, object?> payload,
        int requestSequence,
        string? model,
        string? provider,
        string artifactTransport,
        string statsTransport,
        string? inputPropertyName,
        string requestJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactTransport);
        ArgumentException.ThrowIfNullOrWhiteSpace(statsTransport);
        ArgumentNullException.ThrowIfNull(requestJson);

        await using var diagnosticOperation = diagnosticOperationScopeFactory.BeginOperation(
            ProviderRequestDiagnosticsCapture.BuildOperationStart(
                state.ThreadId,
                state.TurnId,
                requestSequence,
                nameof(KernelProviderRequestDiagnosticsRuntime)));
        var payloadArtifact = await ProviderRequestDiagnosticsCapture.WritePayloadArtifactAsync(
            providerRequestPayloadArtifactWriter,
            state.TurnId,
            requestSequence,
            artifactTransport,
            requestJson,
            diagnosticOperation.Context,
            cancellationToken).ConfigureAwait(false);
        var providerStats = ProviderRequestDiagnosticsCapture.BuildContextStats(
            payload,
            requestSequence,
            model,
            provider,
            statsTransport,
            inputPropertyName,
            payloadArtifact,
            diagnosticOperation.Context,
            jsonOptions);
        await ProviderRequestDiagnosticsCapture.EmitContextStatsAsync(
                diagnosticEventSink,
                providerStats,
                diagnosticOperation.Context,
                jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        await diagnosticOperation.CompleteAsync(new DiagnosticOperationCompletion(), cancellationToken).ConfigureAwait(false);
    }
}
