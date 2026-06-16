using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Conversations;

/// <summary>
/// 控制平面对话角色。
/// Control-plane conversation role.
/// </summary>
public enum ControlPlaneConversationRole
{
    System = 0,
    User = 1,
    Assistant = 2,
}

/// <summary>
/// 控制平面轮次提交结果。
/// Control-plane turn submission result.
/// </summary>
public sealed record ControlPlaneTurnSubmissionResult
{
    public bool Accepted { get; init; }

    public string Message { get; init; } = string.Empty;

    public TurnId? TurnId { get; init; }

    public string? TurnStatus { get; init; }

    public string? CorrelationId { get; init; }

    public ControlPlaneFollowUpMode? RequestedMode { get; init; }

    public ControlPlaneFollowUpMode? EffectiveMode { get; init; }
}

/// <summary>
/// 控制平面待处理 follow-up 变更结果。
/// Control-plane pending follow-up mutation result.
/// </summary>
public sealed record ControlPlanePendingFollowUpMutationResult
{
    public bool Accepted { get; init; }

    public string Message { get; init; } = string.Empty;

    public ThreadId? ThreadId { get; init; }

    public TurnId? TurnId { get; init; }

    public string? CorrelationId { get; init; }

    public ControlPlanePendingFollowUpMutationKind Kind { get; init; }

    public ControlPlanePendingInputState? PendingInputState { get; init; }
}

/// <summary>
/// 控制平面对话消息。
/// Control-plane conversation message.
/// </summary>
public sealed record ControlPlaneConversationMessage
{
    public ControlPlaneConversationRole Role { get; init; }

    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<ControlPlaneInputItem> ContentItems { get; init; } = Array.Empty<ControlPlaneInputItem>();

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public bool IsStreaming { get; init; }
}

/// <summary>
/// 控制平面输入项基类。
/// Base type for control-plane input items.
/// </summary>
public abstract record ControlPlaneInputItem(string Type);

/// <summary>
/// 控制平面文本输入。
/// Control-plane text input.
/// </summary>
public sealed record ControlPlaneTextInput(
    string Text,
    IReadOnlyList<ControlPlaneTextElement>? TextElements = null)
    : ControlPlaneInputItem("text");

/// <summary>
/// 控制平面远程图片输入。
/// Control-plane remote image input.
/// </summary>
public sealed record ControlPlaneImageInput(string Url)
    : ControlPlaneInputItem("image");

/// <summary>
/// 控制平面本地图片输入。
/// Control-plane local image input.
/// </summary>
public sealed record ControlPlaneLocalImageInput(string Path)
    : ControlPlaneInputItem("local_image");

/// <summary>
/// 控制平面技能输入。
/// Control-plane skill input.
/// </summary>
public sealed record ControlPlaneSkillInput(string Name, string Path)
    : ControlPlaneInputItem("skill");

/// <summary>
/// 控制平面提及输入。
/// Control-plane mention input.
/// </summary>
public sealed record ControlPlaneMentionInput(string Name, string Path)
    : ControlPlaneInputItem("mention");

/// <summary>
/// 控制平面文本占位片段。
/// Control-plane text placeholder segment.
/// </summary>
public sealed record ControlPlaneTextElement(ControlPlaneByteRange ByteRange, string? Placeholder = null);

/// <summary>
/// 控制平面字节区间。
/// Control-plane byte range.
/// </summary>
public sealed record ControlPlaneByteRange(int Start, int End);

/// <summary>
/// 控制平面线程列表结果。
/// Control-plane thread listing result.
/// </summary>
public sealed record ControlPlaneThreadListResult
{
    public IReadOnlyList<ControlPlaneThreadSummary> Threads { get; init; } = Array.Empty<ControlPlaneThreadSummary>();

    public string? NextCursor { get; init; }
}

/// <summary>
/// 控制平面线程摘要。
/// Control-plane thread summary.
/// </summary>
public sealed record ControlPlaneThreadSummary
{
    public ThreadId ThreadId { get; init; }

    public string Preview { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? Path { get; init; }

    public string? ModelProvider { get; init; }

    public ControlPlaneThreadSourceKind? Source { get; init; }

    public ThreadId? ParentThreadId { get; init; }

    public int? LineageDepth { get; init; }

    public string? CliVersion { get; init; }

    public string? AgentNickname { get; init; }

    public string? AgentRole { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsEphemeral { get; init; }

    public string? Status { get; init; }

    public IReadOnlyList<string> ActiveFlags { get; init; } = Array.Empty<string>();

    public string? GitSha { get; init; }

    public string? GitBranch { get; init; }

    public string? GitOriginUrl { get; init; }

    public ControlPlaneThreadSessionConfiguration? SessionConfiguration { get; init; }
}

/// <summary>
/// 控制平面线程会话配置摘要。
/// Control-plane thread session configuration summary.
/// </summary>
public sealed record ControlPlaneThreadSessionConfiguration
{
    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    public string? ModelProviderId { get; init; }

    public string? ModelRouteSetId { get; init; }

    public string? ServiceTier { get; init; }

    public string? ApprovalPolicy { get; init; }

    public string? SandboxPolicy { get; init; }

    public StructuredValue? SandboxPolicyPayload { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? HistoryLogId { get; init; }

    public int? HistoryEntryCount { get; init; }

    public string? RolloutPath { get; init; }

    public string? ReasoningSummary { get; init; }

    public string? Verbosity { get; init; }

    public string? Personality { get; init; }

    public bool? AllowLoginShell { get; init; }

    public StructuredValue? ShellEnvironmentPolicy { get; init; }

    public string? ProviderBaseUrl { get; init; }

    public string? ProviderApiKeyEnvironmentVariable { get; init; }

    public string? ProviderWireApi { get; init; }

    public int? ProviderRequestMaxRetries { get; init; }

    public int? ProviderStreamMaxRetries { get; init; }

    public long? ProviderStreamIdleTimeoutMs { get; init; }

    public long? ProviderWebsocketConnectTimeoutMs { get; init; }

    public bool? ProviderSupportsWebsockets { get; init; }

    public string? WebSearchMode { get; init; }

    public string? ServiceName { get; init; }

    public string? BaseInstructions { get; init; }

    public string? DeveloperInstructions { get; init; }

    public string? UserInstructions { get; init; }

    public IReadOnlyList<StructuredValue>? DynamicTools { get; init; }

    public StructuredValue? CollaborationMode { get; init; }

    public bool? PersistExtendedHistory { get; init; }

    public ThreadId? ForkedFromThreadId { get; init; }

    public string? WorkingDirectory { get; init; }

    public ControlPlaneSessionSource? SessionSource { get; init; }

    public string? WindowsSandboxLevel { get; init; }

    public bool? DefaultModeRequestUserInputEnabled { get; init; }
}

/// <summary>
/// 控制平面线程详情。
/// Control-plane thread details.
/// </summary>
public sealed record ControlPlaneThreadDetail
{
    public ThreadId ThreadId { get; init; }

    public string Preview { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? Path { get; init; }

    public string? ModelProvider { get; init; }

    public ControlPlaneThreadSourceKind? Source { get; init; }

    public ThreadId? ParentThreadId { get; init; }

    public int? LineageDepth { get; init; }

    public string? CliVersion { get; init; }

    public string? AgentNickname { get; init; }

    public string? AgentRole { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsEphemeral { get; init; }

    public string? Status { get; init; }

    public IReadOnlyList<string> ActiveFlags { get; init; } = Array.Empty<string>();

    public string? GitSha { get; init; }

    public string? GitBranch { get; init; }

    public string? GitOriginUrl { get; init; }

    public IReadOnlyList<ControlPlaneThreadTurn> Turns { get; init; } = Array.Empty<ControlPlaneThreadTurn>();

    public IReadOnlyList<ControlPlaneSeedHistoryItem> SeedHistory { get; init; } = Array.Empty<ControlPlaneSeedHistoryItem>();

    public ControlPlanePendingInputState? PendingInputState { get; init; }

    public IReadOnlyList<ControlPlanePendingInteractiveRequest> PendingInteractiveRequests { get; init; } = Array.Empty<ControlPlanePendingInteractiveRequest>();

    public ControlPlaneThreadSessionConfiguration? SessionConfiguration { get; init; }
}

/// <summary>
/// 控制平面线程管理操作结果。
/// Control-plane result returned by thread management operations.
/// </summary>
public sealed record ControlPlaneThreadOperationResult
{
    public ControlPlaneThreadDetail? Thread { get; init; }
}

/// <summary>
/// 控制平面已加载线程列表结果。
/// Control-plane result that lists loaded thread identifiers.
/// </summary>
public sealed record ControlPlaneLoadedThreadListResult
{
    public IReadOnlyList<ThreadId> ThreadIds { get; init; } = Array.Empty<ThreadId>();

    public string? NextCursor { get; init; }
}

/// <summary>
/// 控制平面线程取消订阅结果。
/// Control-plane result returned by thread unsubscribe.
/// </summary>
public sealed record ControlPlaneThreadUnsubscribeResult
{
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面线程挂起交互计数结果。
/// Control-plane result returned by thread elicitation adjustments.
/// </summary>
public sealed record ControlPlaneThreadElicitationResult
{
    public ulong Count { get; init; }

    public bool Paused { get; init; }
}

/// <summary>
/// 控制平面线程命令已接受结果。
/// Control-plane result indicating that a thread command was accepted.
/// </summary>
public sealed record ControlPlaneThreadCommandAcceptedResult;

/// <summary>
/// 控制平面清空线程结果。
/// Control-plane result returned after clearing threads.
/// </summary>
public sealed record ControlPlaneClearThreadsResult
{
    public int DeletedCount { get; init; }
}

/// <summary>
/// 控制平面模糊文件搜索结果项。
/// Control-plane fuzzy-file-search result entry.
/// </summary>
public sealed record ControlPlaneFuzzyFileSearchFile
{
    public string Path { get; init; } = string.Empty;

    public string? FileName { get; init; }
}

/// <summary>
/// 控制平面模糊文件搜索结果。
/// Control-plane fuzzy-file-search result.
/// </summary>
public sealed record ControlPlaneFuzzyFileSearchResult
{
    public IReadOnlyList<ControlPlaneFuzzyFileSearchFile> Files { get; init; } = Array.Empty<ControlPlaneFuzzyFileSearchFile>();
}

/// <summary>
/// 控制平面模糊文件搜索命令已接受结果。
/// Control-plane result indicating a fuzzy-file-search command was accepted.
/// </summary>
public sealed record ControlPlaneFuzzyFileSearchCommandAcceptedResult;

/// <summary>
/// 控制平面实时音频输入。
/// Control-plane realtime audio input payload.
/// </summary>
public sealed record ControlPlaneRealtimeAudioInput
{
    public string Data { get; init; } = string.Empty;

    public int? SampleRate { get; init; }

    public int? NumChannels { get; init; }

    public int? SamplesPerChannel { get; init; }
}

/// <summary>
/// 控制平面实时命令已接受结果。
/// Control-plane result indicating a realtime command was accepted.
/// </summary>
public sealed record ControlPlaneRealtimeCommandAcceptedResult;

/// <summary>
/// 控制平面动态工具规范。
/// Control-plane dynamic tool specification.
/// </summary>
public sealed record ControlPlaneDynamicToolSpec
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public StructuredValue InputSchema { get; init; } = StructuredValue.Null;
}

/// <summary>
/// 控制平面执行策略修订。
/// Control-plane execution-policy amendment.
/// </summary>
public sealed record ControlPlaneExecPolicyAmendment(
    IReadOnlyList<string> CommandPrefix);

/// <summary>
/// 控制平面网络策略修订。
/// Control-plane network-policy amendment.
/// </summary>
public sealed record ControlPlaneNetworkPolicyAmendment(
    string Host,
    string Action);

/// <summary>
/// 控制平面审批选项。
/// Control-plane approval decision option.
/// </summary>
public sealed record ControlPlaneApprovalDecisionOption(
    string Type,
    ControlPlaneExecPolicyAmendment? ExecPolicyAmendment = null,
    ControlPlaneNetworkPolicyAmendment? NetworkPolicyAmendment = null);

/// <summary>
/// 控制平面线程轮次错误。
/// Control-plane thread-turn error.
/// </summary>
public sealed record ControlPlaneThreadTurnError
{
    public string? Message { get; init; }

    public string? AdditionalDetails { get; init; }
}

/// <summary>
/// 控制平面线程轮次项。
/// Control-plane thread-turn item.
/// </summary>
public sealed record ControlPlaneThreadTurnItem
{
    public string Id { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string? Text { get; init; }

    public string? Phase { get; init; }

    public StructuredValue? Data { get; init; }
}

/// <summary>
/// 控制平面线程轮次。
/// Control-plane thread turn.
/// </summary>
public sealed record ControlPlaneThreadTurn
{
    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public ControlPlaneThreadTurnError? Error { get; init; }

    public IReadOnlyList<ControlPlaneThreadTurnItem> Items { get; init; } = Array.Empty<ControlPlaneThreadTurnItem>();
}

/// <summary>
/// 控制平面线程种子历史项。
/// Control-plane seed-history item.
/// </summary>
public sealed record ControlPlaneSeedHistoryItem
{
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<ControlPlaneInputItem> Inputs { get; init; } = Array.Empty<ControlPlaneInputItem>();
}

/// <summary>
/// 控制平面待处理交互请求。
/// Control-plane pending interactive request.
/// </summary>
public sealed record ControlPlanePendingInteractiveRequest
{
    public long RequestId { get; init; }

    public string? RequestIdRaw { get; init; }

    public string RequestKind { get; init; } = string.Empty;

    public string? RequestMethod { get; init; }

    public string CallId { get; init; } = string.Empty;

    public string? ThreadId { get; init; }

    public string? TurnId { get; init; }

    public string? ToolName { get; init; }

    public string? ServerName { get; init; }

    public string? Text { get; init; }

    public string? Status { get; init; }

    public string? Phase { get; init; }

    public bool? RequiresApproval { get; init; }

    public string? ApprovalKind { get; init; }

    public IReadOnlyList<string>? AvailableDecisions { get; init; }

    public IReadOnlyList<ControlPlaneApprovalDecisionOption>? AvailableDecisionOptions { get; init; }
}

/// <summary>
/// 控制平面待处理输入项。
/// Control-plane pending input entry.
/// </summary>
public sealed record ControlPlanePendingInputStateEntry(
    string CorrelationId,
    string RequestedMode,
    string EffectiveMode,
    string LifecycleState,
    string? ExpectedTurnId,
    string? TurnId,
    StructuredValue? CompareKey = null,
    string PendingBucket = "QueuedUserMessage",
    IReadOnlyList<ControlPlaneInputItem>? Inputs = null);

/// <summary>
/// 控制平面待处理输入状态。
/// Control-plane pending input state.
/// </summary>
public sealed record ControlPlanePendingInputState(
    IReadOnlyList<ControlPlanePendingInputStateEntry> Entries,
    bool InterruptRequestPending,
    bool SubmitPendingSteersAfterInterrupt = false,
    IReadOnlyList<ControlPlanePendingInputStateEntry>? QueuedUserMessages = null,
    IReadOnlyList<ControlPlanePendingInputStateEntry>? PendingSteers = null);

/// <summary>
/// 控制平面线程快照。
/// Control-plane thread snapshot.
/// </summary>
public sealed record ControlPlaneThreadSnapshot
{
    public ControlPlaneThreadSummary Thread { get; init; } = new();

    public IReadOnlyList<ControlPlaneConversationMessage> Messages { get; init; } = Array.Empty<ControlPlaneConversationMessage>();

    public IReadOnlyList<ControlPlaneThreadTurn> Turns { get; init; } = Array.Empty<ControlPlaneThreadTurn>();

    public IReadOnlyList<ControlPlaneSeedHistoryItem> SeedHistory { get; init; } = Array.Empty<ControlPlaneSeedHistoryItem>();

    public ControlPlanePendingInputState? PendingInputState { get; init; }

    public IReadOnlyList<ControlPlanePendingInteractiveRequest> PendingInteractiveRequests { get; init; } = Array.Empty<ControlPlanePendingInteractiveRequest>();
}
