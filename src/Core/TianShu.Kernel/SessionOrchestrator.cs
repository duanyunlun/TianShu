using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel;

/// <summary>
/// 会话编排器，负责从会话状态选择下一 Stage 并生成可审计决策。
/// Session orchestrator that selects the next stage from session state and creates an auditable decision.
/// </summary>
public sealed class SessionOrchestrator
{
    private readonly IReadOnlyList<StageDefinition> stages;
    private readonly IReadOnlyDictionary<string, StageDefinition> stagesById;
    private readonly IReadOnlyDictionary<string, int> stageOrderById;

    /// <summary>
    /// 使用可见 Stage 定义初始化会话编排器。
    /// Initializes the session orchestrator with visible stage definitions.
    /// </summary>
    public SessionOrchestrator(IEnumerable<StageDefinition> stageDefinitions)
    {
        ArgumentNullException.ThrowIfNull(stageDefinitions);

        var orderedStages = new List<StageDefinition>();
        var mutableStagesById = new Dictionary<string, StageDefinition>(StringComparer.OrdinalIgnoreCase);
        var mutableStageOrderById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var stage in stageDefinitions)
        {
            ArgumentNullException.ThrowIfNull(stage);
            if (!mutableStagesById.TryAdd(stage.Id, stage))
            {
                throw new ArgumentException($"Stage `{stage.Id}` 被重复注册。", nameof(stageDefinitions));
            }

            mutableStageOrderById[stage.Id] = orderedStages.Count;
            orderedStages.Add(stage);
        }

        if (orderedStages.Count == 0)
        {
            throw new ArgumentException("SessionOrchestrator 至少需要一个 StageDefinition。", nameof(stageDefinitions));
        }

        stages = orderedStages;
        stagesById = mutableStagesById;
        stageOrderById = mutableStageOrderById;
    }

    /// <summary>
    /// 观察输入并选择下一次 Stage。
    /// Observes input and selects the next stage.
    /// </summary>
    public SessionOrchestrationDecision PlanNext(SessionOrchestrationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var previousStageId = ResolvePreviousStageId(input);
        var candidateStages = ResolveCandidateStages(previousStageId);
        var selectedStage = SelectStage(input, previousStageId, candidateStages);
        var reasonCode = ResolveReasonCode(input, previousStageId, selectedStage);
        var decisionId = BuildStableId("decision", input.CorrelationId, selectedStage.Id);

        var decision = new OrchestratorDecision(
            decisionId,
            input.SessionId,
            input.ThreadId,
            selectedStage.Id,
            candidateStages.Select(static stage => stage.Id).ToArray(),
            reasonCode,
            previousStageId: previousStageId,
            contextProjectionReason: selectedStage.ContextProjectionMode.ToString(),
            policyHits: input.ObservedState.PolicyHits);

        return new SessionOrchestrationDecision(decision, selectedStage);
    }

    private string? ResolvePreviousStageId(SessionOrchestrationInput input)
    {
        var previousStageId = Normalize(input.PreviousStageId)
                              ?? input.Checkpoints.LastOrDefault()?.StageId;
        if (previousStageId is not null && !stagesById.ContainsKey(previousStageId))
        {
            throw new ArgumentException($"上一 Stage `{previousStageId}` 未注册。", nameof(input));
        }

        return previousStageId;
    }

    private IReadOnlyList<StageDefinition> ResolveCandidateStages(string? previousStageId)
    {
        if (previousStageId is null)
        {
            return OrderStages(stages.Where(static stage => stage.AllowedPrevious.Count == 0)).ToArray();
        }

        var previousStage = stagesById[previousStageId];
        var candidateStages = previousStage.AllowedNext
            .Select(stageId => stagesById.TryGetValue(stageId, out var stage) ? stage : null)
            .OfType<StageDefinition>()
            .Concat(stages.Where(stage => stage.AllowedPrevious.Contains(previousStageId, StringComparer.OrdinalIgnoreCase)))
            .DistinctBy(static stage => stage.Id, StringComparer.OrdinalIgnoreCase);
        var orderedCandidates = OrderStages(candidateStages).ToArray();
        if (orderedCandidates.Length == 0)
        {
            throw new InvalidOperationException($"Stage `{previousStageId}` 没有可达的下一 Stage。");
        }

        return orderedCandidates;
    }

    private StageDefinition SelectStage(
        SessionOrchestrationInput input,
        string? previousStageId,
        IReadOnlyList<StageDefinition> candidateStages)
    {
        var requestedStageId = Normalize(input.RequestedStageId);
        if (requestedStageId is not null)
        {
            if (!stagesById.TryGetValue(requestedStageId, out var requestedStage))
            {
                throw new ArgumentException($"请求的 Stage `{requestedStageId}` 未注册。", nameof(input));
            }

            if (!candidateStages.Any(stage => string.Equals(stage.Id, requestedStage.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Stage `{previousStageId ?? "<entry>"}` 不能跳转到 `{requestedStage.Id}`。");
            }

            return requestedStage;
        }

        var suggestedStage = input.Checkpoints
            .LastOrDefault()?
            .NextStageSuggestions
            .Select(stageId => candidateStages.FirstOrDefault(candidate => string.Equals(candidate.Id, stageId, StringComparison.OrdinalIgnoreCase)))
            .OfType<StageDefinition>()
            .FirstOrDefault();

        return suggestedStage ?? candidateStages[0];
    }

    private string ResolveReasonCode(
        SessionOrchestrationInput input,
        string? previousStageId,
        StageDefinition selectedStage)
    {
        if (Normalize(input.RequestedStageId) is not null)
        {
            return "requested-stage";
        }

        if (input.Checkpoints.LastOrDefault()?.NextStageSuggestions.Contains(selectedStage.Id, StringComparer.OrdinalIgnoreCase) == true)
        {
            return "checkpoint-suggestion";
        }

        return previousStageId is null ? "session-entry" : "transition-default";
    }

    private IOrderedEnumerable<StageDefinition> OrderStages(IEnumerable<StageDefinition> values)
        => values
            .OrderBy(static stage => stage.LifecycleOrder)
            .ThenBy(stage => stageOrderById.TryGetValue(stage.Id, out var order) ? order : int.MaxValue);

    private static string BuildStableId(string prefix, string correlationId, string stageId)
        => $"{prefix}-{Normalize(correlationId) ?? "session"}-{stageId}";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
