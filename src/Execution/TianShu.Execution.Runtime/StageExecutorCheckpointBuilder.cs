using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Stage Executor 单次 turn 完成结果，表达执行层生成 checkpoint 所需的终态信息。
/// Stage executor turn completion result carrying terminal data needed to create a checkpoint.
/// </summary>
public sealed record StageExecutorTurnCompletion
{
    /// <summary>
    /// 初始化 Stage Executor turn 完成结果。
    /// Initializes the stage executor turn completion result.
    /// </summary>
    public StageExecutorTurnCompletion(
        string turnId,
        string finalTurnStatus,
        StageExecutionState state,
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
        TurnId = string.IsNullOrWhiteSpace(turnId)
            ? throw new ArgumentException("Stage Executor turn id 不能为空。", nameof(turnId))
            : turnId.Trim();
        FinalTurnStatus = string.IsNullOrWhiteSpace(finalTurnStatus)
            ? throw new ArgumentException("Stage Executor turn 终态不能为空。", nameof(finalTurnStatus))
            : finalTurnStatus.Trim();
        State = state;
        CompletedAt = completedAt;
        EffectiveUserText = Normalize(effectiveUserText);
        FinalAssistantText = Normalize(finalAssistantText);
        ReviewOutputText = Normalize(reviewOutputText);
        ReviewFailureMessage = Normalize(reviewFailureMessage);
        ErrorMessage = Normalize(errorMessage);
        ErrorDetails = Normalize(errorDetails);
        Output = output;
        ArtifactRefs = artifactRefs ?? Array.Empty<ArtifactRef>();
    }

    public string TurnId { get; }

    public string FinalTurnStatus { get; }

    public StageExecutionState State { get; }

    public DateTimeOffset? CompletedAt { get; }

    public string? EffectiveUserText { get; }

    public string? FinalAssistantText { get; }

    public string? ReviewOutputText { get; }

    public string? ReviewFailureMessage { get; }

    public string? ErrorMessage { get; }

    public string? ErrorDetails { get; }

    public StructuredValue? Output { get; }

    public IReadOnlyList<ArtifactRef> ArtifactRefs { get; }

    /// <summary>
    /// 从 AppHost 终态字符串解析 Stage 执行状态。
    /// Resolves a stage execution state from the AppHost terminal turn status.
    /// </summary>
    public static StageExecutionState ResolveState(string finalTurnStatus)
        => Normalize(finalTurnStatus)?.ToLowerInvariant() switch
        {
            "completed" => StageExecutionState.Completed,
            "failed" => StageExecutionState.Failed,
            "interrupted" => StageExecutionState.Blocked,
            _ => StageExecutionState.Failed,
        };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Stage Executor checkpoint 完成器，统一生成 Stage 执行后的可持久化 checkpoint。
/// Stage executor checkpoint completer that creates persistable checkpoints after a stage execution.
/// </summary>
public sealed class StageExecutorCheckpointBuilder
{
    /// <summary>
    /// 默认 checkpoint 完成器实例。
    /// Default checkpoint completer instance.
    /// </summary>
    public static StageExecutorCheckpointBuilder Instance { get; } = new();

    /// <summary>
    /// 根据执行上下文和 turn 完成结果生成 checkpoint。
    /// Creates a checkpoint from the execution context and terminal turn result.
    /// </summary>
    public StageCheckpoint Complete(
        StageExecutorRuntimeContext context,
        StageExecutorTurnCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completion);

        var completedAt = completion.CompletedAt;
        if (completedAt.HasValue && completedAt.Value < context.StartedAt)
        {
            completedAt = context.StartedAt;
        }

        return new StageCheckpoint(
            BuildCheckpointId(context, completion),
            context.StageId,
            completion.State,
            context.StartedAt,
            completedAt,
            summary: BuildSummary(completion),
            output: completion.Output,
            artifactRefs: completion.ArtifactRefs,
            modelRouteSetId: context.ModelRouteSetId,
            modelRouteKind: context.ModelRouteKind,
            diagnostics: StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["turnId"] = completion.TurnId,
                ["status"] = completion.FinalTurnStatus,
                ["decisionId"] = context.DecisionId,
                ["contextPackageId"] = context.ContextPackageId,
                ["executionRequestId"] = context.ExecutionId,
                ["executorBinding"] = context.ExecutorBinding,
                ["executorImplementationId"] = context.ExecutorImplementationId,
                ["executorDispatchKind"] = context.ExecutorDispatchKind?.ToString(),
                ["modelRouteDiagnosticsCorrelationId"] = context.ModelRouteDiagnosticsCorrelationId,
                ["errorMessage"] = completion.ErrorMessage,
                ["errorDetails"] = completion.ErrorDetails,
            }));
    }

    /// <summary>
    /// 根据终态 turn 字段生成 checkpoint，并在执行层内完成 completion 投影。
    /// Creates a checkpoint from terminal turn fields and keeps completion projection inside execution runtime.
    /// </summary>
    public StageCheckpoint CompleteTerminalTurn(
        StageExecutorRuntimeContext context,
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
        => Complete(
            context,
            new StageExecutorTurnCompletion(
                turnId,
                finalTurnStatus,
                StageExecutorTurnCompletion.ResolveState(finalTurnStatus),
                completedAt,
                effectiveUserText,
                finalAssistantText,
                reviewOutputText,
                reviewFailureMessage,
                errorMessage,
                errorDetails,
                output,
                artifactRefs));

    private static string BuildCheckpointId(
        StageExecutorRuntimeContext context,
        StageExecutorTurnCompletion completion)
        => $"checkpoint-{context.DecisionId ?? completion.TurnId}-{context.StageId}";

    private static string? BuildSummary(StageExecutorTurnCompletion completion)
    {
        var summary = completion.FinalAssistantText
                      ?? completion.ReviewOutputText
                      ?? completion.ReviewFailureMessage
                      ?? completion.ErrorMessage
                      ?? completion.EffectiveUserText
                      ?? completion.FinalTurnStatus;
        return TruncateSummary(summary);
    }

    private static string? TruncateSummary(string? value)
    {
        const int MaxSummaryLength = 2048;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= MaxSummaryLength
            ? normalized
            : normalized[..MaxSummaryLength];
    }
}
