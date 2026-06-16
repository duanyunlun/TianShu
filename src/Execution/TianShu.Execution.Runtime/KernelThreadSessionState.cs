using System.Text.Json;

namespace TianShu.Execution.Runtime;

/// <summary>
/// 线程协作模式配置快照。
/// Collaboration mode settings snapshot for a thread session.
/// </summary>
internal sealed record KernelCollaborationModeSettings(
    string Model,
    string? ReasoningEffort,
    string? DeveloperInstructions);

/// <summary>
/// 线程协作模式状态。
/// Collaboration mode state carried by the thread session runtime.
/// </summary>
internal sealed record KernelCollaborationModeState(
    string Mode,
    KernelCollaborationModeSettings Settings)
{
    public const string DefaultMode = "default";
    public const string PlanMode = "plan";

    public static KernelCollaborationModeState CreateDefault(
        string model,
        string? reasoningEffort = null,
        string? developerInstructions = null)
        => new(
            DefaultMode,
            new KernelCollaborationModeSettings(model, reasoningEffort, developerInstructions));

    public static KernelCollaborationModeState NormalizeOrDefault(
        KernelCollaborationModeState? state,
        string fallbackModel,
        string? fallbackReasoningEffort = null)
        => state is null
            ? CreateDefault(fallbackModel, fallbackReasoningEffort)
            : new KernelCollaborationModeState(
                string.IsNullOrWhiteSpace(state.Mode) ? DefaultMode : state.Mode,
                new KernelCollaborationModeSettings(
                    string.IsNullOrWhiteSpace(state.Settings.Model) ? fallbackModel : state.Settings.Model,
                    state.Settings.ReasoningEffort ?? fallbackReasoningEffort,
                    state.Settings.DeveloperInstructions));

    public bool AllowsRequestUserInput(bool defaultModeRequestUserInputEnabled = false)
        => string.Equals(Mode, PlanMode, StringComparison.OrdinalIgnoreCase)
            || (defaultModeRequestUserInputEnabled
                && string.Equals(Mode, DefaultMode, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Windows 沙箱级别。
/// Windows sandbox level resolved for the thread session.
/// </summary>
internal enum KernelWindowsSandboxLevel
{
    Disabled = 0,
    Unelevated = 1,
    Elevated = 2,
}

/// <summary>
/// 线程会话运行时状态。
/// Provider-neutral thread session runtime state consumed by execution and kernel paths.
/// </summary>
internal sealed record KernelThreadSessionState(
    string Model,
    string ModelProvider,
    KernelServiceTier? ServiceTier,
    string Cwd,
    KernelApprovalPolicy ApprovalPolicy,
    JsonElement SandboxPolicy,
    string SandboxMode,
    bool AllowLoginShell = true,
    KernelShellEnvironmentPolicy? ShellEnvironmentPolicy = null,
    bool Ephemeral = false,
    string? ServiceName = null,
    string? BaseInstructions = null,
    string? DeveloperInstructions = null,
    string? UserInstructions = null,
    string? ReasoningSummary = null,
    string? Verbosity = null,
    string? Personality = null,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools = null,
    string? ProviderBaseUrl = null,
    string? ProviderApiKeyEnvironmentVariable = null,
    string? ProviderWireApi = null,
    int? ProviderRequestMaxRetries = null,
    int? ProviderStreamMaxRetries = null,
    long? ProviderStreamIdleTimeoutMs = null,
    long? ProviderWebsocketConnectTimeoutMs = null,
    bool? ProviderSupportsWebsockets = null,
    bool ProviderHttpFallbackEnabled = false,
    string? WebSearchMode = null,
    KernelCollaborationModeState? CollaborationMode = null,
    bool PersistExtendedHistory = false,
    KernelWindowsSandboxLevel WindowsSandboxLevel = KernelWindowsSandboxLevel.Disabled,
    bool DefaultModeRequestUserInputEnabled = false,
    KernelSessionSource SessionSource = null!,
    string? ModelRouteSetId = null);
