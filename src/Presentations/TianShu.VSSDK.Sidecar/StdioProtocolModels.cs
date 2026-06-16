using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.VSSDK.Sidecar;

internal sealed class SidecarRequestEnvelope
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }

    public string GetRequiredRequestId()
        => string.IsNullOrWhiteSpace(RequestId)
            ? throw new InvalidOperationException("requestId 不能为空。")
            : RequestId.Trim();

    public string GetRequiredCommand()
        => string.IsNullOrWhiteSpace(Command)
            ? throw new InvalidOperationException("command 不能为空。")
            : Command.Trim();

    public T DeserializePayload<T>(JsonSerializerOptions jsonOptions)
        where T : new()
    {
        if (Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new T();
        }

        var value = JsonSerializer.Deserialize<T>(Payload.GetRawText(), jsonOptions);
        return value ?? new T();
    }
}

internal sealed class SidecarResponseEnvelope
{
    [JsonPropertyName("messageType")]
    public string MessageType { get; init; } = "response";

    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; init; }
}

internal sealed class SidecarEventEnvelope
{
    [JsonPropertyName("messageType")]
    public string MessageType { get; init; } = "event";

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "info";

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public string? TurnId { get; init; }

    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("data")]
    public object? Data { get; init; }
}

internal sealed class InitializePayload
{
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("configPath")]
    public string? ConfigPath { get; init; }

    [JsonPropertyName("profileName")]
    public string? ProfileName { get; init; }

    [JsonPropertyName("appHostProjectPath")]
    public string? AppHostProjectPath { get; init; }

    [JsonPropertyName("createThreadOnInitialize")]
    public bool? CreateThreadOnInitialize { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("modelProvider")]
    public string? ModelProvider { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public string? ApprovalPolicy { get; init; }

    [JsonPropertyName("sandboxMode")]
    public string? SandboxMode { get; init; }

    [JsonPropertyName("webSearchMode")]
    public string? WebSearchMode { get; init; }

    [JsonPropertyName("serviceTier")]
    public string? ServiceTier { get; init; }

    [JsonPropertyName("collaborationMode")]
    public string? CollaborationMode { get; init; }
}

internal sealed class SendPayload
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("inputs")]
    public List<SidecarUserInputPayload>? Inputs { get; init; }

    [JsonPropertyName("historyMessages")]
    public List<ConversationHistoryMessagePayload>? HistoryMessages { get; init; }
}

internal sealed class FollowUpPayload
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("inputs")]
    public List<SidecarUserInputPayload>? Inputs { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }
}

internal sealed class ListThreadsPayload
{
    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 20;

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("archived")]
    public bool Archived { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("sortKey")]
    public string? SortKey { get; init; }

    [JsonPropertyName("modelProviders")]
    public IReadOnlyList<string>? ModelProviders { get; init; }

    [JsonPropertyName("sourceKinds")]
    public IReadOnlyList<ControlPlaneThreadSourceKind>? SourceKinds { get; init; }

    [JsonPropertyName("searchTerm")]
    public string? SearchTerm { get; init; }

    [JsonPropertyName("matchCurrentCwd")]
    public bool MatchCurrentCwd { get; init; } = true;
}

internal abstract class ThreadRequestPayloadBase
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("modelProvider")]
    public string? ModelProvider { get; init; }

    [JsonPropertyName("serviceTier")]
    public SidecarServiceTierOverride ServiceTier { get; init; } = SidecarServiceTierOverride.Unspecified;

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public SidecarApprovalPolicy? ApprovalPolicy { get; init; }

    [JsonPropertyName("sandboxMode")]
    public string? SandboxMode { get; init; }

    [JsonPropertyName("config")]
    public Dictionary<string, StructuredValue>? Config { get; init; }

    [JsonPropertyName("baseInstructions")]
    public string? BaseInstructions { get; init; }

    [JsonPropertyName("developerInstructions")]
    public string? DeveloperInstructions { get; init; }
}

internal sealed class StartNewThreadPayload : ThreadRequestPayloadBase
{
    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; init; }

    [JsonPropertyName("personality")]
    public SidecarPersonality? Personality { get; init; }

    [JsonPropertyName("ephemeral")]
    public bool? Ephemeral { get; init; }

    [JsonPropertyName("dynamicTools")]
    public List<ControlPlaneDynamicToolSpec>? DynamicTools { get; init; }

    [JsonPropertyName("persistExtendedHistory")]
    public bool? PersistExtendedHistory { get; init; }

    [JsonPropertyName("experimentalRawEvents")]
    public bool? ExperimentalRawEvents { get; init; }
}

internal sealed class ResumeThreadPayload : ThreadRequestPayloadBase
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("history")]
    public List<SidecarThreadHistoryItem>? History { get; init; }

    [JsonPropertyName("personality")]
    public SidecarPersonality? Personality { get; init; }

    [JsonPropertyName("persistExtendedHistory")]
    public bool? PersistExtendedHistory { get; init; }
}

internal sealed class ForkThreadPayload : ThreadRequestPayloadBase
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("ephemeral")]
    public bool? Ephemeral { get; init; }

    [JsonPropertyName("persistExtendedHistory")]
    public bool? PersistExtendedHistory { get; init; }
}

internal sealed class RenameThreadPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class ArchiveThreadPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }
}

internal sealed class DeleteThreadPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }
}

internal sealed class ReadThreadPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("includeTurns")]
    public bool IncludeTurns { get; init; }
}

internal sealed class LoadedThreadListPayload
{
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

internal sealed class ThreadCompactPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("keepRecentTurns")]
    public int? KeepRecentTurns { get; init; }
}

internal sealed class ThreadIdPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }
}

internal sealed class UnarchiveThreadPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }
}

internal sealed class RollbackThreadPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("numTurns")]
    public int NumTurns { get; init; }
}

internal sealed class ThreadMetadataUpdatePayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("gitInfo")]
    public ThreadGitInfoPayload? GitInfo { get; init; }

    [JsonPropertyName("hasGitSha")]
    public bool? HasGitSha { get; init; }

    [JsonPropertyName("gitSha")]
    public string? GitSha { get; init; }

    [JsonPropertyName("hasGitBranch")]
    public bool? HasGitBranch { get; init; }

    [JsonPropertyName("gitBranch")]
    public string? GitBranch { get; init; }

    [JsonPropertyName("hasGitOriginUrl")]
    public bool? HasGitOriginUrl { get; init; }

    [JsonPropertyName("gitOriginUrl")]
    public string? GitOriginUrl { get; init; }
}

internal sealed class ThreadGitInfoPayload
{
    [JsonPropertyName("sha")]
    public string? Sha { get; init; }

    [JsonPropertyName("branch")]
    public string? Branch { get; init; }

    [JsonPropertyName("originUrl")]
    public string? OriginUrl { get; init; }
}

internal sealed class FuzzyFileSearchPayload
{
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("roots")]
    public IReadOnlyList<string>? Roots { get; init; }
}

internal sealed class FuzzyFileSearchSessionStartPayload
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("roots")]
    public IReadOnlyList<string>? Roots { get; init; }
}

internal sealed class FuzzyFileSearchSessionUpdatePayload
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("query")]
    public string? Query { get; init; }
}

internal sealed class FuzzyFileSearchSessionStopPayload
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }
}

internal sealed class RealtimeStartPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }
}

internal sealed class RealtimeAppendTextPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class RealtimeAppendAudioPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("audio")]
    public RealtimeAudioPayload? Audio { get; init; }
}

internal sealed class RealtimeAudioPayload
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

internal sealed class RealtimeHandoffOutputPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("handoffId")]
    public string? HandoffId { get; init; }

    [JsonPropertyName("output")]
    public string? Output { get; init; }
}

internal sealed class RealtimeStopPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }
}

internal sealed class FeedbackUploadPayload
{
    [JsonPropertyName("classification")]
    public string? Classification { get; init; }

    [JsonPropertyName("includeLogs")]
    public bool IncludeLogs { get; init; }

    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("extraLogFiles")]
    public IReadOnlyList<string>? ExtraLogFiles { get; init; }
}

internal sealed class ConfigReadPayload
{
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("includeLayers")]
    public bool IncludeLayers { get; init; }
}

internal sealed class ModelListPayload
{
    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 50;

    [JsonPropertyName("includeHidden")]
    public bool IncludeHidden { get; init; }
}

internal sealed class CapabilityCatalogPayload
{
    [JsonPropertyName("workspacePath")]
    public string? WorkspacePath { get; init; }

    [JsonPropertyName("modelLimit")]
    public int ModelLimit { get; init; } = 200;

    [JsonPropertyName("includeHiddenModels")]
    public bool IncludeHiddenModels { get; init; }
}

internal sealed class ResolveEngineBindingPayload
{
    [JsonPropertyName("workspacePath")]
    public string? WorkspacePath { get; init; }

    [JsonPropertyName("providerKey")]
    public string? ProviderKey { get; init; }

    [JsonPropertyName("modelKey")]
    public string? ModelKey { get; init; }

    [JsonPropertyName("reasoningEffort")]
    public string? ReasoningEffort { get; init; }

    [JsonPropertyName("reasoningSummary")]
    public string? ReasoningSummary { get; init; }

    [JsonPropertyName("verbosity")]
    public string? Verbosity { get; init; }

    [JsonPropertyName("preferWebsocketTransport")]
    public bool PreferWebsocketTransport { get; init; }
}

internal sealed class AgentListPayload
{
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("includePrimaryThreads")]
    public bool IncludePrimaryThreads { get; init; }
}

internal sealed class RegisterAgentThreadPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("agentNickname")]
    public string? AgentNickname { get; init; }

    [JsonPropertyName("agentRole")]
    public string? AgentRole { get; init; }
}

internal sealed class CreateAgentJobPayload
{
    [JsonPropertyName("jobId")]
    public string? JobId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("instruction")]
    public string? Instruction { get; init; }

    [JsonPropertyName("inputHeaders")]
    public StructuredValue? InputHeaders { get; init; }

    [JsonPropertyName("inputCsvPath")]
    public string? InputCsvPath { get; init; }

    [JsonPropertyName("outputCsvPath")]
    public string? OutputCsvPath { get; init; }

    [JsonPropertyName("autoExport")]
    public bool? AutoExport { get; init; }

    [JsonPropertyName("outputSchema")]
    public StructuredValue? OutputSchema { get; init; }

    [JsonPropertyName("items")]
    public List<StructuredValue>? Items { get; init; }
}

internal sealed class DispatchAgentJobPayload
{
    [JsonPropertyName("jobId")]
    public string? JobId { get; init; }

    [JsonPropertyName("threadIds")]
    public List<string>? ThreadIds { get; init; }
}

internal sealed class ReportAgentJobItemPayload
{
    [JsonPropertyName("jobId")]
    public string? JobId { get; init; }

    [JsonPropertyName("itemId")]
    public string? ItemId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("result")]
    public StructuredValue? Result { get; init; }

    [JsonPropertyName("lastError")]
    public string? LastError { get; init; }
}

internal sealed class ReadAgentJobPayload
{
    [JsonPropertyName("jobId")]
    public string? JobId { get; init; }
}

internal sealed class CreateWorkflowPayload
{
    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; init; }

    [JsonPropertyName("spaceId")]
    public string? SpaceId { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; init; }
}

internal sealed class PublishWorkflowPlanPayload
{
    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("steps")]
    public List<WorkflowPlanStepPayload>? Steps { get; init; }
}

internal sealed class WorkflowPlanStepPayload
{
    [JsonPropertyName("order")]
    public int? Order { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

internal sealed class CreateWorkflowTaskPayload
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; init; }

    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; init; }
}

internal sealed class UpdateWorkflowTaskStatePayload
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; init; }
}

internal sealed class CreateCollaborationSpacePayload
{
    [JsonPropertyName("spaceId")]
    public string? SpaceId { get; init; }

    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; init; }

    [JsonPropertyName("defaultWorkspace")]
    public string? DefaultWorkspace { get; init; }

    [JsonPropertyName("defaultExecutionProfile")]
    public string? DefaultExecutionProfile { get; init; }

    [JsonPropertyName("policyKey")]
    public string? PolicyKey { get; init; }
}

internal sealed class ConfigureCollaborationSpacePayload
{
    [JsonPropertyName("spaceId")]
    public string? SpaceId { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; init; }

    [JsonPropertyName("defaultWorkspace")]
    public string? DefaultWorkspace { get; init; }

    [JsonPropertyName("defaultExecutionProfile")]
    public string? DefaultExecutionProfile { get; init; }

    [JsonPropertyName("policyKey")]
    public string? PolicyKey { get; init; }
}

internal sealed class ArchiveCollaborationSpacePayload
{
    [JsonPropertyName("spaceId")]
    public string? SpaceId { get; init; }
}

internal sealed class BindParticipantToSessionPayload
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; init; }
}

internal sealed class BindParticipantToWorkflowPayload
{
    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; init; }

    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; init; }
}

internal sealed class UpdateParticipantRolePayload
{
    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; init; }

    [JsonPropertyName("role")]
    public string? Role { get; init; }
}

internal sealed class ConfigValueWritePayload
{
    [JsonPropertyName("keyPath")]
    public string? KeyPath { get; init; }

    [JsonPropertyName("value")]
    public StructuredValue? Value { get; init; }

    [JsonPropertyName("mergeStrategy")]
    public string? MergeStrategy { get; init; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("filePath")]
    public string? FilePath { get; init; }

    [JsonPropertyName("expectedVersion")]
    public string? ExpectedVersion { get; init; }

    [JsonPropertyName("reloadUserConfig")]
    public bool ReloadUserConfig { get; init; }
}

internal sealed class ConfigBatchWritePayload
{
    [JsonPropertyName("items")]
    public List<ConfigWriteItemPayload>? Items { get; init; }

    [JsonPropertyName("mergeStrategy")]
    public string? MergeStrategy { get; init; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("filePath")]
    public string? FilePath { get; init; }

    [JsonPropertyName("expectedVersion")]
    public string? ExpectedVersion { get; init; }

    [JsonPropertyName("reloadUserConfig")]
    public bool ReloadUserConfig { get; init; }
}

internal sealed class ConfigWriteItemPayload
{
    [JsonPropertyName("keyPath")]
    public string? KeyPath { get; init; }

    [JsonPropertyName("value")]
    public StructuredValue? Value { get; init; }

    [JsonPropertyName("mergeStrategy")]
    public string? MergeStrategy { get; init; }
}

internal sealed class ConfigRequirementsReadPayload
{
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }
}

internal sealed class ExperimentalFeatureListPayload
{
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

internal sealed class McpServerStatusListPayload
{
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

internal sealed class SkillsListPayload
{
    [JsonPropertyName("workingDirectories")]
    public List<string>? WorkingDirectories { get; init; }

    [JsonPropertyName("forceReload")]
    public bool ForceReload { get; init; }
}

internal sealed class SkillsConfigWritePayload
{
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }
}

internal sealed class SkillsRemoteListPayload
{
    [JsonPropertyName("hazelnutScope")]
    public string? HazelnutScope { get; init; }

    [JsonPropertyName("productSurface")]
    public string? ProductSurface { get; init; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }
}

internal sealed class SkillsRemoteExportPayload
{
    [JsonPropertyName("hazelnutId")]
    public string? HazelnutId { get; init; }
}

internal sealed class PluginListPayload
{
    [JsonPropertyName("workingDirectories")]
    public List<string>? WorkingDirectories { get; init; }

    [JsonPropertyName("forceRemoteSync")]
    public bool ForceRemoteSync { get; init; }
}

internal sealed class PluginReadPayload
{
    [JsonPropertyName("marketplacePath")]
    public string? MarketplacePath { get; init; }

    [JsonPropertyName("pluginName")]
    public string? PluginName { get; init; }
}

internal sealed class PluginInstallPayload
{
    [JsonPropertyName("marketplacePath")]
    public string? MarketplacePath { get; init; }

    [JsonPropertyName("pluginName")]
    public string? PluginName { get; init; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }
}

internal sealed class PluginUninstallPayload
{
    [JsonPropertyName("pluginId")]
    public string? PluginId { get; init; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }
}

internal sealed class AppListPayload
{
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("forceRefetch")]
    public bool ForceRefetch { get; init; }
}

internal sealed class ReviewStartPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("delivery")]
    public string? Delivery { get; init; }

    [JsonPropertyName("targetType")]
    public string? TargetType { get; init; }
}

internal sealed class McpServerOauthLoginPayload
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("timeoutSecs")]
    public long? TimeoutSecs { get; init; }
}

internal sealed class ConversationSummaryPayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("rolloutPath")]
    public string? RolloutPath { get; init; }
}

internal sealed class GitDiffToRemotePayload
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }
}

internal sealed class RespondApprovalPayload
{
    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("approved")]
    public bool Approved { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    [JsonPropertyName("execPolicyAmendment")]
    public SidecarExecPolicyAmendmentPayload? ExecPolicyAmendment { get; init; }

    [JsonPropertyName("networkPolicyAmendment")]
    public SidecarNetworkPolicyAmendmentPayload? NetworkPolicyAmendment { get; init; }
}

internal sealed class RespondUserInputPayload
{
    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("answers")]
    public Dictionary<string, StructuredValue>? Answers { get; init; }
}

internal sealed class RespondPermissionPayload
{
    [JsonPropertyName("callId")]
    public string? CallId { get; init; }

    [JsonPropertyName("permissions")]
    public Dictionary<string, StructuredValue>? Permissions { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}

internal sealed class InvokeCapabilityPayload
{
    [JsonPropertyName("capability")]
    public string? Capability { get; init; }

    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("parametersJson")]
    public string? ParametersJson { get; init; }
}

internal sealed class InvokeRuntimeSurfacePayload
{
    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("parametersJson")]
    public string? ParametersJson { get; init; }
}

internal sealed class ConversationHistoryMessagePayload
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("inputs")]
    public List<SidecarUserInputPayload>? Inputs { get; init; }
}

internal sealed class SidecarUserInputPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("textElements")]
    public List<SidecarTextElementPayload>? TextElements { get; init; }

    public static SidecarUserInputPayload[]? MapControlPlaneInputs(IReadOnlyList<ControlPlaneInputItem>? inputs)
        => inputs is not { Count: > 0 }
            ? null
            : inputs.Select(FromControlPlane).ToArray();

    public static SidecarUserInputPayload FromControlPlane(ControlPlaneInputItem input)
        => input switch
        {
            ControlPlaneTextInput textInput => new SidecarUserInputPayload
            {
                Type = Normalize(textInput.Type) ?? "text",
                Text = textInput.Text,
                TextElements = (textInput.TextElements ?? Array.Empty<ControlPlaneTextElement>())
                    .Select(static item => new SidecarTextElementPayload
                    {
                        ByteRange = new SidecarByteRangePayload
                        {
                            Start = item.ByteRange.Start,
                            End = item.ByteRange.End,
                        },
                        Placeholder = item.Placeholder,
                    })
                    .ToList(),
            },
            ControlPlaneImageInput imageInput => new SidecarUserInputPayload
            {
                Type = Normalize(imageInput.Type) ?? "image",
                Url = imageInput.Url,
            },
            ControlPlaneLocalImageInput localImageInput => new SidecarUserInputPayload
            {
                Type = Normalize(localImageInput.Type) ?? "local_image",
                Path = localImageInput.Path,
            },
            ControlPlaneSkillInput skillInput => new SidecarUserInputPayload
            {
                Type = Normalize(skillInput.Type) ?? "skill",
                Name = skillInput.Name,
                Path = skillInput.Path,
            },
            ControlPlaneMentionInput mentionInput => new SidecarUserInputPayload
            {
                Type = Normalize(mentionInput.Type) ?? "mention",
                Name = mentionInput.Name,
                Path = mentionInput.Path,
            },
            _ => new SidecarUserInputPayload
            {
                Type = Normalize(input.Type) ?? string.Empty,
            },
        };

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}

internal sealed class SidecarTextElementPayload
{
    [JsonPropertyName("byteRange")]
    public SidecarByteRangePayload? ByteRange { get; init; }

    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; init; }
}

internal sealed class SidecarByteRangePayload
{
    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("end")]
    public int End { get; init; }
}


