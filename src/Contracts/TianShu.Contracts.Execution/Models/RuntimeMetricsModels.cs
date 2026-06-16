using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Execution;

/// <summary>
/// Token 使用量快照，必须显式标明是否可用、是否估算以及来源。
/// Token-usage snapshot that explicitly records availability, estimation, and source.
/// </summary>
public sealed record TokenUsageSnapshot(
    bool Available,
    string? MissingReason,
    bool Estimated,
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens,
    long? ReasoningOutputTokens,
    long? TotalTokens,
    string Source)
{
    public string Source { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Source, nameof(Source));
}

/// <summary>
/// Runtime 成本快照；只有真实 usage 与 price model 都具备时才可用。
/// Runtime cost snapshot; available only when real usage and a price model are both present.
/// </summary>
public sealed record RuntimeCostSnapshot(
    bool Available,
    string? MissingReason,
    decimal? EstimatedCost,
    string? Currency,
    string? PriceModelVersion);

/// <summary>
/// Runtime 执行指标事件，归因到 run/graph/stage/step/execution。
/// Runtime execution metrics event attributed to run/graph/stage/step/execution.
/// </summary>
public sealed record RuntimeMetricsEvent
{
    public RuntimeMetricsEvent(
        string eventId,
        string runId,
        string executionId,
        string planId,
        string graphId,
        string? stageId,
        string? stepId,
        string modelId,
        int attemptIndex,
        int? reviseRound,
        TokenUsageSnapshot tokenUsage,
        RuntimeCostSnapshot cost,
        int modelCallCount,
        TimeSpan latency,
        IReadOnlyList<string>? missingReasons = null)
    {
        EventId = IdentifierGuard.AgainstNullOrWhiteSpace(eventId, nameof(eventId));
        RunId = IdentifierGuard.AgainstNullOrWhiteSpace(runId, nameof(runId));
        ExecutionId = IdentifierGuard.AgainstNullOrWhiteSpace(executionId, nameof(executionId));
        PlanId = IdentifierGuard.AgainstNullOrWhiteSpace(planId, nameof(planId));
        GraphId = IdentifierGuard.AgainstNullOrWhiteSpace(graphId, nameof(graphId));
        StageId = stageId;
        StepId = stepId;
        ModelId = IdentifierGuard.AgainstNullOrWhiteSpace(modelId, nameof(modelId));
        AttemptIndex = attemptIndex <= 0 ? throw new ArgumentOutOfRangeException(nameof(attemptIndex), "AttemptIndex 必须大于 0。") : attemptIndex;
        ReviseRound = reviseRound;
        TokenUsage = tokenUsage ?? throw new ArgumentNullException(nameof(tokenUsage));
        Cost = cost ?? throw new ArgumentNullException(nameof(cost));
        ModelCallCount = modelCallCount < 0 ? throw new ArgumentOutOfRangeException(nameof(modelCallCount), "ModelCallCount 不能为负数。") : modelCallCount;
        Latency = latency < TimeSpan.Zero ? throw new ArgumentOutOfRangeException(nameof(latency), "Latency 不能为负数。") : latency;
        MissingReasons = missingReasons ?? Array.Empty<string>();
    }

    public string EventId { get; }

    public string RunId { get; }

    public string ExecutionId { get; }

    public string PlanId { get; }

    public string GraphId { get; }

    public string? StageId { get; }

    public string? StepId { get; }

    public string ModelId { get; }

    public int AttemptIndex { get; }

    public int? ReviseRound { get; }

    public TokenUsageSnapshot TokenUsage { get; }

    public RuntimeCostSnapshot Cost { get; }

    public int ModelCallCount { get; }

    public TimeSpan Latency { get; }

    public IReadOnlyList<string> MissingReasons { get; }
}

/// <summary>
/// 候选生成指标事件，归因到 task/candidate/attempt/revise。
/// Candidate-generation metrics event attributed to task/candidate/attempt/revise.
/// </summary>
public sealed record CandidateGenerationMetricsEvent
{
    public CandidateGenerationMetricsEvent(
        string eventId,
        string taskId,
        string candidateKind,
        int attemptIndex,
        int? reviseRound,
        string modelId,
        TokenUsageSnapshot tokenUsage,
        RuntimeCostSnapshot cost,
        int modelCallCount,
        TimeSpan latency,
        IReadOnlyList<string>? missingReasons = null)
    {
        EventId = IdentifierGuard.AgainstNullOrWhiteSpace(eventId, nameof(eventId));
        TaskId = IdentifierGuard.AgainstNullOrWhiteSpace(taskId, nameof(taskId));
        CandidateKind = IdentifierGuard.AgainstNullOrWhiteSpace(candidateKind, nameof(candidateKind));
        AttemptIndex = attemptIndex <= 0 ? throw new ArgumentOutOfRangeException(nameof(attemptIndex), "AttemptIndex 必须大于 0。") : attemptIndex;
        ReviseRound = reviseRound;
        ModelId = IdentifierGuard.AgainstNullOrWhiteSpace(modelId, nameof(modelId));
        TokenUsage = tokenUsage ?? throw new ArgumentNullException(nameof(tokenUsage));
        Cost = cost ?? throw new ArgumentNullException(nameof(cost));
        ModelCallCount = modelCallCount < 0 ? throw new ArgumentOutOfRangeException(nameof(modelCallCount), "ModelCallCount 不能为负数。") : modelCallCount;
        Latency = latency < TimeSpan.Zero ? throw new ArgumentOutOfRangeException(nameof(latency), "Latency 不能为负数。") : latency;
        MissingReasons = missingReasons ?? Array.Empty<string>();
    }

    public string EventId { get; }

    public string TaskId { get; }

    public string CandidateKind { get; }

    public int AttemptIndex { get; }

    public int? ReviseRound { get; }

    public string ModelId { get; }

    public TokenUsageSnapshot TokenUsage { get; }

    public RuntimeCostSnapshot Cost { get; }

    public int ModelCallCount { get; }

    public TimeSpan Latency { get; }

    public IReadOnlyList<string> MissingReasons { get; }
}

/// <summary>
/// Execution Runtime 指标事件接收器。
/// Sink for Execution Runtime metrics events.
/// </summary>
public interface IExecutionRuntimeMetricsSink
{
    ValueTask RecordAsync(RuntimeMetricsEvent metricsEvent, CancellationToken cancellationToken);
}
