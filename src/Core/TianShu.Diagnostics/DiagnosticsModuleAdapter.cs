using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Diagnostics;

/// <summary>
/// Diagnostics Module 适配器，将模块事件统一脱敏后写入现有诊断 sink。
/// Diagnostics Module adapter that redacts module events before writing them to the existing diagnostic sink.
/// </summary>
public sealed class DiagnosticsModuleAdapter : IDiagnosticsModule
{
    private readonly IDiagnosticEventSink sink;
    private readonly IDiagnosticRedactor redactor;

    public DiagnosticsModuleAdapter(
        IDiagnosticEventSink sink,
        IDiagnosticRedactor? redactor = null,
        ModuleDescriptor? descriptor = null)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.redactor = redactor ?? new DefaultDiagnosticRedactor();
        Descriptor = descriptor ?? BuiltInModuleDescriptors.Diagnostics();
    }

    public ModuleDescriptor Descriptor { get; }

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var passed = Descriptor.Kind == ModuleKind.Diagnostics;
        return ValueTask.FromResult(new ModuleSmokeCheckResult(
            Descriptor.ModuleId,
            passed,
            passed ? ModuleHealthStatus.Healthy : ModuleHealthStatus.Degraded,
            passed ? null : "Diagnostics Module descriptor kind mismatch."));
    }

    public async ValueTask<DiagnosticsModuleEmitResult> EmitAsync(
        DiagnosticsModuleEvent diagnosticEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnosticsRef = CreateDiagnosticsRef(diagnosticEvent);
        var traceRef = CreateTraceRef(diagnosticEvent);
        var operation = CreateOperationContext(diagnosticEvent, diagnosticsRef, traceRef);

        var envelope = new DiagnosticEventEnvelope
        {
            EventName = diagnosticEvent.EventName,
            Payload = redactor.RedactStructuredValue(diagnosticEvent.Payload),
            Operation = operation,
            Timestamp = diagnosticEvent.Timestamp,
            Producer = Descriptor.ModuleId,
            Metadata = RedactMetadata(BuildEnvelopeMetadata(diagnosticEvent, diagnosticsRef, traceRef)),
        };

        try
        {
            await sink.EmitAsync(envelope, cancellationToken).ConfigureAwait(false);
            return new DiagnosticsModuleEmitResult(
                success: true,
                diagnosticEvent.EventName,
                diagnosticEvent.Kind,
                diagnosticsRef,
                traceRef);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DiagnosticsModuleEmitResult(
                success: false,
                diagnosticEvent.EventName,
                diagnosticEvent.Kind,
                diagnosticsRef,
                traceRef,
                redactor.RedactText("diagnostics_failure", ex.Message));
        }
    }

    private DiagnosticOperationContext CreateOperationContext(
        DiagnosticsModuleEvent diagnosticEvent,
        string diagnosticsRef,
        string traceRef)
    {
        var context = diagnosticEvent.Context;
        return new DiagnosticOperationContext
        {
            OperationId = context.RuntimeStepId
                          ?? context.SourceKernelOperationId?.Value
                          ?? context.ExecutionId?.Value
                          ?? context.KernelRunId?.Value
                          ?? $"{Descriptor.ModuleId}:{diagnosticEvent.EventName}",
            OperationName = diagnosticEvent.EventName,
            OperationKind = diagnosticEvent.Kind.ToString(),
            TraceId = traceRef,
            Producer = Descriptor.ModuleId,
            Metadata = RedactMetadata(BuildOperationMetadata(diagnosticEvent, diagnosticsRef, traceRef)),
        };
    }

    private MetadataBag BuildOperationMetadata(
        DiagnosticsModuleEvent diagnosticEvent,
        string diagnosticsRef,
        string traceRef)
    {
        var entries = CreateStandardMetadata(diagnosticEvent, diagnosticsRef, traceRef);
        Merge(entries, diagnosticEvent.Context.Metadata);
        return new MetadataBag(entries);
    }

    private MetadataBag BuildEnvelopeMetadata(
        DiagnosticsModuleEvent diagnosticEvent,
        string diagnosticsRef,
        string traceRef)
    {
        var entries = CreateStandardMetadata(diagnosticEvent, diagnosticsRef, traceRef);
        Merge(entries, diagnosticEvent.Context.Metadata);
        Merge(entries, diagnosticEvent.Metadata);

        if (!string.IsNullOrWhiteSpace(diagnosticEvent.RejectionCode))
        {
            entries["rejectionCode"] = StructuredValue.FromString(diagnosticEvent.RejectionCode);
        }

        if (!string.IsNullOrWhiteSpace(diagnosticEvent.FailureMessage))
        {
            entries["failureMessage"] = StructuredValue.FromString(redactor.RedactText("failureMessage", diagnosticEvent.FailureMessage));
        }

        entries["isRetryable"] = StructuredValue.FromBoolean(diagnosticEvent.IsRetryable);
        return new MetadataBag(entries);
    }

    private Dictionary<string, StructuredValue> CreateStandardMetadata(
        DiagnosticsModuleEvent diagnosticEvent,
        string diagnosticsRef,
        string traceRef)
    {
        var context = diagnosticEvent.Context;
        var entries = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["diagnosticModule"] = StructuredValue.FromString(Descriptor.ModuleId),
            ["diagnosticsEventKind"] = StructuredValue.FromString(diagnosticEvent.Kind.ToString()),
            ["diagnosticsRef"] = StructuredValue.FromString(diagnosticsRef),
            ["traceRef"] = StructuredValue.FromString(traceRef),
        };

        AddIfPresent(entries, "kernelRunId", context.KernelRunId?.Value);
        AddIfPresent(entries, "executionId", context.ExecutionId?.Value);
        AddIfPresent(entries, "runtimeStepId", context.RuntimeStepId);
        AddIfPresent(entries, "sourceIntentId", context.SourceIntentId?.Value);
        AddIfPresent(entries, "sourceGraphId", context.SourceGraphId?.Value);
        AddIfPresent(entries, "sourceStageId", context.SourceStageId?.Value);
        AddIfPresent(entries, "sourceKernelOperationId", context.SourceKernelOperationId?.Value);
        AddIfPresent(entries, "moduleId", context.ModuleId);
        AddIfPresent(entries, "capabilityId", context.CapabilityId);

        return entries;
    }

    private MetadataBag RedactMetadata(MetadataBag metadata)
        => new(metadata.Entries.ToDictionary(
            static pair => pair.Key,
            pair => redactor.IsSensitiveKey(pair.Key)
                ? StructuredValue.FromString("[REDACTED]")
                : redactor.RedactStructuredValue(pair.Value),
            StringComparer.Ordinal));

    private static void Merge(Dictionary<string, StructuredValue> target, MetadataBag metadata)
    {
        foreach (var entry in metadata.Entries)
        {
            target[entry.Key] = entry.Value;
        }
    }

    private static void AddIfPresent(Dictionary<string, StructuredValue> entries, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            entries[key] = StructuredValue.FromString(value);
        }
    }

    private static string CreateDiagnosticsRef(DiagnosticsModuleEvent diagnosticEvent)
        => CreateRef("diagnostics", diagnosticEvent);

    private static string CreateTraceRef(DiagnosticsModuleEvent diagnosticEvent)
        => CreateRef("trace", diagnosticEvent);

    private static string CreateRef(string scheme, DiagnosticsModuleEvent diagnosticEvent)
    {
        var context = diagnosticEvent.Context;
        var eventSegment = Uri.EscapeDataString(diagnosticEvent.EventName.Replace('/', '.'));

        if (context.ExecutionId is not null && !string.IsNullOrWhiteSpace(context.RuntimeStepId))
        {
            return $"{scheme}://execution/{context.ExecutionId.Value}/{context.RuntimeStepId}/{eventSegment}";
        }

        if (context.KernelRunId is not null)
        {
            return $"{scheme}://kernel/{context.KernelRunId.Value}/{eventSegment}";
        }

        return $"{scheme}://diagnostics/{diagnosticEvent.Kind.ToString().ToLowerInvariant()}/{eventSegment}";
    }
}
