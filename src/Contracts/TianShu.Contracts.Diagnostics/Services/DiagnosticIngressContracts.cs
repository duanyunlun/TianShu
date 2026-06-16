using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Diagnostics;

/// <summary>
/// 诊断事件发布入口，所有模块通过它发布 typed 诊断事件。
/// Diagnostic event sink used by every module to publish typed diagnostic events.
/// </summary>
public interface IDiagnosticEventSink
{
    ValueTask EmitAsync(DiagnosticEventEnvelope diagnosticEvent, CancellationToken cancellationToken);
}

/// <summary>
/// 诊断产物写入入口，用于写入已脱敏的可复查材料。
/// Diagnostic artifact writer used to persist sanitized review artifacts.
/// </summary>
public interface IDiagnosticArtifactWriter
{
    ValueTask<DiagnosticArtifactManifest> WriteAsync(DiagnosticArtifactWriteRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 诊断操作 scope 工厂，统一生成 operation correlation。
/// Factory that creates diagnostic operation scopes and correlation context.
/// </summary>
public interface IDiagnosticOperationScopeFactory
{
    IDiagnosticOperationScope BeginOperation(DiagnosticOperationStart operationStart);
}

/// <summary>
/// 诊断操作 scope。
/// Diagnostic operation scope.
/// </summary>
public interface IDiagnosticOperationScope : IAsyncDisposable
{
    DiagnosticOperationContext Context { get; }

    ValueTask CompleteAsync(DiagnosticOperationCompletion completion, CancellationToken cancellationToken);

    ValueTask FailAsync(DiagnosticOperationFailure failure, CancellationToken cancellationToken);
}

/// <summary>
/// 诊断统计构建器，把模块输入转换为 typed stats。
/// Diagnostic stats builder that maps module input into typed statistics.
/// </summary>
public interface IDiagnosticStatsBuilder<in TInput, out TStats>
{
    TStats Build(TInput input, DiagnosticOperationContext context);
}

/// <summary>
/// 诊断脱敏入口，统一判断和处理 secret-like 内容。
/// Diagnostic redactor that centralizes secret-like filtering.
/// </summary>
public interface IDiagnosticRedactor
{
    bool IsSensitiveKey(string key);

    string RedactText(string? key, string value);

    StructuredValue RedactStructuredValue(StructuredValue value);
}

/// <summary>
/// 诊断采集策略，用于按模块、事件、级别和采样率控制输出。
/// Diagnostic collection policy that controls output by module, event, level, and sampling rate.
/// </summary>
public interface IDiagnosticCollectionPolicy
{
    DiagnosticCollectionDecision ShouldCollect(
        string eventName,
        string? moduleName,
        DiagnosticCollectionLevel requiredLevel,
        DiagnosticOperationContext? operation,
        MetadataBag metadata);

    bool ShouldWriteArtifact(string artifactKind, DiagnosticOperationContext? operation, MetadataBag metadata);

    bool ShouldWriteArtifact(
        string artifactKind,
        long contentBytes,
        DiagnosticOperationContext? operation,
        MetadataBag metadata);
}

/// <summary>
/// 诊断事件包络。
/// Diagnostic event envelope.
/// </summary>
public sealed record DiagnosticEventEnvelope
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string EventName { get; init; }

    public required StructuredValue Payload { get; init; }

    public DiagnosticOperationContext? Operation { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string? Producer { get; init; }

    public MetadataBag Metadata { get; init; } = MetadataBag.Empty;
}

/// <summary>
/// 诊断操作启动请求。
/// Diagnostic operation start request.
/// </summary>
public sealed record DiagnosticOperationStart
{
    public required string OperationName { get; init; }

    public required string OperationKind { get; init; }

    public string? TraceId { get; init; }

    public string? ThreadId { get; init; }

    public string? TurnId { get; init; }

    public int? RequestSequence { get; init; }

    public string? ParentOperationId { get; init; }

    public string? Producer { get; init; }

    public MetadataBag Metadata { get; init; } = MetadataBag.Empty;
}

/// <summary>
/// 诊断操作上下文。
/// Diagnostic operation context.
/// </summary>
public sealed record DiagnosticOperationContext
{
    public required string OperationId { get; init; }

    public required string OperationName { get; init; }

    public required string OperationKind { get; init; }

    public string? TraceId { get; init; }

    public string? ThreadId { get; init; }

    public string? TurnId { get; init; }

    public int? RequestSequence { get; init; }

    public string? ParentOperationId { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? Producer { get; init; }

    public MetadataBag Metadata { get; init; } = MetadataBag.Empty;
}

/// <summary>
/// 诊断操作完成信息。
/// Diagnostic operation completion data.
/// </summary>
public sealed record DiagnosticOperationCompletion
{
    public DateTimeOffset? CompletedAt { get; init; }

    public string Status { get; init; } = "completed";

    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();

    public MetadataBag Metadata { get; init; } = MetadataBag.Empty;
}

/// <summary>
/// 诊断操作失败信息。
/// Diagnostic operation failure data.
/// </summary>
public sealed record DiagnosticOperationFailure
{
    public DateTimeOffset? CompletedAt { get; init; }

    public required string FailureKind { get; init; }

    public string? RedactedMessage { get; init; }

    public bool Retryable { get; init; }

    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();

    public MetadataBag Metadata { get; init; } = MetadataBag.Empty;
}

/// <summary>
/// 诊断产物写入请求。
/// Diagnostic artifact write request.
/// </summary>
public sealed record DiagnosticArtifactWriteRequest
{
    public required string ArtifactKind { get; init; }

    public required string FileName { get; init; }

    public required string MediaType { get; init; }

    public required string Content { get; init; }

    public string? SourceEventName { get; init; }

    public DiagnosticOperationContext? Operation { get; init; }

    public bool ContentAlreadySanitized { get; init; }

    public MetadataBag Metadata { get; init; } = MetadataBag.Empty;
}

/// <summary>
/// 诊断产物 manifest。
/// Diagnostic artifact manifest.
/// </summary>
public sealed record DiagnosticArtifactManifest
{
    public required string ArtifactId { get; init; }

    public required string ArtifactKind { get; init; }

    public required string FileName { get; init; }

    public required string RelativePath { get; init; }

    public required string MediaType { get; init; }

    public required string RedactionStatus { get; init; }

    public required string Sha256 { get; init; }

    public required long Bytes { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? SourceEventName { get; init; }

    public DiagnosticOperationContext? Operation { get; init; }

    public MetadataBag Metadata { get; init; } = MetadataBag.Empty;
}
