using System.Text.Json;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.Diagnostics;

/// <summary>
/// 诊断事件包络工厂，把 typed stats 转换为统一 envelope。
/// Factory that wraps typed stats into diagnostic event envelopes.
/// </summary>
public static class DiagnosticEventEnvelopeFactory
{
    public static DiagnosticEventEnvelope FromStats<TStats>(
        string eventName,
        TStats stats,
        DiagnosticOperationContext? operation,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(stats);

        var payload = ToStructuredValue(stats, jsonOptions);
        return new DiagnosticEventEnvelope
        {
            EventName = eventName,
            Payload = payload,
            Operation = operation,
            Producer = operation?.Producer,
        };
    }

    public static DiagnosticEventEnvelope FromArtifact(DiagnosticArtifactManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new DiagnosticEventEnvelope
        {
            EventName = "diagnostics/artifact/written",
            Payload = ToStructuredValue(manifest),
            Operation = manifest.Operation,
            Producer = manifest.Operation?.Producer,
        };
    }

    private static StructuredValue ToStructuredValue<TValue>(TValue value, JsonSerializerOptions? jsonOptions = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(bytes);
        return StructuredValue.FromJsonElement(document.RootElement);
    }
}
