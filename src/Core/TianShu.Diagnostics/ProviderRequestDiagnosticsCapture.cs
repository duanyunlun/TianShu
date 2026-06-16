using System.Text.Json;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.Diagnostics;

/// <summary>
/// Provider request capture 的共享封装，避免不同运行路径维护分叉诊断语义。
/// </summary>
public static class ProviderRequestDiagnosticsCapture
{
    public static DiagnosticOperationStart BuildOperationStart(
        string? threadId,
        string? turnId,
        int requestSequence,
        string producer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        return new DiagnosticOperationStart
        {
            OperationName = "provider_request_context_stats",
            OperationKind = "turn.provider_request",
            ThreadId = threadId,
            TurnId = turnId,
            RequestSequence = requestSequence,
            Producer = producer,
        };
    }

    public static async Task EmitContextStatsAsync(
        IDiagnosticEventSink diagnosticEventSink,
        object payload,
        DiagnosticOperationContext operationContext,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEventSink);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(operationContext);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        var metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Provider),
            ["status"] = StructuredValue.FromString("info"),
            ["summary"] = StructuredValue.FromString("provider request context stats"),
        });
        await diagnosticEventSink.EmitAsync(
            DiagnosticEventEnvelopeFactory.FromStats(
                    DiagnosticStatisticsEventNames.ProviderRequestContextStats,
                    payload,
                    operationContext,
                    jsonOptions)
                with
                {
                    Metadata = metadata,
                },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<DiagnosticArtifactManifest?> WritePayloadArtifactAsync(
        IDiagnosticArtifactWriter? providerRequestPayloadArtifactWriter,
        string? turnId,
        int requestSequence,
        string transport,
        string requestJson,
        DiagnosticOperationContext operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentNullException.ThrowIfNull(requestJson);
        ArgumentNullException.ThrowIfNull(operation);

        if (providerRequestPayloadArtifactWriter is null)
        {
            return null;
        }

        try
        {
            return await providerRequestPayloadArtifactWriter.WriteAsync(
                new DiagnosticArtifactWriteRequest
                {
                    ArtifactKind = "provider_request_payload",
                    FileName = $"provider-request-{SanitizeArtifactToken(turnId)}-{requestSequence}-{SanitizeArtifactToken(transport)}.sanitized.json",
                    MediaType = "application/json",
                    Content = requestJson,
                    SourceEventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
                    Operation = operation,
                    Metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Provider),
                    }),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public static ProviderRequestContextStats BuildContextStats(
        IReadOnlyDictionary<string, object?> payload,
        int requestSequence,
        string? model,
        string? provider,
        string transport,
        string? inputPropertyName,
        DiagnosticArtifactManifest? payloadArtifact,
        DiagnosticOperationContext operationContext,
        JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentNullException.ThrowIfNull(operationContext);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        var builder = new ProviderRequestContextStatsDiagnosticBuilder(
            new ProviderRequestContextStatsDiagnosticBuilderOptions
            {
                Model = model,
                Provider = provider,
                Transport = transport,
                InputPropertyName = inputPropertyName,
                RequestSequenceFallback = requestSequence,
                PayloadArtifact = payloadArtifact,
            },
            jsonOptions);
        return builder.Build(payload, operationContext);
    }

    private static string SanitizeArtifactToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var chars = value.Trim().Select(static character =>
            char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-').ToArray();
        var sanitized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
