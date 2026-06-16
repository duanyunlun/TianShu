using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.Contracts.Conversations;

namespace TianShu.Execution.Protocol;

internal sealed class AppServerThreadSessionConfigurationDto
{
    [JsonConstructor]
    public AppServerThreadSessionConfigurationDto()
    {
    }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("modelProvider")]
    public string? ModelProvider { get; init; }

    [JsonPropertyName("modelProviderId")]
    public string? ModelProviderId { get; init; }

    [JsonPropertyName("modelRouteSetId")]
    public string? ModelRouteSetId { get; init; }

    [JsonPropertyName("model_route_set")]
    public string? LegacyModelRouteSetId { get; init; }

    [JsonPropertyName("serviceTier")]
    public string? ServiceTier { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public string? ApprovalPolicy { get; init; }

    [JsonPropertyName("sandboxPolicy")]
    public JsonElement SandboxPolicy { get; init; }

    [JsonPropertyName("sandboxPolicyPayload")]
    public JsonElement SandboxPolicyPayload { get; init; }

    [JsonPropertyName("sandbox")]
    public JsonElement Sandbox { get; init; }

    [JsonPropertyName("reasoningEffort")]
    public string? ReasoningEffort { get; init; }

    [JsonPropertyName("reasoning_effort")]
    public string? LegacyReasoningEffort { get; init; }

    [JsonPropertyName("historyLogId")]
    public string? HistoryLogId { get; init; }

    [JsonPropertyName("historyEntryCount")]
    public int? HistoryEntryCount { get; init; }

    [JsonPropertyName("rolloutPath")]
    public string? RolloutPath { get; init; }

    [JsonPropertyName("forkedFromId")]
    public string? ForkedFromId { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("ephemeral")]
    public bool? Ephemeral { get; init; }

    [JsonPropertyName("allowLoginShell")]
    public bool? AllowLoginShell { get; init; }

    [JsonPropertyName("shellEnvironmentPolicy")]
    public JsonElement ShellEnvironmentPolicy { get; init; }

    [JsonPropertyName("providerBaseUrl")]
    public string? ProviderBaseUrl { get; init; }

    [JsonPropertyName("providerApiKeyEnvironmentVariable")]
    public string? ProviderApiKeyEnvironmentVariable { get; init; }

    [JsonPropertyName("providerWireApi")]
    public string? ProviderWireApi { get; init; }

    [JsonPropertyName("providerRequestMaxRetries")]
    public int? ProviderRequestMaxRetries { get; init; }

    [JsonPropertyName("providerStreamMaxRetries")]
    public int? ProviderStreamMaxRetries { get; init; }

    [JsonPropertyName("providerStreamIdleTimeoutMs")]
    public long? ProviderStreamIdleTimeoutMs { get; init; }

    [JsonPropertyName("providerWebsocketConnectTimeoutMs")]
    public long? ProviderWebsocketConnectTimeoutMs { get; init; }

    [JsonPropertyName("providerSupportsWebsockets")]
    public bool? ProviderSupportsWebsockets { get; init; }

    [JsonPropertyName("webSearchMode")]
    public string? WebSearchMode { get; init; }

    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; init; }

    [JsonPropertyName("baseInstructions")]
    public string? BaseInstructions { get; init; }

    [JsonPropertyName("developerInstructions")]
    public string? DeveloperInstructions { get; init; }

    [JsonPropertyName("userInstructions")]
    public string? UserInstructions { get; init; }

    [JsonPropertyName("reasoningSummary")]
    public string? ReasoningSummary { get; init; }

    [JsonPropertyName("verbosity")]
    public string? Verbosity { get; init; }

    [JsonPropertyName("personality")]
    public string? Personality { get; init; }

    [JsonPropertyName("dynamicTools")]
    public JsonElement DynamicTools { get; init; }

    [JsonPropertyName("collaborationMode")]
    public JsonElement CollaborationMode { get; init; }

    [JsonPropertyName("persistExtendedHistory")]
    public bool? PersistExtendedHistory { get; init; }

    [JsonPropertyName("sessionSource")]
    public JsonElement SessionSource { get; init; }

    [JsonPropertyName("windowsSandboxLevel")]
    public string? WindowsSandboxLevel { get; init; }

    [JsonPropertyName("defaultModeRequestUserInputEnabled")]
    public bool? DefaultModeRequestUserInputEnabled { get; init; }

    [JsonIgnore]
    public string? ResolvedModelProvider => Normalize(ModelProvider) ?? Normalize(ModelProviderId);

    [JsonIgnore]
    public string? ResolvedModelRouteSetId => Normalize(ModelRouteSetId) ?? Normalize(LegacyModelRouteSetId);

    [JsonIgnore]
    public string? ResolvedSandboxPolicy => ResolveSandboxMode(SandboxPolicy) ?? ResolveSandboxMode(Sandbox);

    [JsonIgnore]
    public string? ResolvedReasoningEffort => Normalize(ReasoningEffort) ?? Normalize(LegacyReasoningEffort);

    public bool HasAnyValue(string? rolloutPathFallback)
    {
        return !string.IsNullOrWhiteSpace(Normalize(Model))
               || !string.IsNullOrWhiteSpace(ResolvedModelProvider)
               || !string.IsNullOrWhiteSpace(ResolvedModelRouteSetId)
               || !string.IsNullOrWhiteSpace(Normalize(ServiceTier))
               || !string.IsNullOrWhiteSpace(Normalize(ApprovalPolicy))
               || !string.IsNullOrWhiteSpace(ResolvedSandboxPolicy)
               || HasStructuredValue(SandboxPolicyPayload)
               || !string.IsNullOrWhiteSpace(ResolvedReasoningEffort)
               || !string.IsNullOrWhiteSpace(Normalize(HistoryLogId))
               || HistoryEntryCount is not null
               || !string.IsNullOrWhiteSpace(Normalize(RolloutPath) ?? Normalize(rolloutPathFallback))
               || !string.IsNullOrWhiteSpace(Normalize(ForkedFromId))
               || !string.IsNullOrWhiteSpace(Normalize(Cwd))
               || Ephemeral is not null
               || AllowLoginShell is not null
               || HasStructuredValue(ShellEnvironmentPolicy)
               || !string.IsNullOrWhiteSpace(Normalize(ProviderBaseUrl))
               || !string.IsNullOrWhiteSpace(Normalize(ProviderApiKeyEnvironmentVariable))
               || !string.IsNullOrWhiteSpace(Normalize(ProviderWireApi))
               || ProviderRequestMaxRetries is not null
               || ProviderStreamMaxRetries is not null
               || ProviderStreamIdleTimeoutMs is not null
               || ProviderWebsocketConnectTimeoutMs is not null
               || ProviderSupportsWebsockets is not null
               || !string.IsNullOrWhiteSpace(Normalize(WebSearchMode))
               || !string.IsNullOrWhiteSpace(Normalize(ServiceName))
               || !string.IsNullOrWhiteSpace(Normalize(BaseInstructions))
               || !string.IsNullOrWhiteSpace(Normalize(DeveloperInstructions))
               || !string.IsNullOrWhiteSpace(Normalize(UserInstructions))
               || !string.IsNullOrWhiteSpace(Normalize(ReasoningSummary))
               || !string.IsNullOrWhiteSpace(Normalize(Verbosity))
               || !string.IsNullOrWhiteSpace(Normalize(Personality))
               || HasStructuredValue(DynamicTools)
               || HasStructuredValue(CollaborationMode)
               || PersistExtendedHistory is not null
               || HasStructuredValue(SessionSource)
               || !string.IsNullOrWhiteSpace(Normalize(WindowsSandboxLevel))
               || DefaultModeRequestUserInputEnabled is not null;
    }

    private static string? ResolveSandboxMode(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => Normalize(element.GetString()),
            JsonValueKind.Object when element.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                => Normalize(type.GetString()),
            _ => null,
        };
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool HasStructuredValue(JsonElement element)
        => element.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
}

internal sealed class AppServerThreadGitInfoDto
{
    [JsonConstructor]
    public AppServerThreadGitInfoDto()
    {
    }

    [JsonPropertyName("sha")]
    public string? Sha { get; init; }

    [JsonPropertyName("branch")]
    public string? Branch { get; init; }

    [JsonPropertyName("originUrl")]
    public string? OriginUrl { get; init; }
}

internal sealed class AppServerThreadSessionProjectionDto
{
    [JsonConstructor]
    public AppServerThreadSessionProjectionDto()
    {
    }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("collaborationSpaceId")]
    public string? CollaborationSpaceId { get; init; }

    [JsonPropertyName("collaborationSpaceKey")]
    public string? CollaborationSpaceKey { get; init; }

    [JsonPropertyName("collaborationSpaceDisplayName")]
    public string? CollaborationSpaceDisplayName { get; init; }

    [JsonPropertyName("sessionMode")]
    public string? SessionMode { get; init; }

    [JsonPropertyName("isClosed")]
    public bool? IsClosed { get; init; }

    [JsonPropertyName("activeThreadId")]
    public string? ActiveThreadId { get; init; }

    [JsonPropertyName("hasActiveTurn")]
    public bool? HasActiveTurn { get; init; }

    [JsonPropertyName("orchestration")]
    public AppServerThreadOrchestrationProjectionDto? Orchestration { get; init; }
}

internal sealed class AppServerThreadOrchestrationProjectionDto
{
    [JsonConstructor]
    public AppServerThreadOrchestrationProjectionDto()
    {
    }

    [JsonPropertyName("currentStageId")]
    public string? CurrentStageId { get; init; }

    [JsonPropertyName("lastDecision")]
    public AppServerThreadOrchestratorDecisionProjectionDto? LastDecision { get; init; }

    [JsonPropertyName("lastContextPackage")]
    public AppServerThreadStageContextPackageProjectionDto? LastContextPackage { get; init; }

    [JsonPropertyName("contextLedgerSegments")]
    public IReadOnlyList<AppServerThreadStageContextSegmentProjectionDto>? ContextLedgerSegments { get; init; }

    [JsonPropertyName("checkpoints")]
    public IReadOnlyList<AppServerThreadStageCheckpointProjectionDto>? Checkpoints { get; init; }
}

internal sealed class AppServerThreadOrchestratorDecisionProjectionDto
{
    [JsonConstructor]
    public AppServerThreadOrchestratorDecisionProjectionDto()
    {
    }

    [JsonPropertyName("decisionId")]
    public string? DecisionId { get; init; }

    [JsonPropertyName("selectedStageId")]
    public string? SelectedStageId { get; init; }

    [JsonPropertyName("candidateStageIds")]
    public IReadOnlyList<string>? CandidateStageIds { get; init; }

    [JsonPropertyName("reasonCode")]
    public string? ReasonCode { get; init; }

    [JsonPropertyName("previousStageId")]
    public string? PreviousStageId { get; init; }

    [JsonPropertyName("contextProjectionReason")]
    public string? ContextProjectionReason { get; init; }

    [JsonPropertyName("policyHits")]
    public IReadOnlyList<string>? PolicyHits { get; init; }

    [JsonPropertyName("decidedAt")]
    public DateTimeOffset? DecidedAt { get; init; }
}

internal sealed class AppServerThreadStageContextPackageProjectionDto
{
    [JsonConstructor]
    public AppServerThreadStageContextPackageProjectionDto()
    {
    }

    [JsonPropertyName("packageId")]
    public string? PackageId { get; init; }

    [JsonPropertyName("stageId")]
    public string? StageId { get; init; }

    [JsonPropertyName("projectionMode")]
    public string? ProjectionMode { get; init; }

    [JsonPropertyName("budgetTokens")]
    public int? BudgetTokens { get; init; }

    [JsonPropertyName("sourceCheckpointIds")]
    public IReadOnlyList<string>? SourceCheckpointIds { get; init; }

    [JsonPropertyName("segmentCount")]
    public int? SegmentCount { get; init; }

    [JsonPropertyName("artifactRefCount")]
    public int? ArtifactRefCount { get; init; }
}

internal sealed class AppServerThreadStageContextSegmentProjectionDto
{
    [JsonConstructor]
    public AppServerThreadStageContextSegmentProjectionDto()
    {
    }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("source")]
    public AppServerThreadResourceRefProjectionDto? Source { get; init; }

    [JsonPropertyName("required")]
    public bool? Required { get; init; }

    [JsonPropertyName("estimatedTokens")]
    public int? EstimatedTokens { get; init; }
}

internal sealed class AppServerThreadStageCheckpointProjectionDto
{
    [JsonConstructor]
    public AppServerThreadStageCheckpointProjectionDto()
    {
    }

    [JsonPropertyName("checkpointId")]
    public string? CheckpointId { get; init; }

    [JsonPropertyName("stageId")]
    public string? StageId { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("artifactRefs")]
    public IReadOnlyList<AppServerThreadArtifactRefProjectionDto>? ArtifactRefs { get; init; }

    [JsonPropertyName("modelRouteSetId")]
    public string? ModelRouteSetId { get; init; }

    [JsonPropertyName("modelRouteKind")]
    public string? ModelRouteKind { get; init; }

    [JsonPropertyName("diagnostics")]
    public JsonElement? Diagnostics { get; init; }

    [JsonPropertyName("nextStageSuggestions")]
    public IReadOnlyList<string>? NextStageSuggestions { get; init; }
}

internal sealed class AppServerThreadArtifactRefProjectionDto
{
    [JsonConstructor]
    public AppServerThreadArtifactRefProjectionDto()
    {
    }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }
}

internal sealed class AppServerThreadResourceRefProjectionDto
{
    [JsonConstructor]
    public AppServerThreadResourceRefProjectionDto()
    {
    }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("key")]
    public string? Key { get; init; }
}

internal sealed class AppServerThreadSummaryDto
{
    [JsonConstructor]
    public AppServerThreadSummaryDto()
    {
    }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("preview")]
    public string? Preview { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("modelProvider")]
    public string? ModelProvider { get; init; }

    [JsonPropertyName("source")]
    public JsonElement Source { get; init; }

    [JsonPropertyName("cliVersion")]
    public string? CliVersion { get; init; }

    [JsonPropertyName("agentNickname")]
    public string? AgentNickname { get; init; }

    [JsonPropertyName("agentRole")]
    public string? AgentRole { get; init; }

    [JsonPropertyName("createdAt")]
    public long? CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public long? UpdatedAt { get; init; }

    [JsonPropertyName("ephemeral")]
    public bool? Ephemeral { get; init; }

    [JsonPropertyName("status")]
    public AppServerThreadStatusDto? Status { get; init; }

    [JsonPropertyName("gitInfo")]
    public AppServerThreadGitInfoDto? GitInfo { get; init; }

    [JsonPropertyName("turns")]
    public JsonElement Turns { get; init; }

    [JsonPropertyName("sessionState")]
    public AppServerThreadSessionProjectionDto? SessionState { get; init; }
}

internal sealed class AppServerThreadResponseDto
{
    [JsonConstructor]
    public AppServerThreadResponseDto()
    {
    }

    [JsonPropertyName("thread")]
    public AppServerThreadSummaryDto? Thread { get; init; }

    [JsonPropertyName("data")]
    public IReadOnlyList<AppServerThreadSummaryDto>? Data { get; init; }

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("modelProvider")]
    public string? ModelProvider { get; init; }

    [JsonPropertyName("serviceTier")]
    public string? ServiceTier { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public string? ApprovalPolicy { get; init; }

    [JsonPropertyName("sandbox")]
    public JsonElement Sandbox { get; init; }

    [JsonPropertyName("reasoningEffort")]
    public string? ReasoningEffort { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }
}
