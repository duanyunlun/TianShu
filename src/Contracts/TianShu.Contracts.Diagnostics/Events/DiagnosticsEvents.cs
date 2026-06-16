using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Diagnostics;

/// <summary>
/// 审计记录已发布事件。
/// Event emitted when an audit record has been published.
/// </summary>
public sealed record AuditRecordPublished(AuditRecordId AuditRecordId, ExecutionTraceId? ExecutionTraceId = null);
