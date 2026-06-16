namespace TianShu.Execution.Runtime;

internal sealed record KernelThreadConfigSnapshot(
    string Model,
    string ModelProviderId,
    KernelServiceTier? ServiceTier,
    KernelApprovalPolicy ApprovalPolicy,
    System.Text.Json.JsonElement SandboxPolicy,
    string SandboxMode,
    string Cwd,
    bool Ephemeral,
    bool AllowLoginShell,
    KernelShellEnvironmentPolicy ShellEnvironmentPolicy,
    string? ProviderBaseUrl,
    string? ProviderApiKeyEnvironmentVariable,
    string? ProviderWireApi,
    int? ProviderRequestMaxRetries,
    int? ProviderStreamMaxRetries,
    long? ProviderStreamIdleTimeoutMs,
    long? ProviderWebsocketConnectTimeoutMs,
    bool? ProviderSupportsWebsockets,
    bool ProviderHttpFallbackEnabled,
    string? WebSearchMode,
    string? ServiceName,
    string? BaseInstructions,
    string? DeveloperInstructions,
    string? ReasoningEffort,
    string? ReasoningSummary,
    string? Verbosity,
    string? Personality,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools,
    KernelCollaborationModeState? CollaborationMode,
    bool PersistExtendedHistory,
    KernelSessionSource SessionSource,
    string? UserInstructions = null,
    KernelWindowsSandboxLevel WindowsSandboxLevel = KernelWindowsSandboxLevel.Disabled,
    bool DefaultModeRequestUserInputEnabled = false,
    string? ModelRouteSetId = null)
{
    public KernelThreadConfigSnapshot DeepClone()
        => new(
            Model,
            ModelProviderId,
            ServiceTier,
            ApprovalPolicy,
            SandboxPolicy.Clone(),
            SandboxMode,
            Cwd,
            Ephemeral,
            AllowLoginShell,
            KernelThreadConfigSnapshotFactory.CloneShellEnvironmentPolicy(ShellEnvironmentPolicy),
            ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable,
            ProviderWireApi,
            ProviderRequestMaxRetries,
            ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets,
            ProviderHttpFallbackEnabled,
            WebSearchMode,
            ServiceName,
            BaseInstructions,
            DeveloperInstructions,
            ReasoningEffort,
            ReasoningSummary,
            Verbosity,
            Personality,
            KernelDynamicToolResolver.Clone(DynamicTools),
            KernelThreadConfigSnapshotFactory.CloneCollaborationMode(CollaborationMode),
            PersistExtendedHistory,
            SessionSource,
            UserInstructions,
            WindowsSandboxLevel,
            DefaultModeRequestUserInputEnabled,
            ModelRouteSetId);
}

internal static class KernelThreadConfigSnapshotFactory
{
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static KernelThreadConfigSnapshot FromSession(KernelThreadSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new KernelThreadConfigSnapshot(
            Model: session.Model,
            ModelProviderId: session.ModelProvider,
            ServiceTier: session.ServiceTier,
            ApprovalPolicy: session.ApprovalPolicy,
            SandboxPolicy: session.SandboxPolicy.Clone(),
            SandboxMode: session.SandboxMode,
            Cwd: session.Cwd,
            Ephemeral: session.Ephemeral,
            AllowLoginShell: session.AllowLoginShell,
            ShellEnvironmentPolicy: CloneShellEnvironmentPolicy(session.ShellEnvironmentPolicy),
            ProviderBaseUrl: session.ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable: session.ProviderApiKeyEnvironmentVariable,
            ProviderWireApi: session.ProviderWireApi,
            ProviderRequestMaxRetries: session.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries: session.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs: session.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs: session.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets: session.ProviderSupportsWebsockets,
            ProviderHttpFallbackEnabled: false,
            WebSearchMode: session.WebSearchMode,
            ServiceName: session.ServiceName,
            BaseInstructions: session.BaseInstructions,
            DeveloperInstructions: session.DeveloperInstructions,
            UserInstructions: session.UserInstructions,
            ReasoningEffort: session.CollaborationMode?.Settings.ReasoningEffort,
            ReasoningSummary: session.ReasoningSummary,
            Verbosity: session.Verbosity,
            Personality: session.Personality,
            DynamicTools: KernelDynamicToolResolver.Clone(session.DynamicTools),
            CollaborationMode: CloneCollaborationMode(session.CollaborationMode),
            PersistExtendedHistory: session.PersistExtendedHistory,
            SessionSource: session.SessionSource,
            WindowsSandboxLevel: session.WindowsSandboxLevel,
            DefaultModeRequestUserInputEnabled: session.DefaultModeRequestUserInputEnabled,
            ModelRouteSetId: session.ModelRouteSetId);
    }

    public static bool TryRead(System.Text.Json.JsonElement json, out KernelThreadConfigSnapshot? snapshot)
    {
        snapshot = null;
        if (json.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined)
        {
            return false;
        }

        try
        {
            snapshot = System.Text.Json.JsonSerializer.Deserialize<KernelThreadConfigSnapshot>(json.GetRawText(), SerializerOptions)?.DeepClone();
            return snapshot is not null;
        }
        catch
        {
            snapshot = null;
            return false;
        }
    }

    public static KernelShellEnvironmentPolicy CloneShellEnvironmentPolicy(KernelShellEnvironmentPolicy? policy)
    {
        var effectivePolicy = policy ?? KernelShellEnvironmentPolicy.Default;
        return new KernelShellEnvironmentPolicy(
            effectivePolicy.Inherit,
            effectivePolicy.IgnoreDefaultExcludes,
            effectivePolicy.ExcludePatterns,
            effectivePolicy.SetVariables,
            effectivePolicy.IncludeOnlyPatterns,
            effectivePolicy.UseProfile);
    }

    public static KernelCollaborationModeState? CloneCollaborationMode(KernelCollaborationModeState? state)
    {
        if (state is null)
        {
            return null;
        }

        return new KernelCollaborationModeState(
            state.Mode,
            new KernelCollaborationModeSettings(
                state.Settings.Model,
                state.Settings.ReasoningEffort,
                state.Settings.DeveloperInstructions));
    }
}
