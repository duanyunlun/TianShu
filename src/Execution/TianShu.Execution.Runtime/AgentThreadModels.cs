using System.Text.Json;
using TianShu.Execution.Runtime.Models;
using TianShu.Execution.Runtime.Events;
using TianShu.Contracts.Conversations;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Runtime;

internal enum FollowUpMode
{
    Queue = 0,
    Steer = 1,
    Interrupt = 2,
}

internal sealed class AgentThreadSessionConfiguration
{
    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    public string? ModelProviderId { get; init; }

    public string? ModelRouteSetId { get; init; }

    public AgentServiceTier? ServiceTier { get; init; }

    public AgentApprovalPolicy? ApprovalPolicy { get; init; }

    public string? SandboxPolicy { get; init; }

    public AgentStructuredValue? SandboxPolicyPayload { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? HistoryLogId { get; init; }

    public int? HistoryEntryCount { get; init; }

    public string? RolloutPath { get; init; }

    public string? ForkedFromId { get; init; }

    public string? Cwd { get; init; }

    public bool? Ephemeral { get; init; }

    public bool? AllowLoginShell { get; init; }

    public AgentStructuredValue? ShellEnvironmentPolicy { get; init; }

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

    public IReadOnlyList<AgentStructuredValue>? DynamicTools { get; init; }

    public AgentStructuredValue? CollaborationMode { get; init; }

    public bool? PersistExtendedHistory { get; init; }

    public ControlPlaneSessionSource? SessionSource { get; init; }

    public string? WindowsSandboxLevel { get; init; }

    public bool? DefaultModeRequestUserInputEnabled { get; init; }
}

internal sealed class AgentThreadSessionProjection
{
    public string SessionId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string CollaborationSpaceId { get; init; } = string.Empty;

    public string CollaborationSpaceKey { get; init; } = string.Empty;

    public string CollaborationSpaceDisplayName { get; init; } = string.Empty;

    public string SessionMode { get; init; } = string.Empty;

    public bool IsClosed { get; init; }

    public string? ActiveThreadId { get; init; }

    public bool HasActiveTurn { get; init; }

    public AgentThreadOrchestrationProjection? Orchestration { get; init; }
}

internal sealed class AgentThreadOrchestrationProjection
{
    public string? CurrentStageId { get; init; }

    public AgentThreadOrchestratorDecisionProjection? LastDecision { get; init; }

    public AgentThreadStageContextPackageProjection? LastContextPackage { get; init; }

    public IReadOnlyList<AgentThreadStageContextSegmentProjection> ContextLedgerSegments { get; init; } = Array.Empty<AgentThreadStageContextSegmentProjection>();

    public IReadOnlyList<AgentThreadStageCheckpointProjection> Checkpoints { get; init; } = Array.Empty<AgentThreadStageCheckpointProjection>();
}

internal sealed class AgentThreadOrchestratorDecisionProjection
{
    public string DecisionId { get; init; } = string.Empty;

    public string SelectedStageId { get; init; } = string.Empty;

    public IReadOnlyList<string> CandidateStageIds { get; init; } = Array.Empty<string>();

    public string ReasonCode { get; init; } = string.Empty;

    public string? PreviousStageId { get; init; }

    public string? ContextProjectionReason { get; init; }

    public IReadOnlyList<string> PolicyHits { get; init; } = Array.Empty<string>();

    public DateTimeOffset DecidedAt { get; init; }
}

internal sealed class AgentThreadStageContextPackageProjection
{
    public string PackageId { get; init; } = string.Empty;

    public string StageId { get; init; } = string.Empty;

    public string ProjectionMode { get; init; } = string.Empty;

    public int? BudgetTokens { get; init; }

    public IReadOnlyList<string> SourceCheckpointIds { get; init; } = Array.Empty<string>();

    public int SegmentCount { get; init; }

    public int ArtifactRefCount { get; init; }
}

internal sealed class AgentThreadStageContextSegmentProjection
{
    public string Kind { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string? Title { get; init; }

    public AgentThreadResourceRefProjection? Source { get; init; }

    public bool Required { get; init; }

    public int? EstimatedTokens { get; init; }
}

internal sealed class AgentThreadStageCheckpointProjection
{
    public string CheckpointId { get; init; } = string.Empty;

    public string StageId { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Summary { get; init; }

    public IReadOnlyList<AgentThreadArtifactRefProjection> ArtifactRefs { get; init; } = Array.Empty<AgentThreadArtifactRefProjection>();

    public string? ModelRouteSetId { get; init; }

    public string? ModelRouteKind { get; init; }

    public JsonElement? Diagnostics { get; init; }

    public IReadOnlyList<string> NextStageSuggestions { get; init; } = Array.Empty<string>();
}

internal sealed class AgentThreadArtifactRefProjection
{
    public string Id { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Kind { get; init; }
}

internal sealed class AgentThreadResourceRefProjection
{
    public string Kind { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;
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

    public AgentThreadStatus? Status { get; init; }

    public AgentThreadGitInfo? GitInfo { get; init; }

    public AgentThreadSessionConfiguration? SessionConfiguration { get; init; }

    public AgentThreadSessionProjection? SessionState { get; init; }
}

internal sealed class AgentThreadListResult
{
    public IReadOnlyList<AgentThreadInfo> Data { get; init; } = Array.Empty<AgentThreadInfo>();

    public string? NextCursor { get; init; }
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

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    public bool IsEphemeral { get; init; }

    public AgentThreadStatus? Status { get; init; }

    public AgentThreadGitInfo? GitInfo { get; init; }

    public IReadOnlyList<AgentThreadTurn> Turns { get; init; } = Array.Empty<AgentThreadTurn>();

    public IReadOnlyList<AgentThreadSeedHistoryItem> SeedHistory { get; init; } = Array.Empty<AgentThreadSeedHistoryItem>();

    public IReadOnlyList<ConversationMessage> Messages { get; init; } = Array.Empty<ConversationMessage>();

    public ControlPlanePendingInputState? PendingInputState { get; init; }

    public IReadOnlyList<ControlPlanePendingInteractiveRequest> PendingInteractiveRequests { get; init; } = Array.Empty<ControlPlanePendingInteractiveRequest>();

    public AgentThreadSessionConfiguration? SessionConfiguration { get; init; }

    public AgentThreadSessionProjection? SessionState { get; init; }
}

internal sealed class AgentThreadDetails
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

    public AgentThreadStatus? Status { get; init; }

    public AgentThreadGitInfo? GitInfo { get; init; }

    public IReadOnlyList<AgentThreadTurn> Turns { get; init; } = Array.Empty<AgentThreadTurn>();

    public IReadOnlyList<AgentThreadSeedHistoryItem> SeedHistory { get; init; } = Array.Empty<AgentThreadSeedHistoryItem>();

    public ControlPlanePendingInputState? PendingInputState { get; set; }

    public IReadOnlyList<ControlPlanePendingInteractiveRequest> PendingInteractiveRequests { get; set; } = Array.Empty<ControlPlanePendingInteractiveRequest>();

    public AgentThreadSessionConfiguration? SessionConfiguration { get; init; }

    public AgentThreadSessionProjection? SessionState { get; init; }
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

internal sealed class AgentThreadStatus
{
    public string Type { get; init; } = string.Empty;

    public IReadOnlyList<string> ActiveFlags { get; init; } = Array.Empty<string>();
}

internal sealed class AgentThreadGitInfo
{
    public string? Sha { get; init; }

    public string? Branch { get; init; }

    public string? OriginUrl { get; init; }
}

internal sealed class AgentThreadTurn
{
    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public AgentThreadTurnError? Error { get; init; }

    public IReadOnlyList<AgentThreadTurnItem> Items { get; init; } = Array.Empty<AgentThreadTurnItem>();
}

internal sealed class AgentThreadTurnError
{
    public string? Message { get; init; }

    public string? AdditionalDetails { get; init; }
}

internal abstract class AgentThreadTurnItem
{
    public string Id { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public virtual string? Text => null;

    public virtual string? Phase => null;
}

internal sealed class GenericThreadTurnItem : AgentThreadTurnItem
{
    public string? RawText { get; init; }

    public string? ItemPhase { get; init; }

    public AgentStructuredValue? RawData { get; init; }

    public override string? Text => RawText;

    public override string? Phase => ItemPhase;
}

public abstract class AgentUserInput
{
    public string Type { get; init; } = string.Empty;
}

public sealed class TextUserInput : AgentUserInput
{
    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<AgentTextElement> TextElements { get; init; } = Array.Empty<AgentTextElement>();
}

public sealed class ImageUserInput : AgentUserInput
{
    public string Url { get; init; } = string.Empty;
}

public sealed class LocalImageUserInput : AgentUserInput
{
    public string Path { get; init; } = string.Empty;
}

public sealed class SkillUserInput : AgentUserInput
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;
}

public sealed class MentionUserInput : AgentUserInput
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;
}

public sealed class AgentByteRange
{
    public int Start { get; init; }

    public int End { get; init; }
}

public sealed class AgentTextElement
{
    public AgentByteRange ByteRange { get; init; } = new();

    public string? Placeholder { get; init; }
}

internal sealed class UserMessageThreadItem : AgentThreadTurnItem
{
    public IReadOnlyList<AgentUserInput> Content { get; init; } = Array.Empty<AgentUserInput>();

    public override string? Text => AgentThreadTurnItemTextFormatter.FormatUserInputs(Content);
}

internal sealed class AgentMessageThreadItem : AgentThreadTurnItem
{
    public string MessageText { get; init; } = string.Empty;

    public string? MessagePhase { get; init; }

    public override string? Text => MessageText;

    public override string? Phase => MessagePhase;
}

internal sealed class PlanThreadItem : AgentThreadTurnItem
{
    public string PlanText { get; init; } = string.Empty;

    public override string? Text => PlanText;
}

internal sealed class ReasoningThreadItem : AgentThreadTurnItem
{
    public IReadOnlyList<string> Summary { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Content { get; init; } = Array.Empty<string>();

    public override string? Text => AgentThreadTurnItemTextFormatter.FormatReasoning(Summary, Content);
}

internal sealed class AgentCommandAction
{
    public string Type { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Path { get; init; }

    public string? Query { get; init; }
}

internal sealed class CommandExecutionThreadItem : AgentThreadTurnItem
{
    public string Command { get; init; } = string.Empty;

    public IReadOnlyList<AgentCommandAction> CommandActions { get; init; } = Array.Empty<AgentCommandAction>();

    public string Cwd { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? AggregatedOutput { get; init; }

    public int? ExitCode { get; init; }

    public int? DurationMs { get; init; }

    public string? ProcessId { get; init; }

    public override string? Text => string.IsNullOrWhiteSpace(AggregatedOutput) ? Command : AggregatedOutput;
}

internal sealed class AgentFileUpdateChange
{
    public string Path { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Diff { get; init; } = string.Empty;
}

internal sealed class FileChangeThreadItem : AgentThreadTurnItem
{
    public string Status { get; init; } = string.Empty;

    public IReadOnlyList<AgentFileUpdateChange> Changes { get; init; } = Array.Empty<AgentFileUpdateChange>();

    public override string? Text => AgentThreadTurnItemTextFormatter.FormatChangedPaths(Changes);
}

internal sealed class AgentMcpToolCallError
{
    public string Message { get; init; } = string.Empty;
}

internal sealed class AgentMcpToolCallResult
{
    public IReadOnlyList<AgentStructuredValue> Content { get; init; } = Array.Empty<AgentStructuredValue>();

    public AgentStructuredValue? StructuredContent { get; init; }
}

internal sealed class McpToolCallThreadItem : AgentThreadTurnItem
{
    public string Server { get; init; } = string.Empty;

    public string Tool { get; init; } = string.Empty;

    public AgentStructuredValue? Arguments { get; init; }

    public string Status { get; init; } = string.Empty;

    public AgentMcpToolCallResult? Result { get; init; }

    public AgentMcpToolCallError? Error { get; init; }

    public int? DurationMs { get; init; }

    public override string? Text => AgentThreadTurnItemTextFormatter.FormatMcpResult(Tool, Result, Error);
}

internal sealed class AgentDynamicToolCallContentItem
{
    public string Type { get; init; } = string.Empty;

    public string? Text { get; init; }

    public string? ImageUrl { get; init; }
}

internal sealed class DynamicToolCallThreadItem : AgentThreadTurnItem
{
    public string Tool { get; init; } = string.Empty;

    public AgentStructuredValue? Arguments { get; init; }

    public IReadOnlyList<AgentDynamicToolCallContentItem> ContentItems { get; init; } = Array.Empty<AgentDynamicToolCallContentItem>();

    public string Status { get; init; } = string.Empty;

    public bool? Success { get; init; }

    public int? DurationMs { get; init; }

    public override string? Text => AgentThreadTurnItemTextFormatter.FormatDynamicToolContent(Tool, ContentItems);
}

internal sealed class AgentCollabAgentState
{
    public string? Status { get; init; }

    public string? Message { get; init; }
}

internal sealed class CollabAgentToolCallThreadItem : AgentThreadTurnItem
{
    public string SenderThreadId { get; init; } = string.Empty;

    public IReadOnlyList<string> ReceiverThreadIds { get; init; } = Array.Empty<string>();

    public string Tool { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, AgentCollabAgentState> AgentsStates { get; init; }
        = new Dictionary<string, AgentCollabAgentState>(StringComparer.Ordinal);

    public string? Model { get; init; }

    public string? Prompt { get; init; }

    public string? ReasoningEffort { get; init; }

    public override string? Text => Prompt;
}

internal sealed class AgentWebSearchAction
{
    public string Type { get; init; } = string.Empty;

    public string? Query { get; init; }

    public IReadOnlyList<string> Queries { get; init; } = Array.Empty<string>();

    public string? Url { get; init; }

    public string? Pattern { get; init; }
}

internal sealed class WebSearchThreadItem : AgentThreadTurnItem
{
    public string Query { get; init; } = string.Empty;

    public AgentWebSearchAction? Action { get; init; }

    public override string? Text => Query;
}

internal sealed class ImageViewThreadItem : AgentThreadTurnItem
{
    public string Path { get; init; } = string.Empty;

    public override string? Text => Path;
}

internal sealed class ImageGenerationThreadItem : AgentThreadTurnItem
{
    public string Result { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? RevisedPrompt { get; init; }

    public override string? Text => Result;
}

internal sealed class EnteredReviewModeThreadItem : AgentThreadTurnItem
{
    public string Review { get; init; } = string.Empty;

    public override string? Text => Review;
}

internal sealed class ExitedReviewModeThreadItem : AgentThreadTurnItem
{
    public string Review { get; init; } = string.Empty;

    public override string? Text => Review;
}

internal sealed class ContextCompactionThreadItem : AgentThreadTurnItem
{
}

internal static class AgentThreadTurnItemTextFormatter
{
    public static string? FormatUserInputs(IReadOnlyList<AgentUserInput> content)
    {
        if (content.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var item in content)
        {
            switch (item)
            {
                case TextUserInput text when !string.IsNullOrWhiteSpace(text.Text):
                    parts.Add(text.Text);
                    break;
                case SkillUserInput skill when !string.IsNullOrWhiteSpace(skill.Name):
                    parts.Add(skill.Name);
                    break;
                case MentionUserInput mention when !string.IsNullOrWhiteSpace(mention.Name):
                    parts.Add(mention.Name);
                    break;
                case LocalImageUserInput image when !string.IsNullOrWhiteSpace(image.Path):
                    parts.Add(image.Path);
                    break;
                case ImageUserInput image when !string.IsNullOrWhiteSpace(image.Url):
                    parts.Add(image.Url);
                    break;
            }
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    public static string? FormatReasoning(IReadOnlyList<string> summary, IReadOnlyList<string> content)
    {
        var parts = summary.Count > 0 ? summary : content;
        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    public static string? FormatChangedPaths(IReadOnlyList<AgentFileUpdateChange> changes)
    {
        if (changes.Count == 0)
        {
            return null;
        }

        var paths = changes
            .Select(static item => item.Path)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return paths.Length == 0 ? null : string.Join(Environment.NewLine, paths);
    }

    public static string? FormatMcpResult(string tool, AgentMcpToolCallResult? result, AgentMcpToolCallError? error)
    {
        if (!string.IsNullOrWhiteSpace(error?.Message))
        {
            return error.Message;
        }

        if (result is not null)
        {
            var text = string.Join(
                Environment.NewLine,
                result.Content
                    .Select(static item => item.Kind is AgentStructuredValueKind.String or AgentStructuredValueKind.Number or AgentStructuredValueKind.Boolean
                        ? item.GetString()
                        : null)
                    .Where(static item => !string.IsNullOrWhiteSpace(item)));
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return tool;
    }

    public static string? FormatDynamicToolContent(string tool, IReadOnlyList<AgentDynamicToolCallContentItem> items)
    {
        if (items.Count == 0)
        {
            return tool;
        }

        var text = string.Join(
            Environment.NewLine,
            items
                .Select(static item => !string.IsNullOrWhiteSpace(item.Text) ? item.Text : item.ImageUrl)
                .Where(static item => !string.IsNullOrWhiteSpace(item)));
        return string.IsNullOrWhiteSpace(text) ? tool : text;
    }
}

internal sealed class AgentThreadSeedHistoryItem
{
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<AgentUserInput> Inputs { get; init; } = Array.Empty<AgentUserInput>();
}
