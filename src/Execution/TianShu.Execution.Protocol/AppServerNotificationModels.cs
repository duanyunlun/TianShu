using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.Execution.Protocol;

internal sealed class AppServerThreadReferenceDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

internal sealed class AppServerTurnReferenceDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("error")]
    public AppServerErrorDto? Error { get; init; }
}

internal sealed class AppServerPlanStepDto
{
    [JsonPropertyName("step")]
    public string? Step { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

internal sealed class TurnPlanUpdatedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("explanation")]
    public string? Explanation { get; init; }

    [JsonPropertyName("plan")]
    public IReadOnlyList<AppServerPlanStepDto>? Plan { get; init; }
}

internal sealed class ThreadStartedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("thread")]
    public AppServerThreadReferenceDto? Thread { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }
}

internal sealed class ThreadClosedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }
}

internal sealed class TurnStartedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("turn")]
    public AppServerTurnReferenceDto? Turn { get; init; }
}

internal sealed class TurnDiffUpdatedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("diff")]
    public string? Diff { get; init; }
}

internal sealed class TurnCompletedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("turn")]
    public AppServerTurnReferenceDto? Turn { get; init; }
}

internal sealed class ServerRequestResolvedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("requestId")]
    public JsonElement? RequestId { get; init; }
}

internal sealed class AppServerResponseItemDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("phase")]
    public string? Phase { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("outputText")]
    public string? OutputText { get; init; }

    [JsonPropertyName("delta")]
    public string? Delta { get; init; }

    [JsonPropertyName("output")]
    public string? Output { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }

    [JsonPropertyName("input")]
    public string? Input { get; init; }

    [JsonPropertyName("content")]
    public JsonElement? Content { get; init; }
}

internal sealed class RawResponseItemCompletedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("item")]
    public AppServerResponseItemDto? Item { get; init; }
}

internal sealed class AppServerApprovalStateDto
{
    [JsonPropertyName("required")]
    public bool? Required { get; init; }
}

internal sealed class ItemNotificationParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("itemId")]
    public string? ItemId { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("phase")]
    public string? Phase { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("toolCallId")]
    public string? ToolCallId { get; init; }

    [JsonPropertyName("delta")]
    public string? Delta { get; init; }

    [JsonPropertyName("output")]
    public string? Output { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }

    [JsonPropertyName("input")]
    public string? Input { get; init; }

    [JsonPropertyName("requiresApproval")]
    public bool? RequiresApproval { get; init; }

    [JsonPropertyName("approvalRequired")]
    public bool? ApprovalRequired { get; init; }

    [JsonPropertyName("approval")]
    public AppServerApprovalStateDto? Approval { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("summaryIndex")]
    public long? SummaryIndex { get; init; }

    [JsonPropertyName("contentIndex")]
    public long? ContentIndex { get; init; }

    [JsonPropertyName("processId")]
    public string? ProcessId { get; init; }

    [JsonPropertyName("stdin")]
    public string? Stdin { get; init; }

    [JsonPropertyName("item")]
    public AppServerResponseItemDto? Item { get; init; }
}

internal sealed class AppServerErrorDto
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("additionalDetails")]
    public string? AdditionalDetails { get; init; }
}

internal sealed class ErrorNotificationParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("error")]
    public AppServerErrorDto? Error { get; init; }

    [JsonPropertyName("willRetry")]
    public bool? WillRetry { get; init; }
}

internal sealed class HookOutputEntryDto
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class HookRunSummaryDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("eventName")]
    public string? EventName { get; init; }

    [JsonPropertyName("handlerType")]
    public string? HandlerType { get; init; }

    [JsonPropertyName("executionMode")]
    public string? ExecutionMode { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; init; }

    [JsonPropertyName("displayOrder")]
    public long? DisplayOrder { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; init; }

    [JsonPropertyName("startedAt")]
    public long? StartedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public long? CompletedAt { get; init; }

    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; init; }

    [JsonPropertyName("entries")]
    public IReadOnlyList<HookOutputEntryDto>? Entries { get; init; }
}

internal sealed class HookStartedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("run")]
    public HookRunSummaryDto? Run { get; init; }
}

internal sealed class HookCompletedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("run")]
    public HookRunSummaryDto? Run { get; init; }
}

internal sealed class ModelReroutedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("fromModel")]
    public string? FromModel { get; init; }

    [JsonPropertyName("toModel")]
    public string? ToModel { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

internal sealed class TurnSteeredParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

internal sealed class McpServerStatusListUpdatedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("data")]
    public IReadOnlyList<McpServerStatusEntryDto>? Data { get; init; }
}

internal sealed class McpServerStatusEntryDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("authStatus")]
    public string? AuthStatus { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyDictionary<string, JsonElement>? Tools { get; init; }

    [JsonPropertyName("resources")]
    public IReadOnlyList<JsonElement>? Resources { get; init; }

    [JsonPropertyName("resourceTemplates")]
    public IReadOnlyList<JsonElement>? ResourceTemplates { get; init; }
}

internal sealed class WindowsSandboxSetupCompletedParamsDto
{
    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

internal sealed class McpServerOauthLoginCompletedParamsDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

internal sealed class ThreadRealtimeStartedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }
}

internal sealed class FuzzyFileSearchFileDto
{
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }
}

internal sealed class FuzzyFileSearchSessionUpdatedParamsDto
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("files")]
    public IReadOnlyList<FuzzyFileSearchFileDto>? Files { get; init; }
}

internal sealed class FuzzyFileSearchSessionCompletedParamsDto
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }
}

internal sealed class DeprecationNoticeParamsDto
{
    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("details")]
    public string? Details { get; init; }
}

internal sealed class AppServerConfigRangePositionDto
{
    [JsonPropertyName("line")]
    public int? Line { get; init; }

    [JsonPropertyName("column")]
    public int? Column { get; init; }
}

internal sealed class AppServerConfigRangeDto
{
    [JsonPropertyName("start")]
    public AppServerConfigRangePositionDto? Start { get; init; }

    [JsonPropertyName("end")]
    public AppServerConfigRangePositionDto? End { get; init; }
}

internal sealed class ConfigWarningParamsDto
{
    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("details")]
    public string? Details { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("range")]
    public AppServerConfigRangeDto? Range { get; init; }
}

internal sealed class AppServerThreadStatusDto
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("activeFlags")]
    public IReadOnlyList<string>? ActiveFlags { get; init; }
}

internal sealed class ThreadStatusChangedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("status")]
    public AppServerThreadStatusDto? Status { get; init; }
}

internal sealed class ThreadNameUpdatedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("threadName")]
    public string? ThreadName { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class AppServerTokenUsageBreakdownDto
{
    [JsonPropertyName("totalTokens")]
    public int? TotalTokens { get; init; }

    [JsonPropertyName("inputTokens")]
    public int? InputTokens { get; init; }

    [JsonPropertyName("cachedInputTokens")]
    public int? CachedInputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    public int? OutputTokens { get; init; }

    [JsonPropertyName("reasoningOutputTokens")]
    public int? ReasoningOutputTokens { get; init; }
}

internal sealed class AppServerThreadTokenUsageDto
{
    [JsonPropertyName("last")]
    public AppServerTokenUsageBreakdownDto? Last { get; init; }

    [JsonPropertyName("total")]
    public AppServerTokenUsageBreakdownDto? Total { get; init; }

    [JsonPropertyName("modelContextWindow")]
    public int? ModelContextWindow { get; init; }

    [JsonPropertyName("estimated")]
    public bool? Estimated { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

internal sealed class ThreadTokenUsageUpdatedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("tokenUsage")]
    public AppServerThreadTokenUsageDto? TokenUsage { get; init; }
}

internal sealed class CommandExecOutputDeltaParamsDto
{
    [JsonPropertyName("processId")]
    public string? ProcessId { get; init; }

    [JsonPropertyName("stream")]
    public string? Stream { get; init; }

    [JsonPropertyName("deltaBase64")]
    public string? DeltaBase64 { get; init; }

    [JsonPropertyName("capReached")]
    public bool? CapReached { get; init; }
}

internal sealed class AppServerAppBrandingDto
{
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("developer")]
    public string? Developer { get; init; }

    [JsonPropertyName("website")]
    public string? Website { get; init; }

    [JsonPropertyName("privacyPolicy")]
    public string? PrivacyPolicy { get; init; }

    [JsonPropertyName("termsOfService")]
    public string? TermsOfService { get; init; }

    [JsonPropertyName("isDiscoverableApp")]
    public bool? IsDiscoverableApp { get; init; }
}

internal sealed class AppServerAppReviewDto
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

internal sealed class AppServerAppScreenshotDto
{
    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

internal sealed class AppServerAppMetadataDto
{
    [JsonPropertyName("review")]
    public AppServerAppReviewDto? Review { get; init; }

    [JsonPropertyName("screenshots")]
    public IReadOnlyList<AppServerAppScreenshotDto>? Screenshots { get; init; }
}

internal sealed class AppListUpdatedEntryDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("logoUrlDark")]
    public string? LogoUrlDark { get; init; }

    [JsonPropertyName("distributionChannel")]
    public string? DistributionChannel { get; init; }

    [JsonPropertyName("branding")]
    public AppServerAppBrandingDto? Branding { get; init; }

    [JsonPropertyName("appMetadata")]
    public AppServerAppMetadataDto? AppMetadata { get; init; }

    [JsonPropertyName("metadata")]
    public AppServerAppMetadataDto? Metadata { get; init; }

    [JsonPropertyName("labels")]
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    [JsonPropertyName("isAccessible")]
    public bool? IsAccessible { get; init; }

    [JsonPropertyName("isEnabled")]
    public bool? IsEnabled { get; init; }

    [JsonPropertyName("installUrl")]
    public string? InstallUrl { get; init; }

    [JsonPropertyName("pluginDisplayNames")]
    public IReadOnlyList<string>? PluginDisplayNames { get; init; }
}

internal sealed class AppListUpdatedParamsDto
{
    [JsonPropertyName("data")]
    public IReadOnlyList<AppListUpdatedEntryDto>? Data { get; init; }
}

internal sealed class ThreadRealtimeItemAddedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("item")]
    public JsonElement? Item { get; init; }
}

internal sealed class AppServerRealtimeAudioDto
{
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    [JsonPropertyName("sampleRate")]
    public int? SampleRate { get; init; }

    [JsonPropertyName("numChannels")]
    public int? NumChannels { get; init; }

    [JsonPropertyName("samplesPerChannel")]
    public int? SamplesPerChannel { get; init; }
}

internal sealed class ThreadRealtimeOutputAudioDeltaParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("audio")]
    public AppServerRealtimeAudioDto? Audio { get; init; }
}

internal sealed class ThreadRealtimeErrorParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

internal sealed class ThreadRealtimeClosedParamsDto
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
