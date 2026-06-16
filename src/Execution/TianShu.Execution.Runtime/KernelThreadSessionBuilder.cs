using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Runtime;

internal sealed class KernelThreadSessionBuilder
{
    private static readonly JsonSerializerOptions ProtocolJsonOptions = CreateProtocolJsonOptions();

    private string model;
    private string modelProvider;
    private string? modelRouteSetId;
    private KernelServiceTier? serviceTier;
    private string cwd;
    private KernelApprovalPolicy approvalPolicy;
    private JsonElement sandboxPolicy;
    private string sandboxMode;
    private bool allowLoginShell;
    private KernelShellEnvironmentPolicy shellEnvironmentPolicy;
    private string? providerBaseUrl;
    private string? providerApiKeyEnvironmentVariable;
    private string? providerWireApi;
    private int? providerRequestMaxRetries;
    private int? providerStreamMaxRetries;
    private long? providerStreamIdleTimeoutMs;
    private long? providerWebsocketConnectTimeoutMs;
    private bool? providerSupportsWebsockets;
    private bool providerHttpFallbackEnabled;
    private string? webSearchMode;
    private bool ephemeral;
    private string? serviceName;
    private string? baseInstructions;
    private string? developerInstructions;
    private string? userInstructions;
    private string? reasoningSummary;
    private string? verbosity;
    private string? personality;
    private IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools;
    private KernelCollaborationModeState? collaborationMode;
    private bool persistExtendedHistory;
    private KernelWindowsSandboxLevel windowsSandboxLevel;
    private bool defaultModeRequestUserInputEnabled;
    private KernelSessionSource sessionSource;

    private KernelThreadSessionBuilder(
        string model,
        string modelProvider,
        KernelServiceTier? serviceTier,
        string cwd,
        KernelApprovalPolicy approvalPolicy,
        JsonElement sandboxPolicy,
        string sandboxMode,
        bool allowLoginShell,
        KernelShellEnvironmentPolicy shellEnvironmentPolicy,
        string? providerBaseUrl,
        string? providerApiKeyEnvironmentVariable,
        string? providerWireApi,
        int? providerRequestMaxRetries,
        int? providerStreamMaxRetries,
        long? providerStreamIdleTimeoutMs,
        long? providerWebsocketConnectTimeoutMs,
        bool? providerSupportsWebsockets,
        bool providerHttpFallbackEnabled,
        string? webSearchMode,
        bool ephemeral,
        string? serviceName,
        string? baseInstructions,
        string? developerInstructions,
        string? userInstructions,
        string? reasoningSummary,
        string? verbosity,
        string? personality,
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        KernelCollaborationModeState? collaborationMode,
        bool persistExtendedHistory,
        KernelWindowsSandboxLevel windowsSandboxLevel,
        bool defaultModeRequestUserInputEnabled,
        KernelSessionSource sessionSource,
        string? modelRouteSetId = null)
    {
        this.model = model;
        this.modelProvider = modelProvider;
        this.modelRouteSetId = Normalize(modelRouteSetId);
        this.serviceTier = serviceTier;
        this.cwd = cwd;
        this.approvalPolicy = approvalPolicy;
        this.sandboxPolicy = sandboxPolicy;
        this.sandboxMode = sandboxMode;
        this.allowLoginShell = allowLoginShell;
        this.shellEnvironmentPolicy = shellEnvironmentPolicy;
        this.providerBaseUrl = providerBaseUrl;
        this.providerApiKeyEnvironmentVariable = providerApiKeyEnvironmentVariable;
        this.providerWireApi = providerWireApi;
        this.providerRequestMaxRetries = providerRequestMaxRetries;
        this.providerStreamMaxRetries = providerStreamMaxRetries;
        this.providerStreamIdleTimeoutMs = providerStreamIdleTimeoutMs;
        this.providerWebsocketConnectTimeoutMs = providerWebsocketConnectTimeoutMs;
        this.providerSupportsWebsockets = providerSupportsWebsockets;
        this.providerHttpFallbackEnabled = providerHttpFallbackEnabled;
        this.webSearchMode = webSearchMode;
        this.ephemeral = ephemeral;
        this.serviceName = serviceName;
        this.baseInstructions = baseInstructions;
        this.developerInstructions = developerInstructions;
        this.userInstructions = userInstructions;
        this.reasoningSummary = reasoningSummary;
        this.verbosity = verbosity;
        this.personality = personality;
        this.dynamicTools = KernelDynamicToolResolver.Clone(dynamicTools);
        this.collaborationMode = collaborationMode;
        this.persistExtendedHistory = persistExtendedHistory;
        this.windowsSandboxLevel = windowsSandboxLevel;
        this.defaultModeRequestUserInputEnabled = defaultModeRequestUserInputEnabled;
        this.sessionSource = sessionSource ?? KernelSessionSource.VsCode;
    }

    public static KernelThreadSessionBuilder FromRecord(
        KernelThreadRecord record,
        string defaultModel,
        string defaultModelProvider,
        KernelApprovalPolicy defaultApprovalPolicy,
        JsonElement? defaultSandboxPolicy = null,
        string? defaultSandboxMode = null,
        bool defaultAllowLoginShell = true,
        KernelShellEnvironmentPolicy? defaultShellEnvironmentPolicy = null)
    {
        var sandbox = defaultSandboxPolicy?.Clone() ?? JsonSerializer.SerializeToElement(BuildDefaultSandboxPolicyPayload());
        return new KernelThreadSessionBuilder(
            model: defaultModel,
            modelProvider: defaultModelProvider,
            serviceTier: null,
            cwd: Normalize(record.Cwd) ?? Environment.CurrentDirectory,
            approvalPolicy: defaultApprovalPolicy,
            sandboxPolicy: sandbox,
            sandboxMode: Normalize(defaultSandboxMode) ?? ResolveSandboxMode(sandbox) ?? "workspaceWrite",
            allowLoginShell: defaultAllowLoginShell,
            shellEnvironmentPolicy: defaultShellEnvironmentPolicy ?? KernelShellEnvironmentPolicy.Default,
            providerBaseUrl: null,
            providerApiKeyEnvironmentVariable: null,
            providerWireApi: null,
            providerRequestMaxRetries: null,
            providerStreamMaxRetries: null,
            providerStreamIdleTimeoutMs: null,
            providerWebsocketConnectTimeoutMs: null,
            providerSupportsWebsockets: null,
            providerHttpFallbackEnabled: false,
            webSearchMode: null,
            ephemeral: false,
            serviceName: null,
            baseInstructions: null,
            developerInstructions: null,
            userInstructions: null,
            reasoningSummary: null,
            verbosity: null,
            personality: null,
            dynamicTools: null,
            collaborationMode: KernelCollaborationModeState.CreateDefault(defaultModel),
            persistExtendedHistory: false,
            windowsSandboxLevel: KernelWindowsSandboxLevel.Disabled,
            defaultModeRequestUserInputEnabled: false,
            sessionSource: KernelSessionSource.VsCode);
    }

    public static KernelThreadSessionBuilder FromSession(KernelThreadSessionState session)
    {
        return new KernelThreadSessionBuilder(
            model: session.Model,
            modelProvider: session.ModelProvider,
            serviceTier: session.ServiceTier,
            cwd: session.Cwd,
            approvalPolicy: session.ApprovalPolicy,
            sandboxPolicy: session.SandboxPolicy.Clone(),
            sandboxMode: session.SandboxMode,
            allowLoginShell: session.AllowLoginShell,
            shellEnvironmentPolicy: session.ShellEnvironmentPolicy ?? KernelShellEnvironmentPolicy.Default,
            providerBaseUrl: session.ProviderBaseUrl,
            providerApiKeyEnvironmentVariable: session.ProviderApiKeyEnvironmentVariable,
            providerWireApi: session.ProviderWireApi,
            providerRequestMaxRetries: session.ProviderRequestMaxRetries,
            providerStreamMaxRetries: session.ProviderStreamMaxRetries,
            providerStreamIdleTimeoutMs: session.ProviderStreamIdleTimeoutMs,
            providerWebsocketConnectTimeoutMs: session.ProviderWebsocketConnectTimeoutMs,
            providerSupportsWebsockets: session.ProviderSupportsWebsockets,
            providerHttpFallbackEnabled: session.ProviderHttpFallbackEnabled,
            webSearchMode: session.WebSearchMode,
            ephemeral: session.Ephemeral,
            serviceName: session.ServiceName,
            baseInstructions: session.BaseInstructions,
            developerInstructions: session.DeveloperInstructions,
            userInstructions: session.UserInstructions,
            reasoningSummary: session.ReasoningSummary,
            verbosity: session.Verbosity,
            personality: session.Personality,
            dynamicTools: KernelDynamicToolResolver.Clone(session.DynamicTools),
            collaborationMode: KernelCollaborationModeState.NormalizeOrDefault(
                session.CollaborationMode,
                session.Model),
            persistExtendedHistory: session.PersistExtendedHistory,
            windowsSandboxLevel: session.WindowsSandboxLevel,
            defaultModeRequestUserInputEnabled: session.DefaultModeRequestUserInputEnabled,
            sessionSource: session.SessionSource,
            modelRouteSetId: session.ModelRouteSetId);
    }

    public KernelThreadSessionBuilder ApplyConfigSnapshot(KernelThreadConfigSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return this;
        }

        model = Normalize(snapshot.Model) ?? model;
        modelProvider = Normalize(snapshot.ModelProviderId) ?? modelProvider;
        modelRouteSetId = Normalize(snapshot.ModelRouteSetId) ?? modelRouteSetId;
        serviceTier = snapshot.ServiceTier;
        cwd = Normalize(snapshot.Cwd) ?? cwd;
        approvalPolicy = snapshot.ApprovalPolicy;
        sandboxPolicy = snapshot.SandboxPolicy.Clone();
        sandboxMode = Normalize(snapshot.SandboxMode) ?? ResolveSandboxMode(snapshot.SandboxPolicy) ?? sandboxMode;
        allowLoginShell = snapshot.AllowLoginShell;
        shellEnvironmentPolicy = KernelThreadConfigSnapshotFactory.CloneShellEnvironmentPolicy(snapshot.ShellEnvironmentPolicy);
        providerBaseUrl = snapshot.ProviderBaseUrl;
        providerApiKeyEnvironmentVariable = snapshot.ProviderApiKeyEnvironmentVariable;
        providerWireApi = snapshot.ProviderWireApi;
        providerRequestMaxRetries = snapshot.ProviderRequestMaxRetries;
        providerStreamMaxRetries = snapshot.ProviderStreamMaxRetries;
        providerStreamIdleTimeoutMs = snapshot.ProviderStreamIdleTimeoutMs;
        providerWebsocketConnectTimeoutMs = snapshot.ProviderWebsocketConnectTimeoutMs;
        providerSupportsWebsockets = snapshot.ProviderSupportsWebsockets;
        providerHttpFallbackEnabled = snapshot.ProviderHttpFallbackEnabled;
        webSearchMode = snapshot.WebSearchMode;
        ephemeral = snapshot.Ephemeral;
        serviceName = snapshot.ServiceName;
        baseInstructions = snapshot.BaseInstructions ?? baseInstructions;
        developerInstructions = snapshot.DeveloperInstructions ?? developerInstructions;
        userInstructions = snapshot.UserInstructions ?? userInstructions;
        reasoningSummary = snapshot.ReasoningSummary ?? reasoningSummary;
        verbosity = snapshot.Verbosity ?? verbosity;
        personality = snapshot.Personality;
        dynamicTools = KernelDynamicToolResolver.Clone(snapshot.DynamicTools);
        collaborationMode = snapshot.CollaborationMode is null
            ? KernelCollaborationModeState.CreateDefault(model, snapshot.ReasoningEffort)
            : KernelThreadConfigSnapshotFactory.CloneCollaborationMode(snapshot.CollaborationMode);
        persistExtendedHistory = snapshot.PersistExtendedHistory;
        windowsSandboxLevel = snapshot.WindowsSandboxLevel;
        defaultModeRequestUserInputEnabled = snapshot.DefaultModeRequestUserInputEnabled;
        sessionSource = snapshot.SessionSource ?? sessionSource;
        return this;
    }

    /// <summary>
    /// 仅刷新 Provider 连接参数，避免模型切换时重写会话的审批、沙箱与提示词快照。
    /// Refreshes provider connection settings only, without rewriting approval, sandbox, or prompt snapshots during model switches.
    /// </summary>
    public KernelThreadSessionBuilder ApplyProviderConnectionSnapshot(KernelThreadConfigSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return this;
        }

        providerBaseUrl = snapshot.ProviderBaseUrl;
        providerApiKeyEnvironmentVariable = snapshot.ProviderApiKeyEnvironmentVariable;
        providerWireApi = snapshot.ProviderWireApi;
        providerRequestMaxRetries = snapshot.ProviderRequestMaxRetries;
        providerStreamMaxRetries = snapshot.ProviderStreamMaxRetries;
        providerStreamIdleTimeoutMs = snapshot.ProviderStreamIdleTimeoutMs;
        providerWebsocketConnectTimeoutMs = snapshot.ProviderWebsocketConnectTimeoutMs;
        providerSupportsWebsockets = snapshot.ProviderSupportsWebsockets;
        providerHttpFallbackEnabled = snapshot.ProviderHttpFallbackEnabled;
        return this;
    }

    public KernelThreadSessionBuilder ApplyThreadStart(JsonElement @params)
    {
        if (@params.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return this;
        }

        var request = JsonSerializer.Deserialize<KernelThreadStartRequest>(
            @params.GetRawText(),
            ProtocolJsonOptions)
            ?? new KernelThreadStartRequest();
        return ApplyThreadStart(request);
    }

    public KernelThreadSessionBuilder ApplyThreadStart(KernelThreadStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        model = Normalize(request.Model) ?? model;
        modelProvider = Normalize(request.ModelProvider) ?? modelProvider;
        if (request.ServiceTier.IsSpecified)
        {
            serviceTier = request.ServiceTier.Value;
        }

        cwd = Normalize(request.Cwd) ?? cwd;
        approvalPolicy = request.ApprovalPolicy ?? approvalPolicy;
        if (request.Sandbox is { } sandbox)
        {
            sandboxPolicy = sandbox.ToJsonElement();
            sandboxMode = ResolveSandboxMode(sandboxPolicy) ?? sandboxMode;
        }
        ephemeral = request.Ephemeral ?? ephemeral;
        serviceName = Normalize(request.ServiceName) ?? serviceName;
        baseInstructions = request.BaseInstructions is not null
            ? Normalize(request.BaseInstructions)
            : baseInstructions;
        developerInstructions = request.DeveloperInstructions is not null
            ? Normalize(request.DeveloperInstructions)
            : developerInstructions;
        personality = request.Personality is not null
            ? Normalize(request.Personality.Value)
            : personality;
        dynamicTools = request.DynamicTools is { } descriptors
            ? KernelDynamicToolResolver.Clone(descriptors)
            : dynamicTools;
        persistExtendedHistory = request.PersistExtendedHistory;
        sessionSource = request.SessionSource ?? sessionSource;
        ApplyCollaborationMode((KernelCollaborationModeOverride?)null);
        return this;
    }

    public KernelThreadSessionBuilder ApplyTurnOverrides(JsonElement @params)
    {
        if (@params.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return this;
        }

        var request = JsonSerializer.Deserialize<KernelTurnStartRequest>(
            @params.GetRawText(),
            ProtocolJsonOptions)
            ?? new KernelTurnStartRequest();
        return ApplyTurnOverrides(request);
    }

    public KernelThreadSessionBuilder ApplyTurnOverrides(KernelTurnStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        model = Normalize(request.Model) ?? model;
        if (request.ServiceTier.IsSpecified)
        {
            serviceTier = request.ServiceTier.Value;
        }

        cwd = Normalize(request.Cwd) ?? cwd;
        providerBaseUrl = Normalize(request.ProviderBaseUrl) ?? providerBaseUrl;
        providerApiKeyEnvironmentVariable = Normalize(request.ProviderApiKeyEnvironmentVariable) ?? providerApiKeyEnvironmentVariable;
        providerWireApi = ProviderWireApi.NormalizeOrThrow(request.ProviderWireApi, "turn/start.providerWireApi") ?? providerWireApi;
        providerRequestMaxRetries = request.ProviderRequestMaxRetries ?? providerRequestMaxRetries;
        providerStreamMaxRetries = request.ProviderStreamMaxRetries ?? providerStreamMaxRetries;
        providerStreamIdleTimeoutMs = request.ProviderStreamIdleTimeoutMs ?? providerStreamIdleTimeoutMs;
        providerWebsocketConnectTimeoutMs = request.ProviderWebsocketConnectTimeoutMs ?? providerWebsocketConnectTimeoutMs;
        providerSupportsWebsockets = request.ProviderSupportsWebsockets ?? providerSupportsWebsockets;
        approvalPolicy = request.ApprovalPolicy ?? approvalPolicy;
        if (request.SandboxPolicy is { } sandbox)
        {
            sandboxPolicy = sandbox.ToJsonElement();
            sandboxMode = ResolveSandboxMode(sandboxPolicy) ?? sandboxMode;
        }

        reasoningSummary = request.Summary is not null
            ? Normalize(request.Summary)
            : reasoningSummary;
        verbosity = request.Verbosity is not null
            ? Normalize(request.Verbosity)
            : verbosity;
        personality = request.Personality is not null
            ? Normalize(request.Personality.Value)
            : personality;
        ApplyReasoningEffort(Normalize(request.Effort));
        ApplyCollaborationMode(request.CollaborationMode);
        return this;
    }

    public KernelThreadSessionBuilder ApplyThreadResume(KernelThreadResumeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        model = Normalize(request.Model) ?? model;
        modelProvider = Normalize(request.ModelProvider) ?? modelProvider;
        if (request.ServiceTier.IsSpecified)
        {
            serviceTier = request.ServiceTier.Value;
        }

        cwd = Normalize(request.Cwd) ?? cwd;
        approvalPolicy = request.ApprovalPolicy ?? approvalPolicy;
        if (request.Sandbox is { } sandbox)
        {
            sandboxPolicy = sandbox.ToJsonElement();
            sandboxMode = ResolveSandboxMode(sandboxPolicy) ?? sandboxMode;
        }
        baseInstructions = request.BaseInstructions is not null
            ? Normalize(request.BaseInstructions)
            : baseInstructions;
        developerInstructions = request.DeveloperInstructions is not null
            ? Normalize(request.DeveloperInstructions)
            : developerInstructions;
        personality = request.Personality is not null
            ? Normalize(request.Personality.Value)
            : personality;
        persistExtendedHistory = request.PersistExtendedHistory;
        sessionSource = request.SessionSource ?? sessionSource;
        return this;
    }

    public KernelThreadSessionBuilder ApplyThreadFork(KernelThreadForkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        model = Normalize(request.Model) ?? model;
        modelProvider = Normalize(request.ModelProvider) ?? modelProvider;
        if (request.ServiceTier.IsSpecified)
        {
            serviceTier = request.ServiceTier.Value;
        }

        cwd = Normalize(request.Cwd) ?? cwd;
        approvalPolicy = request.ApprovalPolicy ?? approvalPolicy;
        if (request.Sandbox is { } sandbox)
        {
            sandboxPolicy = sandbox.ToJsonElement();
            sandboxMode = ResolveSandboxMode(sandboxPolicy) ?? sandboxMode;
        }
        baseInstructions = request.BaseInstructions is not null
            ? Normalize(request.BaseInstructions)
            : baseInstructions;
        developerInstructions = request.DeveloperInstructions is not null
            ? Normalize(request.DeveloperInstructions)
            : developerInstructions;
        ephemeral = request.Ephemeral;
        persistExtendedHistory = request.PersistExtendedHistory;
        sessionSource = request.SessionSource ?? sessionSource;
        return this;
    }

    public KernelThreadSessionState Build()
    {
        return new KernelThreadSessionState(
            Model: model,
            ModelProvider: modelProvider,
            ServiceTier: serviceTier,
            Cwd: cwd,
            ApprovalPolicy: approvalPolicy,
            SandboxPolicy: sandboxPolicy,
            SandboxMode: sandboxMode,
            AllowLoginShell: allowLoginShell,
            ShellEnvironmentPolicy: shellEnvironmentPolicy,
            ProviderBaseUrl: providerBaseUrl,
            ProviderApiKeyEnvironmentVariable: providerApiKeyEnvironmentVariable,
            ProviderWireApi: providerWireApi,
            ProviderRequestMaxRetries: providerRequestMaxRetries,
            ProviderStreamMaxRetries: providerStreamMaxRetries,
            ProviderStreamIdleTimeoutMs: providerStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs: providerWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets: providerSupportsWebsockets,
            ProviderHttpFallbackEnabled: providerHttpFallbackEnabled,
            WebSearchMode: webSearchMode,
            Ephemeral: ephemeral,
            ServiceName: serviceName,
            BaseInstructions: baseInstructions,
            DeveloperInstructions: developerInstructions,
            UserInstructions: userInstructions,
            ReasoningSummary: reasoningSummary,
            Verbosity: verbosity,
            Personality: personality,
            DynamicTools: dynamicTools,
            CollaborationMode: KernelCollaborationModeState.NormalizeOrDefault(
                collaborationMode,
                model),
            PersistExtendedHistory: persistExtendedHistory,
            WindowsSandboxLevel: windowsSandboxLevel,
            DefaultModeRequestUserInputEnabled: defaultModeRequestUserInputEnabled,
            SessionSource: sessionSource,
            ModelRouteSetId: modelRouteSetId);
    }

    private static object BuildDefaultSandboxPolicyPayload()
        => new
        {
            type = "workspaceWrite",
            writableRoots = Array.Empty<string>(),
            readOnlyAccess = new { type = "fullAccess" },
            networkAccess = false,
            excludeTmpdirEnvVar = false,
            excludeSlashTmp = false,
        };

    private void ApplyCollaborationMode(JsonElement @params)
    {
        ApplyCollaborationMode(TryReadJsonProperty(@params, "collaborationMode"));
    }

    private void ApplyCollaborationMode(KernelCollaborationModeOverride? overrideValue)
    {
        var current = KernelCollaborationModeState.NormalizeOrDefault(
            collaborationMode,
            model);

        if (overrideValue is null)
        {
            collaborationMode = current with
            {
                Settings = current.Settings with
                {
                    Model = model,
                },
            };
            return;
        }

        collaborationMode = new KernelCollaborationModeState(
            Normalize(overrideValue.Mode) ?? current.Mode,
            new KernelCollaborationModeSettings(
                Normalize(overrideValue.Model) ?? model,
                overrideValue.ReasoningEffort ?? current.Settings.ReasoningEffort,
                overrideValue.DeveloperInstructions.IsSpecified
                    ? Normalize(overrideValue.DeveloperInstructions.Value)
                    : current.Settings.DeveloperInstructions));
        model = collaborationMode.Settings.Model;
    }

    private void ApplyReasoningEffort(string? reasoningEffort)
    {
        var normalized = Normalize(reasoningEffort);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var current = KernelCollaborationModeState.NormalizeOrDefault(
            collaborationMode,
            model);
        collaborationMode = current with
        {
            Settings = current.Settings with
            {
                Model = model,
                ReasoningEffort = normalized,
            },
        };
    }

    private void ApplyCollaborationMode(JsonElement? rawCollaborationMode)
    {
        var current = KernelCollaborationModeState.NormalizeOrDefault(
            collaborationMode,
            model);

        if (rawCollaborationMode is null)
        {
            collaborationMode = current with
            {
                Settings = current.Settings with
                {
                    Model = model,
                },
            };
            return;
        }

        collaborationMode = ParseCollaborationMode(rawCollaborationMode.Value, current, model);
        model = collaborationMode.Settings.Model;
    }

    private static KernelCollaborationModeState ParseCollaborationMode(
        JsonElement rawCollaborationMode,
        KernelCollaborationModeState current,
        string currentModel)
    {
        if (rawCollaborationMode.ValueKind == JsonValueKind.String)
        {
            return current with { Mode = Normalize(rawCollaborationMode.GetString()) ?? current.Mode };
        }

        if (rawCollaborationMode.ValueKind != JsonValueKind.Object)
        {
            return current;
        }

        var mode = Normalize(ReadString(rawCollaborationMode, "mode")) ?? current.Mode;
        var settings = TryReadJsonProperty(rawCollaborationMode, "settings");
        var settingsModel = settings is { ValueKind: JsonValueKind.Object }
            ? Normalize(ReadString(settings.Value, "model"))
            : null;
        var settingsReasoningEffort = settings is { ValueKind: JsonValueKind.Object }
            ? Normalize(ReadString(settings.Value, "reasoning_effort")) ?? Normalize(ReadString(settings.Value, "reasoningEffort"))
            : null;
        var hasDeveloperInstructions = settings is { ValueKind: JsonValueKind.Object }
            && (HasProperty(settings.Value, "developer_instructions") || HasProperty(settings.Value, "developerInstructions"));
        var settingsDeveloperInstructions = settings is { ValueKind: JsonValueKind.Object }
            ? Normalize(ReadString(settings.Value, "developer_instructions")) ?? Normalize(ReadString(settings.Value, "developerInstructions"))
            : null;

        return new KernelCollaborationModeState(
            mode,
            new KernelCollaborationModeSettings(
                settingsModel ?? currentModel,
                settingsReasoningEffort ?? current.Settings.ReasoningEffort,
                hasDeveloperInstructions
                    ? settingsDeveloperInstructions
                    : current.Settings.DeveloperInstructions));
    }


    private static string? ResolveSandboxMode(JsonElement policy)
    {
        if (policy.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return Normalize(ReadString(policy, "type"));
    }

    private static JsonElement? TryReadSandboxPolicy(JsonElement @params, params string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            if (TryReadJsonProperty(@params, name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryReadJsonProperty(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        value = property.Clone();
        return true;
    }

    private static JsonElement? TryReadJsonProperty(JsonElement json, string propertyName)
        => TryReadJsonProperty(json, propertyName, out var value) ? value : null;

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Null => null,
            _ => property.GetRawText(),
        };
    }

    private static bool HasProperty(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out _);

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static JsonSerializerOptions CreateProtocolJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new KernelOptionalJsonConverterFactory());
        return options;
    }
}
