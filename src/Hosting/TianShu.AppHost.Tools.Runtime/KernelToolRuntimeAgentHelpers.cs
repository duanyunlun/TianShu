using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelSpawnedAgentSessionContext(
    string? Model,
    string? ModelProvider,
    KernelServiceTier? ServiceTier,
    KernelApprovalPolicy? ApprovalPolicy,
    JsonElement? SandboxPolicy,
    string? SandboxMode,
    bool AllowLoginShell = true,
    KernelShellEnvironmentPolicy? ShellEnvironmentPolicy = null,
    string? Cwd = null,
    string? ProviderBaseUrl = null,
    string? ProviderApiKeyEnvironmentVariable = null,
    string? ProviderWireApi = null,
    int? ProviderRequestMaxRetries = null,
    int? ProviderStreamMaxRetries = null,
    long? ProviderStreamIdleTimeoutMs = null,
    long? ProviderWebsocketConnectTimeoutMs = null,
    bool? ProviderSupportsWebsockets = null,
    string? WebSearchMode = null,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools = null,
    string? DeveloperInstructions = null,
    string? UserInstructions = null,
    string? ReasoningSummary = null,
    string? Verbosity = null,
    KernelCollaborationModeState? CollaborationMode = null);

internal static class KernelToolRuntimeAgentHelpers
{
    public static readonly TimeSpan WaitPollInterval = TimeSpan.FromMilliseconds(250);

    public static int NormalizeWaitTimeoutMs(int? timeoutMs)
    {
        const int minTimeoutMs = 10_000;
        const int defaultTimeoutMs = 30_000;
        const int maxTimeoutMs = 3_600_000;

        if (timeoutMs is <= 0)
        {
            throw new InvalidOperationException("timeout_ms must be greater than zero");
        }

        return timeoutMs switch
        {
            null => defaultTimeoutMs,
            < minTimeoutMs => minTimeoutMs,
            > maxTimeoutMs => maxTimeoutMs,
            var value => value.Value,
        };
    }

    public static KernelSessionSource BuildSpawnedAgentSource(
        KernelSessionSource? parentSource,
        string parentThreadId,
        string? agentRole,
        string? agentNickname,
        int? childDepth = null)
    {
        var nextDepth = childDepth ?? Math.Max(parentSource?.GetThreadSpawnDepth() ?? 0, 0) + 1;
        return KernelSessionSource.SubAgent(
            KernelSubAgentSource.ThreadSpawn(
                parentThreadId,
                nextDepth,
                agentNickname: KernelToolJsonHelpers.Normalize(agentNickname),
                agentRole: KernelToolJsonHelpers.Normalize(agentRole)));
    }

    public static JsonNode? BuildAgentStatusNode(
        KernelThreadRecord? record,
        bool treatArchivedAsNotFound,
        bool hasRunningTurn)
    {
        if (record is null)
        {
            return CreateAgentStatusNode("not_found");
        }

        if (record.IsArchived)
        {
            return treatArchivedAsNotFound
                ? CreateAgentStatusNode("not_found")
                : CreateAgentStatusNode("shutdown");
        }

        if (hasRunningTurn)
        {
            return CreateAgentStatusNode("running");
        }

        var latestTurn = record.Turns.Count == 0 ? null : record.Turns[^1];
        if (latestTurn is null)
        {
            return CreateAgentStatusNode("pending_init");
        }

        var latestTurnStatus = ResolveAgentLatestTurnStatus(latestTurn);
        return latestTurnStatus switch
        {
            "completed" => CreateAgentStatusNode("completed", KernelToolJsonHelpers.Normalize(latestTurn.AssistantMessage) ?? KernelToolJsonHelpers.Normalize(record.LastAssistantMessage)),
            "failed" => CreateAgentStatusNode("errored", KernelToolJsonHelpers.Normalize(latestTurn.AssistantMessage) ?? "Failed"),
            "interrupted" => CreateAgentStatusNode("errored", KernelToolJsonHelpers.Normalize(latestTurn.AssistantMessage) ?? "Interrupted"),
            _ => CreateAgentStatusNode("completed", KernelToolJsonHelpers.Normalize(record.LastAssistantMessage)),
        };
    }

    public static KernelThreadSessionState BuildSpawnedAgentSession(
        KernelThreadSessionState baseSession,
        KernelSpawnedAgentSessionContext turnContext,
        string? cwdOverride,
        string? modelOverride = null,
        string? reasoningEffortOverride = null,
        KernelSessionSource? sessionSourceOverride = null,
        string? developerInstructionsOverride = null)
    {
        var effectiveModel = KernelToolJsonHelpers.Normalize(modelOverride) ?? KernelToolJsonHelpers.Normalize(turnContext.Model) ?? baseSession.Model;
        var effectiveDeveloperInstructions = KernelToolJsonHelpers.Normalize(developerInstructionsOverride)
                                             ?? KernelToolJsonHelpers.Normalize(turnContext.DeveloperInstructions)
                                             ?? baseSession.DeveloperInstructions;
        var effectiveCollaborationMode = turnContext.CollaborationMode ?? baseSession.CollaborationMode;
        if (effectiveCollaborationMode is not null)
        {
            effectiveCollaborationMode = KernelCollaborationModeState.NormalizeOrDefault(
                effectiveCollaborationMode,
                effectiveModel);
        }

        if (!string.IsNullOrWhiteSpace(modelOverride)
            || !string.IsNullOrWhiteSpace(reasoningEffortOverride)
            || !string.IsNullOrWhiteSpace(developerInstructionsOverride))
        {
            effectiveCollaborationMode = KernelCollaborationModeState.NormalizeOrDefault(
                effectiveCollaborationMode,
                effectiveModel) with
            {
                Settings = new KernelCollaborationModeSettings(
                    effectiveModel,
                    KernelToolJsonHelpers.Normalize(reasoningEffortOverride) ?? effectiveCollaborationMode?.Settings.ReasoningEffort,
                    effectiveDeveloperInstructions ?? effectiveCollaborationMode?.Settings.DeveloperInstructions),
            };
        }

        return new KernelThreadSessionState(
            Model: effectiveModel,
            ModelProvider: KernelToolJsonHelpers.Normalize(turnContext.ModelProvider) ?? baseSession.ModelProvider,
            ServiceTier: turnContext.ServiceTier ?? baseSession.ServiceTier,
            Cwd: KernelToolJsonHelpers.Normalize(cwdOverride) ?? KernelToolJsonHelpers.Normalize(turnContext.Cwd) ?? baseSession.Cwd,
            ApprovalPolicy: turnContext.ApprovalPolicy ?? baseSession.ApprovalPolicy,
            SandboxPolicy: turnContext.SandboxPolicy?.Clone() ?? baseSession.SandboxPolicy.Clone(),
            SandboxMode: KernelToolJsonHelpers.Normalize(turnContext.SandboxMode) ?? baseSession.SandboxMode,
            AllowLoginShell: turnContext.AllowLoginShell,
            ShellEnvironmentPolicy: turnContext.ShellEnvironmentPolicy ?? baseSession.ShellEnvironmentPolicy,
            ProviderBaseUrl: KernelToolJsonHelpers.Normalize(turnContext.ProviderBaseUrl) ?? baseSession.ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable: KernelToolJsonHelpers.Normalize(turnContext.ProviderApiKeyEnvironmentVariable) ?? baseSession.ProviderApiKeyEnvironmentVariable,
            ProviderWireApi: ProviderWireApi.NormalizeOrThrow(turnContext.ProviderWireApi, "turn context providerWireApi") ?? baseSession.ProviderWireApi,
            ProviderRequestMaxRetries: turnContext.ProviderRequestMaxRetries ?? baseSession.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries: turnContext.ProviderStreamMaxRetries ?? baseSession.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs: turnContext.ProviderStreamIdleTimeoutMs ?? baseSession.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs: turnContext.ProviderWebsocketConnectTimeoutMs ?? baseSession.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets: turnContext.ProviderSupportsWebsockets ?? baseSession.ProviderSupportsWebsockets,
            WebSearchMode: KernelToolJsonHelpers.Normalize(turnContext.WebSearchMode) ?? baseSession.WebSearchMode,
            Ephemeral: baseSession.Ephemeral,
            ServiceName: baseSession.ServiceName,
            BaseInstructions: baseSession.BaseInstructions,
            DeveloperInstructions: effectiveDeveloperInstructions,
            UserInstructions: turnContext.UserInstructions ?? baseSession.UserInstructions,
            ReasoningSummary: KernelToolJsonHelpers.Normalize(turnContext.ReasoningSummary) ?? baseSession.ReasoningSummary,
            Verbosity: KernelToolJsonHelpers.Normalize(turnContext.Verbosity) ?? baseSession.Verbosity,
            Personality: baseSession.Personality,
            DynamicTools: KernelDynamicToolResolver.Clone(turnContext.DynamicTools) ?? KernelDynamicToolResolver.Clone(baseSession.DynamicTools),
            CollaborationMode: effectiveCollaborationMode,
            PersistExtendedHistory: baseSession.PersistExtendedHistory,
            WindowsSandboxLevel: baseSession.WindowsSandboxLevel,
            DefaultModeRequestUserInputEnabled: baseSession.DefaultModeRequestUserInputEnabled,
            SessionSource: sessionSourceOverride ?? baseSession.SessionSource);
    }

    public static bool IsFinalAgentStatus(JsonNode? status)
    {
        var value = status?.ToJsonString();
        return value switch
        {
            "\"pending_init\"" => false,
            "\"running\"" => false,
            _ => true,
        };
    }

    private static string ResolveAgentLatestTurnStatus(KernelTurnRecord latestTurn)
    {
        var normalizedStatus = KernelToolJsonHelpers.Normalize(latestTurn.Status);
        if (string.Equals(normalizedStatus, "inProgress", StringComparison.OrdinalIgnoreCase))
        {
            // 没有 live turn 时，持久化的 inProgress 只可能是陈旧状态，必须收口为终态。
            return "interrupted";
        }

        return normalizedStatus ?? string.Empty;
    }

    private static JsonNode? CreateAgentStatusNode(string kind, string? message = null)
    {
        return kind switch
        {
            "completed" => new JsonObject { ["completed"] = message is null ? null : JsonValue.Create(message) },
            "errored" => new JsonObject { ["errored"] = message is null ? null : JsonValue.Create(message) },
            _ => JsonValue.Create(kind),
        };
    }
}
