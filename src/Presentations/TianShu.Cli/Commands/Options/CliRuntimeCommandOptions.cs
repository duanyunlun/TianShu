using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.RuntimeComposition;

namespace TianShu.Cli;

internal abstract class CliRuntimeCommandOptions
{
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    public string? AppHostProjectPath { get; set; }

    public string ConfigFilePath { get; set; } = RuntimeConfigurationComposition.ResolveDefaultPath();

    public string? ProfileName { get; set; }

    public Dictionary<string, string> ConfigOverrides { get; } = new(StringComparer.Ordinal);

    public string? ResumeThreadId { get; set; }

    public bool ResumeLatestThread { get; set; }

    public bool ResumeLatestMatchCwd { get; set; } = true;

    public bool CreateThreadOnInitialize { get; set; } = true;

    public string? CollaborationMode { get; set; }

    public string? WebSearchMode { get; set; }

    public IReadOnlyList<ControlPlaneDynamicToolSpec>? DynamicTools { get; set; }

    public string? RuntimeModel { get; set; }

    public string? RuntimeModelProvider { get; set; }

    public string? RuntimeProviderWireApi { get; set; }

    public string? RuntimeApprovalPolicy { get; set; }

    public string? RuntimeSandboxMode { get; set; }

    public bool OutputJson { get; set; }

    public ControlPlaneInitializeRuntimeCommand ToRuntimeOptions()
        => new()
        {
            ExecutablePath = "dotnet",
            UseDotNetProjectLauncher = true,
            WorkingDirectory = WorkingDirectory,
            ConfigFilePath = ConfigFilePath,
            ProfileName = ProfileName,
            ConfigOverrides = ConfigOverrides.Count == 0
                ? null
                : new Dictionary<string, string>(ConfigOverrides, StringComparer.Ordinal),
            ResumeThreadId = ResumeThreadId,
            ResumeLatestThread = ResumeLatestThread,
            ResumeLatestMatchCwd = ResumeLatestMatchCwd,
            CreateThreadOnInitialize = CreateThreadOnInitialize,
            CollaborationMode = CollaborationMode,
            SessionSource = ControlPlaneSessionSource.Cli,
            WebSearchMode = WebSearchMode,
            DynamicTools = DynamicTools,
            Model = RuntimeModel,
            ModelProvider = RuntimeModelProvider,
            ApprovalPolicy = RuntimeApprovalPolicy,
            SandboxMode = RuntimeSandboxMode,
            StartupTimeout = TimeSpan.FromSeconds(45),
            TurnTimeout = TimeSpan.FromMinutes(5),
        };
}

internal enum ChatOutputProtocol
{
    Human,
    Jsonl,
}

internal enum CompletionShellKind
{
    Bash,
    Zsh,
    Fish,
    PowerShell,
}

internal sealed class CompletionCommandOptions
{
    public CompletionShellKind Shell { get; set; } = CompletionShellKind.Bash;
}

internal enum DebugCommandKind
{
    ClearMemories,
}

internal sealed class InitCommandOptions : CliRuntimeCommandOptions
{
    public InitCommandOptions()
    {
        CreateThreadOnInitialize = false;
    }

    public string? Provider { get; set; }

    public bool Force { get; set; }
}

internal sealed class DoctorCommandOptions : CliRuntimeCommandOptions
{
    public DoctorCommandOptions()
    {
        CreateThreadOnInitialize = false;
    }

    public bool Probe { get; set; }
}

internal sealed class DebugCommandOptions : CliRuntimeCommandOptions
{
    public DebugCommandOptions()
    {
        CreateThreadOnInitialize = false;
    }

    public DebugCommandKind CommandKind { get; set; }
}

internal enum ChatStartupThreadActionKind
{
    None = 0,
    Resume = 1,
    Fork = 2,
}

internal sealed class ChatCommandOptions : CliRuntimeCommandOptions
{
    public ChatCommandOptions()
    {
        CreateThreadOnInitialize = false;
    }

    public bool ApproveAll { get; set; }

    public bool FullAuto { get; set; }

    public bool DangerouslyBypassApprovalsAndSandbox { get; set; }

    public ControlPlaneApprovalDecision ApprovalDecision { get; set; } = ControlPlaneApprovalDecision.Approve;

    public string? PermissionsJsonPath { get; set; }

    public string? UserInputJsonPath { get; set; }

    public bool VerboseEvents { get; set; }

    public string? InitialMessage { get; set; }

    public List<string> ImagePaths { get; } = [];

    public string? ScriptPath { get; set; }

    public string? ArtifactsRoot { get; set; }

    public ChatOutputProtocol OutputProtocol { get; set; } = ChatOutputProtocol.Human;

    public ChatStartupThreadActionKind StartupThreadAction { get; set; }

    public string? StartupThreadTarget { get; set; }

    public bool StartupThreadUseLast { get; set; }

    public bool StartupThreadShowAll { get; set; }
}

internal enum ThreadCommandKind
{
    List,
    Start,
    Fork,
    Archive,
    Delete,
    Clear,
    Rename,
    Resume,
    LoadedList,
    Compact,
    CleanBackgroundTerminals,
    Unsubscribe,
    IncrementElicitation,
    DecrementElicitation,
    Read,
    Unarchive,
    Metadata,
    Rollback,
}

internal sealed class ThreadCommandOptions : CliRuntimeCommandOptions
{
    public ThreadCommandKind CommandKind { get; set; }

    public bool ApproveAll { get; set; }

    public ControlPlaneApprovalDecision ApprovalDecision { get; set; } = ControlPlaneApprovalDecision.Approve;

    public string? PermissionsJsonPath { get; set; }

    public string? UserInputJsonPath { get; set; }

    public int Limit { get; set; } = 20;

    public string? Cursor { get; set; }

    public int KeepRecentTurns { get; set; } = 12;

    public int? NumTurns { get; set; }

    public bool IncludeTurns { get; set; }

    public bool Archived { get; set; }

    public bool MatchCurrentCwd { get; set; } = true;

    public string SortKey { get; set; } = "created_at";

    public List<string> ModelProviders { get; } = [];

    public List<ControlPlaneThreadSourceKind> SourceKinds { get; } = [];

    public string? SearchTerm { get; set; }

    public string? ThreadId { get; set; }

    public string? Confirmation { get; set; }

    public string? Name { get; set; }

    public string? GitSha { get; set; }

    public string? GitBranch { get; set; }

    public string? GitOriginUrl { get; set; }

    public bool ClearGitSha { get; set; }

    public bool ClearGitBranch { get; set; }

    public bool ClearGitOriginUrl { get; set; }

    public string? ThreadPath { get; set; }

    public string? ThreadWorkingDirectory { get; set; }

    public string? ThreadModel { get; set; }

    public string? ThreadModelProvider { get; set; }

    public CliServiceTierOverride ThreadServiceTier { get; set; } = CliServiceTierOverride.Unspecified;

    public string? ThreadApprovalPolicy { get; set; }

    public string? ThreadSandboxMode { get; set; }

    public IReadOnlyDictionary<string, StructuredValue>? ThreadConfig { get; set; }

    public string? ThreadServiceName { get; set; }

    public string? ThreadBaseInstructions { get; set; }

    public string? ThreadDeveloperInstructions { get; set; }

    public string? ThreadPersonality { get; set; }

    public bool? ThreadEphemeral { get; set; }

    public IReadOnlyList<StructuredValue>? ThreadHistory { get; set; }

    public IReadOnlyList<ControlPlaneDynamicToolSpec>? ThreadDynamicTools { get; set; }

    public bool? ThreadPersistExtendedHistory { get; set; }

    public bool? ThreadExperimentalRawEvents { get; set; }
}

internal sealed class RpcCommandOptions : CliRuntimeCommandOptions
{
    public string Method { get; set; } = string.Empty;

    public string? ParamsJson { get; set; }
}

internal sealed class FollowUpCliCommandOptions : CliRuntimeCommandOptions
{
    public string Message { get; set; } = string.Empty;

    public ControlPlaneFollowUpMode Mode { get; set; } = ControlPlaneFollowUpMode.Queue;

    public bool KernelRuntimeLoop { get; set; }

    public string? TurnId { get; set; }

    public string? CheckpointRef { get; set; }

    public string? ResumeToken { get; set; }

    public int TurnTimeoutSeconds { get; set; } = 300;

    public bool ApproveAll { get; set; }

    public bool EnableShell { get; set; }

    public bool EnableMcp { get; set; }

    public bool EnableMemory { get; set; }

    public ControlPlaneApprovalDecision ApprovalDecision { get; set; } = ControlPlaneApprovalDecision.Approve;

    public string? PermissionsJsonPath { get; set; }

    public string? UserInputJsonPath { get; set; }

    public bool VerboseEvents { get; set; }
}

internal enum RuntimeSurfaceCommandKind
{
    ConversationThread,
    SessionSnapshot,
    SessionOverview,
    SessionList,
    GovernanceApprovalQueue,
    GovernanceUserInputList,
    CollaborationCreate,
    CollaborationConfigure,
    CollaborationArchive,
    CollaborationOverview,
    CollaborationSpace,
    CollaborationList,
    ParticipantBindSession,
    ParticipantBindWorkflow,
    ParticipantUpdateRole,
    ParticipantRead,
    ParticipantView,
    ParticipantList,
    ArtifactRead,
    ArtifactList,
    WorkflowCreate,
    WorkflowPublishPlan,
    WorkflowCreateTask,
    WorkflowUpdateTaskState,
    WorkflowBoard,
    WorkflowTaskBoard,
    WorkflowPlan,
    AgentList,
    AgentRoster,
    AgentTeam,
    AgentThreadRegister,
    AgentJobCreate,
    AgentJobDispatch,
    AgentJobItemReport,
    AgentJobRead,
    IdentityAccount,
    IdentityDevices,
    MemoryProviders,
    MemorySpaces,
    MemoryOverlay,
    MemoryFilter,
    MemoryAdd,
    MemoryExtract,
    MemoryImport,
    MemoryExport,
    MemoryBindProvider,
    MemoryConsolidate,
    MemoryForget,
    MemoryDelete,
    MemorySupersede,
    MemoryReviewList,
    MemoryReviewApprove,
    MemoryReviewDemote,
    MemoryReviewMerge,
    MemoryReviewRestore,
    MemoryFeedback,
    MemoryCitation,
    DiagnosticsTrace,
    DiagnosticsAttemptList,
    ModelList,
    ModelCatalog,
    ModelResolve,
    ToolCatalog,
    ToolConfigExport,
    SkillsList,
    SkillsConfigWrite,
    SkillsRemoteList,
    SkillsRemoteExport,
    PluginList,
    PluginRead,
    PluginInstall,
    PluginUninstall,
    AppList,
    ReviewStart,
    FeatureList,
    FeatureConfigWrite,
    ConfigRead,
    ConfigRequirementsRead,
    ConfigValueWrite,
    ConfigBatchWrite,
    ExperimentalFeatureList,
    CollaborationModeList,
    McpServerStatusList,
    McpServerReload,
    McpServerOauthLogin,
    ConversationSummary,
    GitDiffToRemote,
}

internal sealed class RuntimeSurfaceCommandOptions : CliRuntimeCommandOptions
{
    public RuntimeSurfaceCommandKind CommandKind { get; set; }

    public string? SessionId { get; set; }

    public string? WorkflowId { get; set; }

    public string? TaskId { get; set; }

    public string? TeamId { get; set; }

    public string? ParticipantId { get; set; }

    public string? ArtifactId { get; set; }

    public string? AccountId { get; set; }

    public string? MemorySpaceId { get; set; }

    public MemoryScopeKind? MemoryScopeKind { get; set; }

    public string? PayloadJson { get; set; }

    public string? PayloadFilePath { get; set; }

    public string? TraceId { get; set; }

    public string? ExecutionId { get; set; }

    public string? CollaborationSpaceId { get; set; }

    public string? CollaborationSpaceKey { get; set; }

    public string? DisplayName { get; set; }

    public string? Title { get; set; }

    public string? Purpose { get; set; }

    public string? DefaultWorkspace { get; set; }

    public string? DefaultExecutionProfile { get; set; }

    public string? PolicyKey { get; set; }

    public string? Role { get; set; }

    public bool IncludeClosed { get; set; }

    public bool IncludeArchived { get; set; }

    public int? Limit { get; set; }

    public string? Cursor { get; set; }

    public bool IncludeHidden { get; set; }

    public bool IncludePrimaryThreads { get; set; }

    public bool IncludeLayers { get; set; }

    public bool ForceReload { get; set; }

    public bool ForceRefetch { get; set; }

    public bool ForceRemoteSync { get; set; }

    public string? ThreadId { get; set; }

    public string? AgentNickname { get; set; }

    public string? AgentRole { get; set; }

    public string? JobId { get; set; }

    public string? Name { get; set; }

    public string? Instruction { get; set; }

    public string? InputHeadersJson { get; set; }

    public string? InputHeadersFilePath { get; set; }

    public string? InputCsvPath { get; set; }

    public string? OutputCsvPath { get; set; }

    public bool? AutoExport { get; set; }

    public string? OutputSchemaJson { get; set; }

    public string? OutputSchemaFilePath { get; set; }

    public string? ItemsJson { get; set; }

    public string? ItemsFilePath { get; set; }

    public List<string> DispatchThreadIds { get; } = [];

    public string? ItemId { get; set; }

    public string? Status { get; set; }

    public string? ResultJson { get; set; }

    public string? ResultFilePath { get; set; }

    public string? LastError { get; set; }

    public string? ToolConfigOutputPath { get; set; }

    public string? Delivery { get; set; }

    public string? ProviderKey { get; set; }

    public string? ModelKey { get; set; }

    public string? ReasoningEffort { get; set; }

    public string? ReasoningSummary { get; set; }

    public string? Verbosity { get; set; }

    public bool PreferWebsocketTransport { get; set; }

    public string? ReviewTargetType { get; set; }

    public string? ReviewBranch { get; set; }

    public string? ReviewSha { get; set; }

    public string? ReviewTitle { get; set; }

    public string? ReviewInstructions { get; set; }

    public string? RolloutPath { get; set; }

    public string? MarketplacePath { get; set; }

    public string? PluginName { get; set; }

    public string? PluginId { get; set; }

    public string? SkillPath { get; set; }

    public string? FeatureName { get; set; }

    public bool? Enabled { get; set; }

    public string? HazelnutScope { get; set; }

    public string? ProductSurface { get; set; }

    public bool? RemoteEnabled { get; set; }

    public string? HazelnutId { get; set; }

    public string? McpServerName { get; set; }

    public long? TimeoutSecs { get; set; }

    public bool WaitForCompletion { get; set; }

    public string? KeyPath { get; set; }

    public string? ConfigValueJson { get; set; }

    public string? ConfigValueFilePath { get; set; }

    public string? ConfigEditFilePath { get; set; }

    public string? ExpectedVersion { get; set; }

    public string MergeStrategy { get; set; } = "replace";

    public bool ReloadUserConfig { get; set; }

    public string? BatchItemsJson { get; set; }

    public string? BatchItemsFilePath { get; set; }

    public List<string> ExtraRoots { get; } = [];
}

internal sealed class ModelRouteDiagnosticCommandOptions : CliRuntimeCommandOptions
{
    public ModelRouteDiagnosticCommandOptions()
    {
        CreateThreadOnInitialize = false;
    }

    public string RouteKind { get; set; } = "default";

    public string? RouteSetId { get; set; }
}

internal enum AppServerCommandKind
{
    RunServer,
    GenerateTs,
    GenerateJsonSchema,
}

internal sealed class AppServerCommandOptions : CliRuntimeCommandOptions
{
    public AppServerCommandKind CommandKind { get; set; } = AppServerCommandKind.RunServer;

    public string ListenUrl { get; set; } = "stdio://";

    public bool AnalyticsDefaultEnabled { get; set; }

    public string? OutDirectory { get; set; }

    public string? PrettierPath { get; set; }

    public bool Experimental { get; set; }
}

internal sealed class McpServerCommandOptions : CliRuntimeCommandOptions
{
}

internal enum McpCommandKind
{
    List,
    Get,
    Add,
    Remove,
}

internal sealed class McpCommandOptions : CliRuntimeCommandOptions
{
    public McpCommandKind CommandKind { get; set; }

    public string? Name { get; set; }

    public string? Url { get; set; }

    public string? BearerTokenEnvVar { get; set; }

    public List<string> Command { get; } = [];

    public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.Ordinal);
}

internal enum CommandExecCommandKind
{
    Exec,
    Write,
    Terminate,
    Resize,
}

internal sealed class CommandExecCommandOptions : CliRuntimeCommandOptions
{
    public CommandExecCommandKind CommandKind { get; set; }

    public string? CommandText { get; set; }

    public string? CommandArgsJson { get; set; }

    public string? CommandArgsFilePath { get; set; }

    public string? ProcessId { get; set; }

    public bool Tty { get; set; }

    public int? Rows { get; set; }

    public int? Cols { get; set; }

    public bool StreamStdin { get; set; }

    public bool StreamStdoutStderr { get; set; }

    public bool Background { get; set; }

    public bool DisableTimeout { get; set; }

    public int? TimeoutMs { get; set; }

    public bool DisableOutputCap { get; set; }

    public int? OutputBytesCap { get; set; }

    public string? ThreadId { get; set; }

    public string? TurnId { get; set; }

    public string? ItemId { get; set; }

    public string? ApprovalPolicy { get; set; }

    public bool Approved { get; set; }

    public bool? Login { get; set; }

    public string? EnvJson { get; set; }

    public string? EnvFilePath { get; set; }

    public string? SandboxJson { get; set; }

    public string? SandboxFilePath { get; set; }

    public string? InputText { get; set; }

    public string? InputFilePath { get; set; }

    public string? InputBase64 { get; set; }

    public bool CloseStdin { get; set; }
}

internal enum ExecCommandKind
{
    UserTurn,
    Resume,
    Review,
}

internal sealed class ExecCommandOptions : CliRuntimeCommandOptions
{
    public ExecCommandOptions()
    {
        CreateThreadOnInitialize = false;
        RuntimeApprovalPolicy = "never";
    }

    public ExecCommandKind CommandKind { get; set; }

    public string? Prompt { get; set; }

    public string? ResumeTarget { get; set; }

    public bool UseLast { get; set; }

    public bool ShowAll { get; set; }

    public bool FullAuto { get; set; }

    public bool DangerouslyBypassApprovalsAndSandbox { get; set; }

    public bool SkipGitRepoCheck { get; set; }

    public string? OutputLastMessageFilePath { get; set; }

    public bool Ephemeral { get; set; }

    public string? OutputSchemaFilePath { get; set; }

    public List<string> AdditionalWritableDirectories { get; } = [];

    public List<string> ImagePaths { get; } = [];

    public bool ReviewUncommitted { get; set; }

    public string? ReviewBaseBranch { get; set; }

    public string? ReviewCommit { get; set; }

    public string? ReviewCommitTitle { get; set; }

    public string? ReviewPrompt { get; set; }
}

internal enum CodeModeCommandKind
{
    Exec,
    Wait,
}

internal sealed class CodeModeCommandOptions : CliRuntimeCommandOptions
{
    public CodeModeCommandKind CommandKind { get; set; }

    public string? ThreadId { get; set; }

    public string? Input { get; set; }

    public string? InputFilePath { get; set; }

    public int? YieldTimeMs { get; set; }

    public int? MaxOutputTokens { get; set; }

    public string? CellId { get; set; }

    public int? MaxTokens { get; set; }

    public bool Terminate { get; set; }
}

internal enum FuzzyFileSearchCommandKind
{
    Search,
    Start,
    Update,
    Stop,
}

internal sealed class FuzzyFileSearchCommandOptions : CliRuntimeCommandOptions
{
    public FuzzyFileSearchCommandKind CommandKind { get; set; }

    public string? SessionId { get; set; }

    public string? Query { get; set; }

    public int? Limit { get; set; }

    public List<string> Roots { get; } = [];
}

internal enum FeedbackCommandKind
{
    Upload,
}

internal sealed class FeedbackCommandOptions : CliRuntimeCommandOptions
{
    public FeedbackCommandKind CommandKind { get; set; }

    public string? Classification { get; set; }

    public bool IncludeLogs { get; set; }

    public string? ThreadId { get; set; }

    public string? Reason { get; set; }

    public List<string> ExtraLogFiles { get; } = [];
}

internal enum WindowsSandboxCommandKind
{
    SetupStart,
}

internal sealed class WindowsSandboxCommandOptions : CliRuntimeCommandOptions
{
    public WindowsSandboxCommandKind CommandKind { get; set; }

    public string? Mode { get; set; }

    public string? SandboxCwd { get; set; }
}

internal enum RealtimeCommandKind
{
    Start,
    AppendText,
    AppendAudio,
    HandoffOutput,
    Stop,
}

internal sealed class RealtimeCommandOptions : CliRuntimeCommandOptions
{
    public RealtimeCommandKind CommandKind { get; set; }

    public string? ThreadId { get; set; }

    public string? SessionId { get; set; }

    public string? Prompt { get; set; }

    public string? Text { get; set; }

    public string? HandoffId { get; set; }

    public string? Output { get; set; }

    public string? AudioJson { get; set; }

    public string? AudioFilePath { get; set; }
}

internal sealed record RuntimeSurfaceInvocation(string Method, object? Parameters);

internal static class CliStructuredPayloadReader
{
    private static readonly JsonSerializerOptions TypedPayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryReadTypedArrayPayload<T>(
        string? inlineJson,
        string? filePath,
        string subject,
        out IReadOnlyList<T>? values,
        out string error)
    {
        values = null;
        error = string.Empty;

        if (!TryReadPayloadText(inlineJson, filePath, subject, out var payloadText, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(payloadText))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadText);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = $"{subject} 必须是 JSON 数组。";
                return false;
            }

            values = JsonSerializer.Deserialize<T[]>(payloadText, TypedPayloadJsonOptions);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"{subject} JSON 解析失败：{ex.Message}";
            return false;
        }
    }

    public static bool TryReadStructuredArrayPayload(
        string? inlineJson,
        string? filePath,
        string subject,
        out IReadOnlyList<StructuredValue>? values,
        out string error)
    {
        values = null;
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(inlineJson) && !string.IsNullOrWhiteSpace(filePath))
        {
            error = $"{subject} 不能同时提供 JSON 文本和文件路径。";
            return false;
        }

        if (!TryReadPayloadText(inlineJson, filePath, subject, out var payloadText, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(payloadText))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadText);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = $"{subject} 必须是 JSON 数组。";
                return false;
            }

            values = document.RootElement.EnumerateArray().Select(StructuredValue.FromJsonElement).ToArray();
            return true;
        }
        catch (JsonException ex)
        {
            error = $"{subject} JSON 解析失败：{ex.Message}";
            return false;
        }
    }

    public static bool TryReadStructuredObjectPayload(
        string? inlineJson,
        string? filePath,
        string subject,
        out IReadOnlyDictionary<string, StructuredValue>? value,
        out string error)
    {
        value = null;
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(inlineJson) && !string.IsNullOrWhiteSpace(filePath))
        {
            error = $"{subject} 不能同时提供 JSON 文本和文件路径。";
            return false;
        }

        if (!TryReadPayloadText(inlineJson, filePath, subject, out var payloadText, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(payloadText))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = $"{subject} 必须是 JSON 对象。";
                return false;
            }

            value = document.RootElement.EnumerateObject()
                .ToDictionary(
                    static property => property.Name,
                    static property => StructuredValue.FromJsonElement(property.Value),
                    StringComparer.Ordinal);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"{subject} JSON 解析失败：{ex.Message}";
            return false;
        }
    }

    private static bool TryReadPayloadText(
        string? inlineJson,
        string? filePath,
        string subject,
        out string? payloadText,
        out string error)
    {
        payloadText = null;
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(inlineJson) && !string.IsNullOrWhiteSpace(filePath))
        {
            error = $"{subject} 不能同时提供 JSON 文本和文件路径。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            if (!File.Exists(filePath))
            {
                error = $"{subject} 文件不存在：{filePath}";
                return false;
            }

            payloadText = File.ReadAllText(filePath);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(inlineJson))
        {
            payloadText = inlineJson;
        }

        return true;
    }
}
