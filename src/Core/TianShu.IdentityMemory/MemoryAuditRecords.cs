using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// 记忆审计记录的统一创建入口，避免 service / provider / store 各自拼装审计字段。
/// Central factory for memory audit records so service, provider, and store code share one audit shape.
/// </summary>
public static class MemoryAuditRecords
{
    /// <summary>
    /// 创建通用记忆审计记录，原始结构化值只会进入 hash 摘要。
    /// Creates a generic memory audit record; raw structured values are summarized by hash only.
    /// </summary>
    public static TianShuMemoryAuditRecord Create(
        string operation,
        MemorySpaceId memorySpaceId,
        string key,
        string actor,
        string source,
        DateTimeOffset occurredAt,
        StructuredValue? value = null,
        decimal? confidence = null,
        MemoryMutationEffect effect = MemoryMutationEffect.None,
        IReadOnlyList<MemoryRiskReasonCode>? reasonCodes = null,
        string? reason = null,
        string? snippet = null,
        IReadOnlyDictionary<string, string>? metadata = null)
        => new(
            operation,
            memorySpaceId,
            key,
            actor,
            source,
            occurredAt,
            value,
            confidence,
            effect: effect,
            reasonCodes: reasonCodes,
            reason: reason,
            snippet: snippet,
            metadata: metadata);

    /// <summary>
    /// 从操作上下文与来源引用创建审计记录。
    /// Creates an audit record from an operation context and a source reference.
    /// </summary>
    public static TianShuMemoryAuditRecord FromContext(
        string operation,
        MemorySpaceId memorySpaceId,
        string key,
        MemoryOperationContext context,
        MemorySourceRef? source = null,
        string? sourceFallback = null,
        StructuredValue? value = null,
        decimal? confidence = null,
        MemoryMutationEffect effect = MemoryMutationEffect.None,
        IReadOnlyList<MemoryRiskReasonCode>? reasonCodes = null,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var auditSource = !string.IsNullOrWhiteSpace(source?.SourceId)
            ? source!.SourceId
            : !string.IsNullOrWhiteSpace(sourceFallback)
                ? sourceFallback!
                : !string.IsNullOrWhiteSpace(context.CorrelationId)
                    ? context.CorrelationId!
                    : context.SessionId?.Value ?? operation;
        return Create(
            operation,
            memorySpaceId,
            key,
            context.ActorId,
            auditSource,
            context.Timestamp,
            value,
            confidence,
            effect,
            reasonCodes,
            reason,
            source?.Snippet,
            source?.Metadata);
    }

    /// <summary>
    /// 创建 SupersedeMemory 的审计摘要；正式取代链写入由 provider/store 负责。
    /// Creates the audit summary for SupersedeMemory; provider/store code owns the formal supersede link write.
    /// </summary>
    public static TianShuMemoryAuditRecord Supersede(
        SupersedeMemory command,
        MemoryOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(command);

        return FromContext(
            "supersede_memory",
            command.MemorySpaceId,
            command.NewKey,
            context,
            command.Source,
            sourceFallback: "memory-supersede",
            value: command.NewValue,
            confidence: command.Confidence,
            effect: MemoryMutationEffect.Superseded,
            reasonCodes: [MemoryRiskReasonCode.ConflictsWithActiveFact],
            reason: command.Reason);
    }
}
