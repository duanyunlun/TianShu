using TianShu.Execution.Runtime;
using System.Text.Json.Serialization;

namespace TianShu.Execution.Runtime.Events;

public sealed record PlanStepEventPayload(
    int Sequence,
    string Step,
    string Status);

public sealed record PlanEventPayload(
    string? Explanation,
    IReadOnlyList<PlanStepEventPayload> Steps);

public sealed record ToolCallEventPayload(
    string? ItemId,
    string? CallId,
    string? ToolName,
    string? ServerName,
    string? InputText,
    string? OutputText,
    string? Status,
    string? Phase,
    bool? RequiresApproval);

public sealed record ServerRequestResolvedPayload(
    long RequestId,
    string? RequestKind,
    string? CallId,
    string? RequestIdRaw = null);

public sealed record TaskEventPayload(
    string? TaskType,
    string? Status);

public sealed record OperationEventPayload(
    string? OperationName,
    string? Phase);

public sealed record HookOutputEntryPayload(
    string Kind,
    string Text);

public sealed record HookRunPayload(
    string? Id,
    string? EventName,
    string? HandlerType,
    string? ExecutionMode,
    string? Scope,
    string? SourcePath,
    long? DisplayOrder,
    string? Status,
    string? StatusMessage,
    long? StartedAt,
    long? CompletedAt,
    long? DurationMs,
    IReadOnlyList<HookOutputEntryPayload> Entries);

public sealed record ReasoningEventPayload(
    string? ItemId,
    string? Status,
    string? Phase,
    string? Text,
    string? SourceMethod,
    long? SummaryIndex = null,
    long? ContentIndex = null);

public sealed record ModelReroutedPayload(
    string? FromModel,
    string? ToModel,
    string? Reason);

public sealed record ItemEventPayload(
    string? ItemId,
    string? ItemType,
    string? Status,
    string? Phase,
    string? Text,
    int ImageCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToolName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CallId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Arguments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? OutputText = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? InputText = null,
    IReadOnlyList<AgentUserInput>? Inputs = null);

public sealed record CommittedUserMessagePayload(
    string? ItemId,
    string Text,
    int ImageCount,
    string? CorrelationId = null,
    IReadOnlyList<AgentUserInput>? Inputs = null);

public sealed record PendingFollowUpCompareKeyPayload(
    string? Message,
    int ImageCount);

public sealed record PendingFollowUpLifecyclePayload(
    string CorrelationId,
    string RequestedMode,
    string EffectiveMode,
    string LifecycleState,
    string? ExpectedTurnId,
    string? TurnId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PendingFollowUpCompareKeyPayload? CompareKey);

public sealed record PendingInputStateEntryPayload(
    string CorrelationId,
    string RequestedMode,
    string EffectiveMode,
    string LifecycleState,
    string? ExpectedTurnId,
    string? TurnId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PendingFollowUpCompareKeyPayload? CompareKey,
    string PendingBucket = "QueuedUserMessage",
    IReadOnlyList<AgentUserInput>? Inputs = null);

public sealed record PendingInputStatePayload(
    IReadOnlyList<PendingInputStateEntryPayload> Entries,
    bool InterruptRequestPending,
    bool SubmitPendingSteersAfterInterrupt = false,
    IReadOnlyList<PendingInputStateEntryPayload>? QueuedUserMessages = null,
    IReadOnlyList<PendingInputStateEntryPayload>? PendingSteers = null);

public sealed record AgentJobProgressPayload(
    string JobId,
    int TotalItems,
    int PendingItems,
    int RunningItems,
    int CompletedItems,
    int FailedItems,
    int? EtaSeconds);

public sealed record McpServerEntryPayload(
    string Name,
    string? AuthStatus,
    int ToolCount,
    int ResourceCount,
    int ResourceTemplateCount);

public sealed record McpServerStatusPayload(
    int? Count,
    IReadOnlyList<McpServerEntryPayload> Servers);

public sealed record DeprecationNoticePayload(
    string Summary,
    string? Details);

public sealed record ConfigRangePositionPayload(
    int Line,
    int Column);

public sealed record ConfigRangePayload(
    ConfigRangePositionPayload Start,
    ConfigRangePositionPayload End);

public sealed record ConfigWarningPayload(
    string Summary,
    string? Details,
    string? Path,
    ConfigRangePayload? Range);

public sealed record ThreadStatusChangedPayload(
    string Type,
    IReadOnlyList<string> ActiveFlags);

public sealed record ThreadNameUpdatedPayload(
    string? ThreadName);

public sealed record TokenUsageBreakdownPayload(
    int TotalTokens,
    int InputTokens,
    int CachedInputTokens,
    int OutputTokens,
    int ReasoningOutputTokens);

public sealed record ThreadTokenUsagePayload(
    TokenUsageBreakdownPayload Last,
    TokenUsageBreakdownPayload Total,
    int? ModelContextWindow,
    bool Estimated = false,
    string? Source = null);

public sealed record CommandExecOutputDeltaPayload(
    string ProcessId,
    string Stream,
    string DeltaBase64,
    bool CapReached);

public sealed record AppListUpdatedEntryPayload(
    string Id,
    string Name,
    string? Description,
    string? LogoUrl,
    string? LogoUrlDark,
    string? DistributionChannel,
    AppBrandingPayload? Branding,
    AppMetadataPayload? AppMetadata,
    IReadOnlyDictionary<string, string> Labels,
    bool IsAccessible,
    bool IsEnabled,
    string? InstallUrl,
    IReadOnlyList<string> PluginDisplayNames)
{
    [JsonPropertyName("metadata")]
    public AppMetadataPayload? Metadata => AppMetadata;
}

public sealed record AppListUpdatedPayload(
    IReadOnlyList<AppListUpdatedEntryPayload> Items);

public sealed record AppBrandingPayload(
    string? Category,
    string? Developer,
    string? Website,
    string? PrivacyPolicy,
    string? TermsOfService,
    bool? IsDiscoverableApp);

public sealed record AppReviewPayload(
    string? Status,
    string? Message);

public sealed record AppScreenshotPayload(
    string? Caption,
    string? Url);

public sealed record AppMetadataPayload(
    AppReviewPayload? Review,
    IReadOnlyList<AppScreenshotPayload> Screenshots);

public sealed record WindowsSandboxSetupPayload(
    string? Mode,
    bool? Success,
    string? Error);

public sealed record McpServerOauthLoginPayload(
    string? Name,
    bool? Success,
    string? Error);

public sealed record RealtimeSessionPayload(
    string? ThreadId,
    string? SessionId);

public sealed record FuzzyFileSearchFilePayload(
    string? Path,
    string? FileName);

public sealed record FuzzyFileSearchSessionPayload(
    string? SessionId,
    IReadOnlyList<FuzzyFileSearchFilePayload> Files,
    bool IsCompleted);

public sealed record ThreadRealtimeItemAddedPayload(
    string? ItemId,
    string? ItemType,
    string? Role,
    string? Status,
    string? Text);

public sealed record ThreadRealtimeOutputAudioDeltaPayload(
    string Data,
    int SampleRate,
    int NumChannels,
    int? SamplesPerChannel);

public sealed record ThreadRealtimeErrorPayload(
    string Message);

public sealed record ThreadRealtimeClosedPayload(
    string? Reason);
