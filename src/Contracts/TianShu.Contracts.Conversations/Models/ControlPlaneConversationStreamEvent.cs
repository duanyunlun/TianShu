using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Conversations;

/// <summary>
/// 控制平面对外暴露的会话流事件种类。
/// Conversation-stream event kinds exposed by the control plane.
/// </summary>
public enum ControlPlaneConversationStreamEventKind
{
    Info = 0,
    RawNotification = 1,
    TurnStarted = 2,
    AssistantTextDelta = 3,
    AssistantTextCompleted = 4,
    ToolCallStarted = 5,
    ToolCallOutputDelta = 6,
    ToolCallCompleted = 7,
    ApprovalRequested = 8,
    TurnCompleted = 9,
    Error = 10,
    ReasoningDelta = 11,
    PlanUpdated = 12,
    DiffUpdated = 13,
    UserInputRequested = 14,
    ServerRequestResolved = 15,
    PermissionRequested = 16,
    TaskStarted = 17,
    TaskCompleted = 18,
    OperationReported = 19,
    TurnSteered = 20,
    McpServerStatusUpdated = 21,
    ItemStarted = 22,
    ItemCompleted = 23,
    UserMessageCommitted = 24,
    PendingFollowUpUpdated = 25,
    AgentJobProgress = 26,
    DeprecationNotice = 27,
    ConfigWarning = 28,
    ThreadStatusChanged = 29,
    ThreadNameUpdated = 30,
    ThreadTokenUsageUpdated = 31,
    ThreadCompacted = 32,
    SkillsChanged = 33,
    CommandExecOutputDelta = 34,
    AppListUpdated = 35,
    ThreadRealtimeItemAdded = 36,
    ThreadRealtimeOutputAudioDelta = 37,
    ThreadRealtimeError = 38,
    ThreadRealtimeClosed = 39,
    HookStarted = 40,
    HookCompleted = 41,
    ModelRerouted = 42,
}

/// <summary>
/// 会话流事件携带的结构化载荷种类。
/// Structured payload kinds carried by a conversation-stream event.
/// </summary>
public enum ControlPlaneConversationStreamPayloadKind
{
    Plan = 0,
    ToolCall = 1,
    ApprovalRequest = 2,
    PermissionRequest = 3,
    UserInputRequest = 4,
    ServerRequestResolved = 5,
    Task = 6,
    Operation = 7,
    HookRun = 8,
    Reasoning = 9,
    ModelRerouted = 10,
    McpServerStatus = 11,
    Item = 12,
    CommittedUserMessage = 13,
    PendingFollowUp = 14,
    PendingInputState = 15,
    AgentJobProgress = 16,
    DeprecationNotice = 17,
    ConfigWarning = 18,
    ThreadStatusChanged = 19,
    ThreadNameUpdated = 20,
    ThreadTokenUsage = 21,
    CommandExecOutputDelta = 22,
    AppListUpdated = 23,
    WindowsSandboxSetup = 24,
    McpServerOauthLogin = 25,
    RealtimeSession = 26,
    FuzzyFileSearchSession = 27,
    ThreadRealtimeItemAdded = 28,
    ThreadRealtimeOutputAudioDelta = 29,
    ThreadRealtimeError = 30,
    ThreadRealtimeClosed = 31,
}

/// <summary>
/// 会话流事件的诊断补充信息。
/// Diagnostic supplements attached to a conversation-stream event.
/// </summary>
public sealed record ControlPlaneConversationStreamDiagnostics
{
    public string? DataJson { get; init; }

    public string? MetadataJson { get; init; }

    public string? RawJson { get; init; }
}

/// <summary>
/// 控制平面对外暴露的会话流事件包络。
/// Conversation-stream event envelope exposed by the control plane.
/// </summary>
public sealed record ControlPlaneConversationStreamEvent
{
    public ControlPlaneConversationStreamEventKind Kind { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public ThreadId? ThreadId { get; init; }

    public TurnId? TurnId { get; init; }

    public string? ItemId { get; init; }

    public CallId? CallId { get; init; }

    public string? ToolName { get; init; }

    public string? ServerName { get; init; }

    public string? Text { get; init; }

    public string? Status { get; init; }

    public string? Phase { get; init; }

    public string? Message { get; init; }

    public bool? WillRetry { get; init; }

    public bool? RequiresApproval { get; init; }

    public string? ApprovalKind { get; init; }

    public IReadOnlyList<string>? AvailableDecisions { get; init; }

    public IReadOnlyList<ControlPlaneApprovalDecisionOption>? AvailableDecisionOptions { get; init; }

    public string? SourceMethod { get; init; }

    public string? TaskType { get; init; }

    public string? OperationName { get; init; }

    public string? Source { get; init; }

    public ControlPlaneConversationStreamPayloadKind? PayloadKind { get; init; }

    public StructuredValue? Payload { get; init; }

    public ControlPlaneThreadTurnError? TurnError { get; init; }

    public ControlPlaneConversationStreamDiagnostics? Diagnostics { get; init; }
}

/// <summary>
/// 会话流事件的 .NET 事件参数。
/// .NET event arguments for a conversation-stream event.
/// </summary>
public sealed class ControlPlaneConversationStreamEventArgs(ControlPlaneConversationStreamEvent streamEvent) : EventArgs
{
    public ControlPlaneConversationStreamEvent StreamEvent { get; } = streamEvent;
}
