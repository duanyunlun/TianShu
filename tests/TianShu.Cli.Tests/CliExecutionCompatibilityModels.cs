using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using TianShu.Provider.Abstractions;

namespace TianShu.Cli.Tests;

internal enum FollowUpMode
{
    Queue = 0,
    Steer = 1,
    Interrupt = 2,
}

internal enum ConversationRole
{
    System = 0,
    User = 1,
    Assistant = 2,
}

internal sealed class ConversationMessage
{
    public ConversationRole Role { get; init; }

    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<AgentUserInput> ContentItems { get; init; } = Array.Empty<AgentUserInput>();

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public bool IsStreaming { get; init; }
}

internal sealed class AgentSendResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? TurnId { get; init; }

    public string? TurnStatus { get; init; }

    public string? CorrelationId { get; init; }

    public FollowUpMode? RequestedMode { get; init; }

    public FollowUpMode? EffectiveMode { get; init; }

    public static AgentSendResult Ok(
        string message,
        string? turnId = null,
        string? turnStatus = null,
        string? correlationId = null,
        FollowUpMode? requestedMode = null,
        FollowUpMode? effectiveMode = null)
        => new()
        {
            Success = true,
            Message = message,
            TurnId = turnId,
            TurnStatus = turnStatus,
            CorrelationId = correlationId,
            RequestedMode = requestedMode,
            EffectiveMode = effectiveMode,
        };

    public static AgentSendResult Fail(
        string message,
        string? turnId = null,
        string? turnStatus = null,
        string? correlationId = null,
        FollowUpMode? requestedMode = null,
        FollowUpMode? effectiveMode = null)
        => new()
        {
            Success = false,
            Message = message,
            TurnId = turnId,
            TurnStatus = turnStatus,
            CorrelationId = correlationId,
            RequestedMode = requestedMode,
            EffectiveMode = effectiveMode,
        };
}

internal sealed class AgentThreadListResult
{
    public IReadOnlyList<AgentThreadInfo> Data { get; init; } = Array.Empty<AgentThreadInfo>();

    public string? NextCursor { get; init; }
}

internal sealed class AgentThreadInfo
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

    public AgentThreadStatusInfo? Status { get; init; }

    public AgentThreadGitInfo? GitInfo { get; init; }

    public AgentThreadSessionConfiguration? SessionConfiguration { get; init; }
}

internal sealed class AgentThreadResumeResult
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

    public AgentThreadStatusInfo? Status { get; init; }

    public AgentThreadGitInfo? GitInfo { get; init; }

    public AgentThreadSessionConfiguration? SessionConfiguration { get; init; }

    public IReadOnlyList<ConversationMessage> Messages { get; init; } = Array.Empty<ConversationMessage>();

    public IReadOnlyList<AgentThreadTurn> Turns { get; init; } = Array.Empty<AgentThreadTurn>();

    public IReadOnlyList<AgentThreadSeedHistoryItem> SeedHistory { get; init; } = Array.Empty<AgentThreadSeedHistoryItem>();

    public ControlPlanePendingInputState? PendingInputState { get; init; }

    public IReadOnlyList<ControlPlanePendingInteractiveRequest> PendingInteractiveRequests { get; init; } = Array.Empty<ControlPlanePendingInteractiveRequest>();
}

internal sealed class AgentThreadSessionConfiguration
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

    public string? ForkedFromId { get; init; }

    public string? Cwd { get; init; }

    public ControlPlaneSessionSource? SessionSource { get; init; }

    public string? WindowsSandboxLevel { get; init; }

    public bool? DefaultModeRequestUserInputEnabled { get; init; }
}

internal sealed class AgentThreadTurn
{
    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public AgentThreadTurnError? Error { get; init; }

    public IReadOnlyList<AgentThreadTurnItem> Items { get; init; } = Array.Empty<AgentThreadTurnItem>();
}

internal class AgentThreadTurnItem
{
    public string Id { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string? Text { get; init; }

    public string? Phase { get; init; }
}

internal sealed class GenericThreadTurnItem : AgentThreadTurnItem
{
    public StructuredValue? RawData { get; init; }
}

internal sealed class AgentThreadTurnError
{
    public string? Message { get; init; }

    public string? AdditionalDetails { get; init; }
}

internal sealed class AgentThreadSeedHistoryItem
{
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<AgentUserInput> Inputs { get; init; } = Array.Empty<AgentUserInput>();
}

internal sealed class AgentThreadStatusInfo
{
    public string? Type { get; init; }

    public IReadOnlyList<string> ActiveFlags { get; init; } = Array.Empty<string>();
}

internal sealed class AgentThreadGitInfo
{
    public string? Sha { get; init; }

    public string? Branch { get; init; }

    public string? OriginUrl { get; init; }
}

internal sealed class PendingInteractiveRequestReplay
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

    public IReadOnlyList<ApprovalDecisionOptionPayload>? AvailableDecisionOptions { get; init; }

    public ApprovalRequestPayload? ApprovalRequest { get; init; }

    public PermissionRequestPayload? PermissionRequest { get; init; }

    public UserInputRequestPayload? UserInputRequest { get; init; }
}
