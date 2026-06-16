using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Diagnostics;

/// <summary>
/// 查询执行追踪。
/// Query that fetches an execution trace.
/// </summary>
public sealed record GetExecutionTrace(ExecutionTraceId TraceId);

/// <summary>
/// 查询执行尝试摘要列表。
/// Query that lists execution attempt summaries.
/// </summary>
public sealed record ListAttemptSummaries(ExecutionId ExecutionId);
