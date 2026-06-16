using TianShu.Contracts.Conversations;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Runtime.Events;

public enum AgentStreamEventKind
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

public sealed partial class AgentStreamEvent
{
    public AgentStreamEventKind Kind { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string? ThreadId { get; init; }

    public string? TurnId { get; init; }

    public string? ItemId { get; init; }

    public string? CallId { get; init; }

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

    public IReadOnlyList<ApprovalDecisionOptionPayload>? AvailableDecisionOptions { get; init; }

    public string? SourceMethod { get; init; }

    public string? TaskType { get; init; }

    public string? OperationName { get; init; }

    public string? Source { get; init; }

    public PlanEventPayload? Plan { get; init; }

    public ToolCallEventPayload? ToolCall { get; init; }

    public ApprovalRequestPayload? ApprovalRequest { get; init; }

    public PermissionRequestPayload? PermissionRequest { get; init; }

    public UserInputRequestPayload? UserInputRequest { get; init; }

    public ServerRequestResolvedPayload? ServerRequestResolved { get; init; }

    public TaskEventPayload? Task { get; init; }

    public OperationEventPayload? Operation { get; init; }

    public HookRunPayload? HookRun { get; init; }

    public ReasoningEventPayload? Reasoning { get; init; }

    public ModelReroutedPayload? ModelRerouted { get; init; }

    public McpServerStatusPayload? McpServerStatus { get; init; }

    public ItemEventPayload? Item { get; init; }

    public CommittedUserMessagePayload? CommittedUserMessage { get; init; }

    public PendingFollowUpLifecyclePayload? PendingFollowUp { get; init; }

    public PendingInputStatePayload? PendingInputState { get; init; }

    public ControlPlaneThreadTurnError? TurnError { get; init; }

    public AgentJobProgressPayload? AgentJobProgress { get; init; }

    public DeprecationNoticePayload? DeprecationNotice { get; init; }

    public ConfigWarningPayload? ConfigWarning { get; init; }

    public ThreadStatusChangedPayload? ThreadStatusChanged { get; init; }

    public ThreadNameUpdatedPayload? ThreadNameUpdated { get; init; }

    public ThreadTokenUsagePayload? ThreadTokenUsage { get; init; }

    public CommandExecOutputDeltaPayload? CommandExecOutputDelta { get; init; }

    public AppListUpdatedPayload? AppListUpdated { get; init; }

    public WindowsSandboxSetupPayload? WindowsSandboxSetup { get; init; }

    public McpServerOauthLoginPayload? McpServerOauthLogin { get; init; }

    public RealtimeSessionPayload? RealtimeSession { get; init; }

    public FuzzyFileSearchSessionPayload? FuzzyFileSearchSession { get; init; }

    public ThreadRealtimeItemAddedPayload? ThreadRealtimeItemAdded { get; init; }

    public ThreadRealtimeOutputAudioDeltaPayload? ThreadRealtimeOutputAudioDelta { get; init; }

    public ThreadRealtimeErrorPayload? ThreadRealtimeError { get; init; }

    public ThreadRealtimeClosedPayload? ThreadRealtimeClosed { get; init; }

    // 仅用于日志、抓包与故障诊断；外部消费方禁止基于这些字段做业务解析。
    public string? DataJson { get; init; }

    public string? MetadataJson { get; init; }

    public string? RawJson { get; init; }
}
