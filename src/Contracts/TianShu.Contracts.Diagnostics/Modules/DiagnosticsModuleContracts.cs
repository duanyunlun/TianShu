using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Diagnostics;

/// <summary>
/// Diagnostics Module 统一入口，负责记录 Kernel、Runtime、Module 与 validation 事件。
/// Unified Diagnostics Module entry point for Kernel, Runtime, Module, and validation events.
/// </summary>
public interface IDiagnosticsModule : IModuleHealthCheck
{
    ValueTask<DiagnosticsModuleEmitResult> EmitAsync(
        DiagnosticsModuleEvent diagnosticEvent,
        CancellationToken cancellationToken);
}

/// <summary>
/// Diagnostics Module 事件分类，限定正式可记录的诊断来源。
/// Diagnostics Module event kind that constrains the official diagnostic sources.
/// </summary>
public enum DiagnosticsModuleEventKind
{
    Unspecified = 0,
    KernelTrace = 1,
    ExecutionRuntimeStep = 2,
    ModuleCall = 3,
    ValidationRejection = 4,
}

/// <summary>
/// Diagnostics Module 事件上下文，保留 Kernel run 和 RuntimeStep 来源。
/// Diagnostics Module event context preserving Kernel-run and RuntimeStep sources.
/// </summary>
public sealed record DiagnosticsModuleEventContext
{
    public DiagnosticsModuleEventContext(
        KernelRunId? kernelRunId = null,
        ExecutionId? executionId = null,
        string? runtimeStepId = null,
        CoreIntentId? sourceIntentId = null,
        StageGraphId? sourceGraphId = null,
        StageId? sourceStageId = null,
        KernelOperationId? sourceKernelOperationId = null,
        string? moduleId = null,
        string? capabilityId = null,
        MetadataBag? metadata = null)
    {
        KernelRunId = kernelRunId;
        ExecutionId = executionId;
        RuntimeStepId = Normalize(runtimeStepId);
        SourceIntentId = sourceIntentId;
        SourceGraphId = sourceGraphId;
        SourceStageId = sourceStageId;
        SourceKernelOperationId = sourceKernelOperationId;
        ModuleId = Normalize(moduleId);
        CapabilityId = Normalize(capabilityId);
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public KernelRunId? KernelRunId { get; }

    public ExecutionId? ExecutionId { get; }

    public string? RuntimeStepId { get; }

    public CoreIntentId? SourceIntentId { get; }

    public StageGraphId? SourceGraphId { get; }

    public StageId? SourceStageId { get; }

    public KernelOperationId? SourceKernelOperationId { get; }

    public string? ModuleId { get; }

    public string? CapabilityId { get; }

    public MetadataBag Metadata { get; }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Diagnostics Module typed event，payload 和 metadata 必须由实现层统一脱敏后写入 sink。
/// Diagnostics Module typed event; implementations must redact payload and metadata before writing to sinks.
/// </summary>
public sealed record DiagnosticsModuleEvent
{
    public DiagnosticsModuleEvent(
        DiagnosticsModuleEventKind kind,
        string eventName,
        StructuredValue payload,
        DiagnosticsModuleEventContext context,
        string? rejectionCode = null,
        string? failureMessage = null,
        bool isRetryable = false,
        DateTimeOffset? timestamp = null,
        MetadataBag? metadata = null)
    {
        if (kind is DiagnosticsModuleEventKind.Unspecified)
        {
            throw new ArgumentException("Diagnostics event kind must be specified.", nameof(kind));
        }

        if ((kind is DiagnosticsModuleEventKind.ExecutionRuntimeStep or DiagnosticsModuleEventKind.ModuleCall)
            && string.IsNullOrWhiteSpace(context?.RuntimeStepId))
        {
            throw new ArgumentException("Runtime and module call diagnostics must include RuntimeStepId.", nameof(context));
        }

        Kind = kind;
        EventName = IdentifierGuard.AgainstNullOrWhiteSpace(eventName, nameof(eventName));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        RejectionCode = rejectionCode;
        FailureMessage = failureMessage;
        IsRetryable = isRetryable;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public DiagnosticsModuleEventKind Kind { get; }

    public string EventName { get; }

    public StructuredValue Payload { get; }

    public DiagnosticsModuleEventContext Context { get; }

    public string? RejectionCode { get; }

    public string? FailureMessage { get; }

    public bool IsRetryable { get; }

    public DateTimeOffset Timestamp { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Diagnostics Module 写入结果，返回可追踪的 diagnostics / trace 引用。
/// Diagnostics Module emit result that returns traceable diagnostics and trace references.
/// </summary>
public sealed record DiagnosticsModuleEmitResult
{
    public DiagnosticsModuleEmitResult(
        bool success,
        string eventName,
        DiagnosticsModuleEventKind kind,
        string? diagnosticsRef = null,
        string? traceRef = null,
        string? degradedReason = null)
    {
        Success = success;
        EventName = IdentifierGuard.AgainstNullOrWhiteSpace(eventName, nameof(eventName));
        Kind = kind;
        DiagnosticsRef = diagnosticsRef;
        TraceRef = traceRef;
        DegradedReason = degradedReason;
    }

    public bool Success { get; }

    public string EventName { get; }

    public DiagnosticsModuleEventKind Kind { get; }

    public string? DiagnosticsRef { get; }

    public string? TraceRef { get; }

    public string? DegradedReason { get; }
}
