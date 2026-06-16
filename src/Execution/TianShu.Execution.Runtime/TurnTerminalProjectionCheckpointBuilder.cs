using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Turn terminal projection checkpoint builder，负责从通用 turn 分派上下文生成终态投影 checkpoint。
/// Turn terminal projection checkpoint builder that creates terminal projection checkpoints from a generic turn dispatch context.
/// </summary>
public sealed class TurnTerminalProjectionCheckpointBuilder
{
    /// <summary>
    /// 默认终态 turn 投影 checkpoint builder 实例。
    /// Default terminal turn projection checkpoint builder instance.
    /// </summary>
    public static TurnTerminalProjectionCheckpointBuilder Instance { get; } = new();

    /// <summary>
    /// 根据终态 turn 字段生成 checkpoint，并把旧执行上下文细节保留在 Execution Runtime 内部。
    /// Creates a checkpoint from terminal turn fields while keeping legacy execution-context details inside Execution Runtime.
    /// </summary>
    public StageCheckpoint CompleteTerminalTurn(
        TurnExecutionDispatchContext dispatchContext,
        string turnId,
        string finalTurnStatus,
        DateTimeOffset? completedAt = null,
        string? effectiveUserText = null,
        string? finalAssistantText = null,
        string? reviewOutputText = null,
        string? reviewFailureMessage = null,
        string? errorMessage = null,
        string? errorDetails = null,
        StructuredValue? output = null,
        IReadOnlyList<ArtifactRef>? artifactRefs = null)
    {
        ArgumentNullException.ThrowIfNull(dispatchContext);

        return StageExecutorCheckpointBuilder.Instance.CompleteTerminalTurn(
            dispatchContext.RuntimeContext,
            turnId,
            finalTurnStatus,
            completedAt,
            effectiveUserText,
            finalAssistantText,
            reviewOutputText,
            reviewFailureMessage,
            errorMessage,
            errorDetails,
            output,
            artifactRefs);
    }
}
