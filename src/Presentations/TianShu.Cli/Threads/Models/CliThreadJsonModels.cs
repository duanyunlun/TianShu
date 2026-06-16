using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli;

/// <summary>
/// CLI 本地线程 JSON 输出配置模型。
/// CLI-local JSON model for thread/session output.
/// </summary>
internal sealed class CliThreadSessionConfiguration
{
    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    public string? ModelProviderId { get; init; }

    public string? ServiceTier { get; init; }

    public string? ApprovalPolicy { get; init; }

    public string? SandboxPolicy { get; init; }

    public StructuredValue? SandboxPolicyPayload { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? HistoryLogId { get; init; }

    public int? HistoryEntryCount { get; init; }

    public string? RolloutPath { get; init; }

    public string? ForkedFromId { get; init; }

    public string? Cwd { get; init; }

    public bool? Ephemeral { get; init; }

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

    public string? ReasoningSummary { get; init; }

    public string? Verbosity { get; init; }

    public string? Personality { get; init; }

    public IReadOnlyList<StructuredValue>? DynamicTools { get; init; }

    public StructuredValue? CollaborationMode { get; init; }

    public bool? PersistExtendedHistory { get; init; }

    public ControlPlaneSessionSource? SessionSource { get; init; }

    public string? WindowsSandboxLevel { get; init; }

    public bool? DefaultModeRequestUserInputEnabled { get; init; }
}

/// <summary>
/// CLI 本地线程摘要 JSON 模型。
/// CLI-local JSON model for thread summary output.
/// </summary>
internal sealed class CliThreadInfo
{
    public string ThreadId { get; init; } = string.Empty;

    public string Preview { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Cwd { get; init; }

    public string? Path { get; init; }

    public string? ModelProvider { get; init; }

    public ControlPlaneSessionSource? Source { get; init; }

    public string? CliVersion { get; init; }

    public string? AgentNickname { get; init; }

    public string? AgentRole { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsEphemeral { get; init; }

    public CliThreadStatus? Status { get; init; }

    public CliThreadGitInfo? GitInfo { get; init; }

    public CliThreadSessionConfiguration? SessionConfiguration { get; init; }
}

/// <summary>
/// CLI 本地线程列表 JSON 模型。
/// CLI-local JSON model for thread list output.
/// </summary>
internal sealed class CliThreadListResult
{
    public IReadOnlyList<CliThreadInfo> Data { get; init; } = Array.Empty<CliThreadInfo>();

    public string? NextCursor { get; init; }
}

/// <summary>
/// CLI 本地线程恢复 JSON 模型。
/// CLI-local JSON model for thread resume output.
/// </summary>
internal sealed class CliThreadResumeResult
{
    public string ThreadId { get; init; } = string.Empty;

    public string Preview { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Cwd { get; init; }

    public string? Path { get; init; }

    public string? ModelProvider { get; init; }

    public ControlPlaneSessionSource? Source { get; init; }

    public string? CliVersion { get; init; }

    public string? AgentNickname { get; init; }

    public string? AgentRole { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    public bool IsEphemeral { get; init; }

    public CliThreadStatus? Status { get; init; }

    public CliThreadGitInfo? GitInfo { get; init; }

    public IReadOnlyList<CliThreadTurn> Turns { get; init; } = Array.Empty<CliThreadTurn>();

    public IReadOnlyList<CliThreadSeedHistoryItem> SeedHistory { get; init; } = Array.Empty<CliThreadSeedHistoryItem>();

    public IReadOnlyList<ControlPlaneConversationMessage> Messages { get; init; } = Array.Empty<ControlPlaneConversationMessage>();

    public CliPendingInputStatePayload? PendingInputState { get; init; }

    public IReadOnlyList<CliInteractiveRequestReplay> PendingInteractiveRequests { get; init; } = Array.Empty<CliInteractiveRequestReplay>();

    public CliThreadSessionConfiguration? SessionConfiguration { get; init; }
}

/// <summary>
/// CLI 本地线程详情 JSON 模型。
/// CLI-local JSON model for thread detail output.
/// </summary>
internal sealed class CliThreadDetails
{
    public string Id { get; init; } = string.Empty;

    public string Preview { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Cwd { get; init; }

    public string? Path { get; init; }

    public string? ModelProvider { get; init; }

    public ControlPlaneSessionSource? Source { get; init; }

    public string? CliVersion { get; init; }

    public string? AgentNickname { get; init; }

    public string? AgentRole { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    public bool Ephemeral { get; init; }

    public CliThreadStatus? Status { get; init; }

    public CliThreadGitInfo? GitInfo { get; init; }

    public IReadOnlyList<CliThreadTurn> Turns { get; init; } = Array.Empty<CliThreadTurn>();

    public IReadOnlyList<CliThreadSeedHistoryItem> SeedHistory { get; init; } = Array.Empty<CliThreadSeedHistoryItem>();

    public CliPendingInputStatePayload? PendingInputState { get; set; }

    public IReadOnlyList<CliInteractiveRequestReplay> PendingInteractiveRequests { get; set; } = Array.Empty<CliInteractiveRequestReplay>();

    public CliThreadSessionConfiguration? SessionConfiguration { get; init; }
}

/// <summary>
/// CLI 本地挂起交互请求 JSON 模型。
/// CLI-local JSON model for pending interactive request output.
/// </summary>
internal sealed class CliInteractiveRequestReplay
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

    public IReadOnlyList<CliApprovalDecisionOptionPayload>? AvailableDecisionOptions { get; init; }

    public CliApprovalRequestPayload? ApprovalRequest { get; init; }

    public CliPermissionRequestPayload? PermissionRequest { get; init; }

    public CliUserInputRequestPayload? UserInputRequest { get; init; }
}

internal sealed class CliThreadStatus
{
    public string Type { get; init; } = string.Empty;

    public IReadOnlyList<string> ActiveFlags { get; init; } = Array.Empty<string>();
}

internal sealed class CliThreadGitInfo
{
    public string? Sha { get; init; }

    public string? Branch { get; init; }

    public string? OriginUrl { get; init; }
}

internal sealed class CliThreadTurn
{
    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public CliThreadTurnError? Error { get; init; }

    public IReadOnlyList<CliThreadTurnItem> Items { get; init; } = Array.Empty<CliThreadTurnItem>();
}

internal sealed class CliThreadTurnError
{
    public string? Message { get; init; }

    public string? AdditionalDetails { get; init; }
}

internal abstract class CliThreadTurnItem
{
    public string Id { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public virtual string? Text => null;

    public virtual string? Phase => null;
}

internal sealed class CliGenericThreadTurnItem : CliThreadTurnItem
{
    public string? RawText { get; init; }

    public string? ItemPhase { get; init; }

    public StructuredValue? RawData { get; init; }

    public override string? Text => RawText;

    public override string? Phase => ItemPhase;
}

internal sealed class CliThreadSeedHistoryItem
{
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<ControlPlaneInputItem> Inputs { get; init; } = Array.Empty<ControlPlaneInputItem>();
}
