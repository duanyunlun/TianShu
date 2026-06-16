using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel;

/// <summary>
/// 一次会话编排决策，固定 Decide 阶段的输出。
/// Single session orchestration decision that pins the Decide output.
/// </summary>
public sealed record SessionOrchestrationDecision(
    OrchestratorDecision Decision,
    StageDefinition Stage);

/// <summary>
/// Observe 阶段看到的外部状态，用于在不扩大 SessionOrchestrator 职责的前提下进入上下文投影。
/// External state observed by the Observe phase and later projected without expanding SessionOrchestrator responsibilities.
/// </summary>
public sealed record SessionObservedState
{
    public SessionObservedState(
        IReadOnlyList<StageContextSegment>? workspaceStateSegments = null,
        IReadOnlyList<StageContextSegment>? artifactStateSegments = null,
        IReadOnlyList<StageContextSegment>? diagnosticStateSegments = null,
        IReadOnlyList<StageContextSegment>? memoryStateSegments = null,
        IReadOnlyList<StageContextSegment>? policyStateSegments = null,
        IReadOnlyList<string>? policyHits = null)
    {
        WorkspaceStateSegments = workspaceStateSegments ?? Array.Empty<StageContextSegment>();
        ArtifactStateSegments = artifactStateSegments ?? Array.Empty<StageContextSegment>();
        DiagnosticStateSegments = diagnosticStateSegments ?? Array.Empty<StageContextSegment>();
        MemoryStateSegments = memoryStateSegments ?? Array.Empty<StageContextSegment>();
        PolicyStateSegments = policyStateSegments ?? Array.Empty<StageContextSegment>();
        PolicyHits = ValidateOptionalIdentifierList(policyHits, nameof(policyHits));
    }

    public IReadOnlyList<StageContextSegment> WorkspaceStateSegments { get; }

    public IReadOnlyList<StageContextSegment> ArtifactStateSegments { get; }

    public IReadOnlyList<StageContextSegment> DiagnosticStateSegments { get; }

    public IReadOnlyList<StageContextSegment> MemoryStateSegments { get; }

    public IReadOnlyList<StageContextSegment> PolicyStateSegments { get; }

    public IReadOnlyList<string> PolicyHits { get; }

    public IReadOnlyList<StageContextSegment> ToContextSegments()
        => WorkspaceStateSegments
            .Concat(ArtifactStateSegments)
            .Concat(DiagnosticStateSegments)
            .Concat(MemoryStateSegments)
            .Concat(PolicyStateSegments)
            .ToArray();

    public static SessionObservedState Empty { get; } = new();

    private static IReadOnlyList<string> ValidateOptionalIdentifierList(IReadOnlyList<string>? values, string paramName)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = values
            .Select(value => IdentifierGuard.AgainstNullOrWhiteSpace(value, paramName))
            .ToArray();
        if (normalized.Length != normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new ArgumentException("Observe 策略命中列表不能包含重复项。", paramName);
        }

        return normalized;
    }
}

/// <summary>
/// 会话编排输入，表示一次核心循环 Observe 阶段看到的状态。
/// Session orchestration input that represents state observed by one core-loop iteration.
/// </summary>
public sealed record SessionOrchestrationInput
{
    /// <summary>
    /// 初始化会话编排输入。
    /// Initializes session orchestration input.
    /// </summary>
    public SessionOrchestrationInput(
        SessionId sessionId,
        ThreadId threadId,
        string correlationId,
        string? previousStageId = null,
        string? requestedStageId = null,
        IReadOnlyList<StageCheckpoint>? checkpoints = null,
        IReadOnlyList<StageContextSegment>? contextLedgerSegments = null,
        int? contextBudgetTokens = null,
        SessionObservedState? observedState = null)
    {
        if (contextBudgetTokens is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextBudgetTokens), "上下文预算不能为负。");
        }

        SessionId = sessionId;
        ThreadId = threadId;
        CorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? throw new ArgumentException("编排关联 id 不能为空。", nameof(correlationId))
            : correlationId.Trim();
        PreviousStageId = previousStageId;
        RequestedStageId = requestedStageId;
        Checkpoints = checkpoints ?? Array.Empty<StageCheckpoint>();
        ContextLedgerSegments = contextLedgerSegments ?? Array.Empty<StageContextSegment>();
        ContextBudgetTokens = contextBudgetTokens;
        ObservedState = observedState ?? SessionObservedState.Empty;
    }

    public SessionId SessionId { get; }

    public ThreadId ThreadId { get; }

    public string CorrelationId { get; }

    public string? PreviousStageId { get; }

    public string? RequestedStageId { get; }

    public IReadOnlyList<StageCheckpoint> Checkpoints { get; }

    public IReadOnlyList<StageContextSegment> ContextLedgerSegments { get; }

    public int? ContextBudgetTokens { get; }

    public SessionObservedState ObservedState { get; }
}

/// <summary>
/// 一次会话编排步骤，固定 Decide 与 Project Context 的输出。
/// Single session orchestration step that pins the Decide and Project Context output.
/// </summary>
public sealed record SessionOrchestrationStep(
    OrchestratorDecision Decision,
    StageContextPackage ContextPackage,
    StageDefinition Stage);
