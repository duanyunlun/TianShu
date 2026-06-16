using TianShu.Contracts.Catalog;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Orchestration;

/// <summary>
/// Stage 上下文投影模式，描述编排层如何向目标 Stage 传递会话上下文。
/// Stage context projection mode that describes how the orchestrator passes session context to a target stage.
/// </summary>
public enum StageContextProjectionMode
{
    Summary = 0,
    SelectedSegments = 1,
    FullWithinBudget = 2,
    ReferencesOnly = 3,
}

/// <summary>
/// Stage 执行状态，表达一次 Stage 生命周期节点的当前结果。
/// Stage execution state that describes the current result of a stage lifecycle node.
/// </summary>
public enum StageExecutionState
{
    Pending = 0,
    Running = 1,
    Blocked = 2,
    Completed = 3,
    Failed = 4,
    Skipped = 5,
}

/// <summary>
/// Stage 定义，声明核心循环可进入的生命周期节点。
/// Stage definition that declares a lifecycle node available to the core loop.
/// </summary>
public sealed record StageDefinition
{
    /// <summary>
    /// 初始化 Stage 定义。
    /// Initializes a stage definition.
    /// </summary>
    public StageDefinition(
        string id,
        string displayName,
        int lifecycleOrder,
        ModelRouteKind? modelRouteKind = null,
        IReadOnlyList<string>? allowedPrevious = null,
        IReadOnlyList<string>? allowedNext = null,
        IReadOnlyList<string>? requiredCapabilities = null,
        StageContextProjectionMode contextProjectionMode = StageContextProjectionMode.SelectedSegments,
        string? executorBinding = null,
        StructuredValue? inputContract = null,
        StructuredValue? outputContract = null,
        StructuredValue? policyConstraints = null,
        StructuredValue? diagnosticsContract = null,
        MetadataBag? metadata = null)
    {
        if (lifecycleOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycleOrder), "Stage 生命周期顺序不能为负。");
        }

        Id = IdentifierGuard.AgainstNullOrWhiteSpace(id, nameof(id));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        LifecycleOrder = lifecycleOrder;
        ModelRouteKind = modelRouteKind ?? new ModelRouteKind(Id);
        AllowedPrevious = ValidateOptionalIdentifierList(allowedPrevious, nameof(allowedPrevious));
        AllowedNext = ValidateOptionalIdentifierList(allowedNext, nameof(allowedNext));
        RequiredCapabilities = ValidateOptionalIdentifierList(requiredCapabilities, nameof(requiredCapabilities));
        ContextProjectionMode = contextProjectionMode;
        ExecutorBinding = string.IsNullOrWhiteSpace(executorBinding) ? Id : executorBinding.Trim();
        InputContract = inputContract;
        OutputContract = outputContract;
        PolicyConstraints = policyConstraints;
        DiagnosticsContract = diagnosticsContract;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public int LifecycleOrder { get; }

    public ModelRouteKind ModelRouteKind { get; }

    public IReadOnlyList<string> AllowedPrevious { get; }

    public IReadOnlyList<string> AllowedNext { get; }

    public IReadOnlyList<string> RequiredCapabilities { get; }

    public StageContextProjectionMode ContextProjectionMode { get; }

    public string ExecutorBinding { get; }

    public StructuredValue? InputContract { get; }

    public StructuredValue? OutputContract { get; }

    public StructuredValue? PolicyConstraints { get; }

    public StructuredValue? DiagnosticsContract { get; }

    public MetadataBag Metadata { get; }

    internal static IReadOnlyList<string> ValidateOptionalIdentifierList(IReadOnlyList<string>? values, string paramName)
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
            throw new ArgumentException("Stage 标识列表不能包含重复项。", paramName);
        }

        return normalized;
    }
}

/// <summary>
/// Stage 跳转定义，声明生命周期图中一条允许的跳转边。
/// Stage transition definition that declares an allowed transition edge in the lifecycle graph.
/// </summary>
public sealed record StageTransitionDefinition
{
    /// <summary>
    /// 初始化 Stage 跳转定义。
    /// Initializes a stage transition definition.
    /// </summary>
    public StageTransitionDefinition(
        string fromStageId,
        string toStageId,
        string reasonCode,
        int priority = 0,
        StructuredValue? condition = null,
        MetadataBag? metadata = null)
    {
        if (priority < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priority), "Stage 跳转优先级不能为负。");
        }

        FromStageId = IdentifierGuard.AgainstNullOrWhiteSpace(fromStageId, nameof(fromStageId));
        ToStageId = IdentifierGuard.AgainstNullOrWhiteSpace(toStageId, nameof(toStageId));
        ReasonCode = IdentifierGuard.AgainstNullOrWhiteSpace(reasonCode, nameof(reasonCode));
        Priority = priority;
        Condition = condition;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string FromStageId { get; }

    public string ToStageId { get; }

    public string ReasonCode { get; }

    public int Priority { get; }

    public StructuredValue? Condition { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Stage 上下文片段，表达被投影进目标 Stage 的一段会话上下文。
/// Stage context segment that represents a projected piece of session context for a target stage.
/// </summary>
public sealed record StageContextSegment
{
    /// <summary>
    /// 初始化 Stage 上下文片段。
    /// Initializes a stage context segment.
    /// </summary>
    public StageContextSegment(
        string kind,
        string content,
        string? title = null,
        ResourceRef? source = null,
        bool required = false,
        int? estimatedTokens = null,
        MetadataBag? metadata = null)
    {
        if (estimatedTokens is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedTokens), "上下文片段 token 估算不能为负。");
        }

        Kind = IdentifierGuard.AgainstNullOrWhiteSpace(kind, nameof(kind));
        Content = IdentifierGuard.AgainstNullOrWhiteSpace(content, nameof(content));
        Title = title;
        Source = source;
        Required = required;
        EstimatedTokens = estimatedTokens;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string Kind { get; }

    public string Content { get; }

    public string? Title { get; }

    public ResourceRef? Source { get; }

    public bool Required { get; }

    public int? EstimatedTokens { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Stage 上下文包，表达编排层交给一次 Stage 执行的输入投影。
/// Stage context package that represents the input projection handed to a stage execution.
/// </summary>
public sealed record StageContextPackage
{
    /// <summary>
    /// 初始化 Stage 上下文包。
    /// Initializes a stage context package.
    /// </summary>
    public StageContextPackage(
        string packageId,
        string stageId,
        SessionId sessionId,
        ThreadId threadId,
        IReadOnlyList<StageContextSegment>? segments = null,
        IReadOnlyList<ArtifactRef>? artifactRefs = null,
        IReadOnlyList<string>? sourceCheckpointIds = null,
        StageContextProjectionMode projectionMode = StageContextProjectionMode.SelectedSegments,
        int? budgetTokens = null,
        StructuredValue? projectionReport = null,
        MetadataBag? metadata = null)
    {
        if (budgetTokens is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetTokens), "Stage 上下文预算不能为负。");
        }

        PackageId = IdentifierGuard.AgainstNullOrWhiteSpace(packageId, nameof(packageId));
        StageId = IdentifierGuard.AgainstNullOrWhiteSpace(stageId, nameof(stageId));
        SessionId = sessionId;
        ThreadId = threadId;
        Segments = segments ?? Array.Empty<StageContextSegment>();
        ArtifactRefs = artifactRefs ?? Array.Empty<ArtifactRef>();
        SourceCheckpointIds = StageDefinition.ValidateOptionalIdentifierList(sourceCheckpointIds, nameof(sourceCheckpointIds));
        ProjectionMode = projectionMode;
        BudgetTokens = budgetTokens;
        ProjectionReport = projectionReport;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string PackageId { get; }

    public string StageId { get; }

    public SessionId SessionId { get; }

    public ThreadId ThreadId { get; }

    public IReadOnlyList<StageContextSegment> Segments { get; }

    public IReadOnlyList<ArtifactRef> ArtifactRefs { get; }

    public IReadOnlyList<string> SourceCheckpointIds { get; }

    public StageContextProjectionMode ProjectionMode { get; }

    public int? BudgetTokens { get; }

    public StructuredValue? ProjectionReport { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Stage 执行请求，表达编排层交给 Stage Executor 的完整输入。
/// Stage execution request that represents the full input handed from the orchestrator to a stage executor.
/// </summary>
public sealed record StageExecutionRequest
{
    /// <summary>
    /// 初始化 Stage 执行请求。
    /// Initializes a stage execution request.
    /// </summary>
    public StageExecutionRequest(
        string executionId,
        StageDefinition stage,
        OrchestratorDecision decision,
        StageContextPackage contextPackage,
        ModelRouteResolutionResult modelRoute,
        StructuredValue? input = null,
        DateTimeOffset? requestedAt = null,
        MetadataBag? metadata = null)
    {
        ExecutionId = IdentifierGuard.AgainstNullOrWhiteSpace(executionId, nameof(executionId));
        Stage = stage ?? throw new ArgumentNullException(nameof(stage));
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        ContextPackage = contextPackage ?? throw new ArgumentNullException(nameof(contextPackage));
        ModelRoute = modelRoute ?? throw new ArgumentNullException(nameof(modelRoute));
        ValidateStageBinding(Stage, Decision, ContextPackage, ModelRoute);

        SessionId = Decision.SessionId;
        ThreadId = Decision.ThreadId;
        StageId = Stage.Id;
        ExecutorBinding = Stage.ExecutorBinding;
        RequiredCapabilities = Stage.RequiredCapabilities;
        Input = input;
        RequestedAt = requestedAt ?? DateTimeOffset.UtcNow;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string ExecutionId { get; }

    public SessionId SessionId { get; }

    public ThreadId ThreadId { get; }

    public string StageId { get; }

    public StageDefinition Stage { get; }

    public OrchestratorDecision Decision { get; }

    public StageContextPackage ContextPackage { get; }

    public ModelRouteResolutionResult ModelRoute { get; }

    public string ExecutorBinding { get; }

    public IReadOnlyList<string> RequiredCapabilities { get; }

    public StructuredValue? Input { get; }

    public DateTimeOffset RequestedAt { get; }

    public MetadataBag Metadata { get; }

    private static void ValidateStageBinding(
        StageDefinition stage,
        OrchestratorDecision decision,
        StageContextPackage contextPackage,
        ModelRouteResolutionResult modelRoute)
    {
        if (!string.Equals(decision.SelectedStageId, stage.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("执行请求的 Stage 必须与编排决策选择的 Stage 一致。", nameof(decision));
        }

        if (!string.Equals(contextPackage.StageId, stage.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("执行请求的上下文包必须属于当前 Stage。", nameof(contextPackage));
        }

        if (decision.SessionId != contextPackage.SessionId || decision.ThreadId != contextPackage.ThreadId)
        {
            throw new ArgumentException("编排决策与上下文包必须属于同一 session/thread。", nameof(contextPackage));
        }

        if (!string.Equals(modelRoute.RouteKind.Value, stage.ModelRouteKind.Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("模型路由结果必须匹配当前 Stage 的 model_route_kind。", nameof(modelRoute));
        }
    }
}

/// <summary>
/// Stage 检查点，表达一次 Stage 完成或中断后的可审计结果。
/// Stage checkpoint that represents the auditable result after a stage completes or stops.
/// </summary>
public sealed record StageCheckpoint
{
    /// <summary>
    /// 初始化 Stage 检查点。
    /// Initializes a stage checkpoint.
    /// </summary>
    public StageCheckpoint(
        string checkpointId,
        string stageId,
        StageExecutionState state,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt = null,
        string? summary = null,
        StructuredValue? output = null,
        IReadOnlyList<ArtifactRef>? artifactRefs = null,
        string? modelRouteSetId = null,
        ModelRouteKind? modelRouteKind = null,
        StructuredValue? diagnostics = null,
        IReadOnlyList<string>? nextStageSuggestions = null,
        MetadataBag? metadata = null)
    {
        if (completedAt.HasValue && completedAt.Value < startedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAt), "Stage 完成时间不能早于开始时间。");
        }

        CheckpointId = IdentifierGuard.AgainstNullOrWhiteSpace(checkpointId, nameof(checkpointId));
        StageId = IdentifierGuard.AgainstNullOrWhiteSpace(stageId, nameof(stageId));
        State = state;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Summary = summary;
        Output = output;
        ArtifactRefs = artifactRefs ?? Array.Empty<ArtifactRef>();
        ModelRouteSetId = string.IsNullOrWhiteSpace(modelRouteSetId) ? null : modelRouteSetId.Trim();
        ModelRouteKind = modelRouteKind;
        Diagnostics = diagnostics;
        NextStageSuggestions = StageDefinition.ValidateOptionalIdentifierList(nextStageSuggestions, nameof(nextStageSuggestions));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string CheckpointId { get; }

    public string StageId { get; }

    public StageExecutionState State { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public string? Summary { get; }

    public StructuredValue? Output { get; }

    public IReadOnlyList<ArtifactRef> ArtifactRefs { get; }

    public string? ModelRouteSetId { get; }

    public ModelRouteKind? ModelRouteKind { get; }

    public StructuredValue? Diagnostics { get; }

    public IReadOnlyList<string> NextStageSuggestions { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 编排决策，记录编排层为何选择或改道到某个 Stage。
/// Orchestrator decision that records why the orchestrator selected or redirected to a stage.
/// </summary>
public sealed record OrchestratorDecision
{
    /// <summary>
    /// 初始化编排决策。
    /// Initializes an orchestrator decision.
    /// </summary>
    public OrchestratorDecision(
        string decisionId,
        SessionId sessionId,
        ThreadId threadId,
        string selectedStageId,
        IReadOnlyList<string> candidateStageIds,
        string reasonCode,
        string? previousStageId = null,
        string? contextProjectionReason = null,
        IReadOnlyList<string>? policyHits = null,
        DateTimeOffset? decidedAt = null,
        StructuredValue? diagnostics = null,
        MetadataBag? metadata = null)
    {
        DecisionId = IdentifierGuard.AgainstNullOrWhiteSpace(decisionId, nameof(decisionId));
        SessionId = sessionId;
        ThreadId = threadId;
        SelectedStageId = IdentifierGuard.AgainstNullOrWhiteSpace(selectedStageId, nameof(selectedStageId));
        CandidateStageIds = ValidateRequiredIdentifierList(candidateStageIds, nameof(candidateStageIds));
        if (!CandidateStageIds.Contains(SelectedStageId, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("候选 Stage 列表必须包含最终选择的 Stage。", nameof(candidateStageIds));
        }

        ReasonCode = IdentifierGuard.AgainstNullOrWhiteSpace(reasonCode, nameof(reasonCode));
        PreviousStageId = string.IsNullOrWhiteSpace(previousStageId) ? null : previousStageId.Trim();
        ContextProjectionReason = contextProjectionReason;
        PolicyHits = StageDefinition.ValidateOptionalIdentifierList(policyHits, nameof(policyHits));
        DecidedAt = decidedAt ?? DateTimeOffset.UtcNow;
        Diagnostics = diagnostics;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string DecisionId { get; }

    public SessionId SessionId { get; }

    public ThreadId ThreadId { get; }

    public string SelectedStageId { get; }

    public IReadOnlyList<string> CandidateStageIds { get; }

    public string ReasonCode { get; }

    public string? PreviousStageId { get; }

    public string? ContextProjectionReason { get; }

    public IReadOnlyList<string> PolicyHits { get; }

    public DateTimeOffset DecidedAt { get; }

    public StructuredValue? Diagnostics { get; }

    public MetadataBag Metadata { get; }

    private static IReadOnlyList<string> ValidateRequiredIdentifierList(IReadOnlyList<string>? values, string paramName)
    {
        if (values is null || values.Count == 0)
        {
            throw new ArgumentException("编排决策至少需要一个候选 Stage。", paramName);
        }

        return StageDefinition.ValidateOptionalIdentifierList(values, paramName);
    }
}

/// <summary>
/// TianShu 内置 Stage 标识和定义。
/// Built-in TianShu stage identifiers and definitions.
/// </summary>
public static class BuiltInStageDefinitions
{
    public const string Default = "default";
    public const string Planning = "planning";
    public const string Coding = "coding";
    public const string Review = "review";
    public const string Summarization = "summarization";
    public const string MemoryExtraction = "memory_extraction";
    public const string LongContext = "long_context";
    public const string Fast = "fast";

    public static IReadOnlyList<StageDefinition> All { get; } =
    [
        new(Default, "Default", 0, ModelRouteKind.Default, allowedNext: [Planning, Coding, Review, Fast]),
        new(Planning, "Planning", 10, ModelRouteKind.Planning, allowedPrevious: [Default, Fast], allowedNext: [Coding, LongContext, Fast]),
        new(LongContext, "Long Context", 20, ModelRouteKind.LongContext, allowedPrevious: [Default, Planning, Coding, Review], allowedNext: [Planning, Coding, Review, Summarization], contextProjectionMode: StageContextProjectionMode.ReferencesOnly),
        new(Coding, "Coding", 30, ModelRouteKind.Coding, allowedPrevious: [Default, Planning, LongContext, Fast], allowedNext: [Review, Summarization, MemoryExtraction]),
        new(Review, "Review", 40, ModelRouteKind.Review, allowedPrevious: [Default, Coding, LongContext], allowedNext: [Coding, Summarization, MemoryExtraction]),
        new(Summarization, "Summarization", 50, ModelRouteKind.Summarization, allowedPrevious: [Coding, Review, LongContext], allowedNext: [MemoryExtraction, Default], contextProjectionMode: StageContextProjectionMode.Summary),
        new(MemoryExtraction, "Memory Extraction", 60, ModelRouteKind.MemoryExtraction, allowedPrevious: [Coding, Review, Summarization], allowedNext: [Default], contextProjectionMode: StageContextProjectionMode.Summary),
        new(Fast, "Fast", 10, ModelRouteKind.Fast, allowedPrevious: [Default, Planning], allowedNext: [Planning, Coding]),
    ];
}
