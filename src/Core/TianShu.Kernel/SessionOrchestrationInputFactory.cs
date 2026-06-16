using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel;

/// <summary>
/// Session orchestration input 工厂，集中固定 core-loop 入口输入的 Kernel 规则。
/// Session orchestration input factory that centralizes Kernel rules for core-loop entry input.
/// </summary>
public static class SessionOrchestrationInputFactory
{
    /// <summary>
    /// 创建会话编排输入，并在请求非 default Stage 且无上一 Stage 时补齐 default fallback。
    /// Creates session orchestration input and fills the default fallback when a non-default Stage is requested without a previous Stage.
    /// </summary>
    public static SessionOrchestrationInput Create(
        string normalizedThreadId,
        string correlationId,
        string? currentStageId = null,
        string? latestCheckpointStageId = null,
        string? requestedStageId = null,
        IReadOnlyList<StageCheckpoint>? checkpoints = null,
        IReadOnlyList<StageContextSegment>? contextLedgerSegments = null,
        SessionObservedState? observedState = null)
    {
        var threadId = NormalizeRequired(normalizedThreadId, nameof(normalizedThreadId));
        var previousStageId = ResolvePreviousStageId(
            currentStageId,
            latestCheckpointStageId,
            requestedStageId);

        return new SessionOrchestrationInput(
            new SessionId(threadId),
            new ThreadId(threadId),
            correlationId,
            previousStageId: previousStageId,
            requestedStageId: Normalize(requestedStageId),
            checkpoints: checkpoints,
            contextLedgerSegments: contextLedgerSegments,
            observedState: observedState);
    }

    /// <summary>
    /// 解析上一 Stage，并在无上一 Stage 的非 default 显式请求前建立 default 起点。
    /// Resolves the previous Stage and establishes the default starting point before explicit non-default requests.
    /// </summary>
    public static string? ResolvePreviousStageId(
        string? currentStageId,
        string? latestCheckpointStageId,
        string? requestedStageId)
    {
        var previousStageId = Normalize(currentStageId)
                              ?? Normalize(latestCheckpointStageId);
        var normalizedRequestedStageId = Normalize(requestedStageId);
        if (previousStageId is null
            && normalizedRequestedStageId is not null
            && !string.Equals(normalizedRequestedStageId, BuiltInStageDefinitions.Default, StringComparison.OrdinalIgnoreCase))
        {
            return BuiltInStageDefinitions.Default;
        }

        return previousStageId;
    }

    private static string NormalizeRequired(string value, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("线程标识不能为空。", paramName)
            : value.Trim();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
