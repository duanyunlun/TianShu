using System.Text.Json;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Contracts.Interactions;
using TianShu.Configuration;

namespace TianShu.RuntimeComposition;

/// <summary>
/// AppHost turn request context 工厂，负责把宿主会话状态投影为基础 turn context。
/// AppHost turn request context factory that projects host session state into the base turn context.
/// </summary>
internal static class AppHostTurnRequestContextFactory
{
    private static readonly StringComparer EnvironmentVariableComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static TurnRequestContext CreateFromTransportParams(
        KernelRuntimeThread runtimeThread,
        KernelThreadSessionState session,
        JsonElement parameters,
        TianShuPromptConfiguration? promptConfiguration,
        string? realtimeDeveloperInstructions)
    {
        ArgumentNullException.ThrowIfNull(runtimeThread);
        ArgumentNullException.ThrowIfNull(session);

        return Create(
            runtimeThread,
            session,
            promptConfiguration,
            realtimeDeveloperInstructions,
            TryReadJsonProperty(parameters, "outputSchema", out var rawOutputSchema)
                ? KernelJsonSchemaPayload.FromElement(rawOutputSchema)
                : null,
            TryReadJsonProperty(parameters, "input", out var rawInput)
                ? KernelThreadTransportParsers.ParseTurnInputItems(rawInput)
                : null,
            TryReadJsonProperty(parameters, "interactionEnvelope", out var rawInteractionEnvelope)
                ? JsonSerializer.Deserialize<InteractionEnvelopeRef>(rawInteractionEnvelope.GetRawText())
                : null);
    }

    public static TurnRequestContext CreateFromTurnStartRequest(
        KernelRuntimeThread runtimeThread,
        KernelThreadSessionState session,
        KernelTurnStartRequest request,
        TianShuPromptConfiguration? promptConfiguration,
        string? realtimeDeveloperInstructions)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Create(
            runtimeThread,
            session,
            promptConfiguration,
            realtimeDeveloperInstructions,
            request.OutputSchema,
            request.Input,
            request.InteractionEnvelope?.ToContract());
    }

    public static TurnRequestContext CreateBase(
        KernelThreadSessionState session,
        TianShuPromptConfiguration? promptConfiguration)
    {
        ArgumentNullException.ThrowIfNull(session);

        return Create(
            runtimeThread: null,
            session,
            promptConfiguration,
            realtimeDeveloperInstructions: null,
            outputSchema: null,
            inputItems: null,
            interactionEnvelope: null);
    }

    private static TurnRequestContext Create(
        KernelRuntimeThread? runtimeThread,
        KernelThreadSessionState session,
        TianShuPromptConfiguration? promptConfiguration,
        string? realtimeDeveloperInstructions,
        KernelJsonSchemaPayload? outputSchema,
        IReadOnlyList<KernelTurnInputItem>? inputItems,
        InteractionEnvelopeRef? interactionEnvelope)
        => new(
            Model: session.Model,
            ModelProvider: session.ModelProvider,
            ServiceTier: session.ServiceTier,
            ApprovalPolicy: session.ApprovalPolicy,
            SandboxPolicy: session.SandboxPolicy,
            SandboxMode: session.SandboxMode,
            AllowLoginShell: session.AllowLoginShell,
            ShellEnvironmentPolicy: session.ShellEnvironmentPolicy,
            Cwd: session.Cwd,
            ProviderBaseUrl: session.ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable: session.ProviderApiKeyEnvironmentVariable,
            ProviderWireApi: session.ProviderWireApi,
            ProviderRequestMaxRetries: session.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries: session.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs: session.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs: session.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets: session.ProviderSupportsWebsockets,
            WebSearchMode: session.WebSearchMode,
            IsReview: false,
            ReviewDisplayText: null,
            DynamicTools: session.DynamicTools,
            BaseInstructions: session.BaseInstructions,
            DeveloperInstructions: session.DeveloperInstructions,
            UserInstructions: session.UserInstructions,
            ReasoningSummary: session.ReasoningSummary,
            Verbosity: session.Verbosity,
            OutputSchema: outputSchema,
            InputItems: inputItems,
            ExplicitSkillInjections: [],
            DependencyEnvironment: runtimeThread is null
                ? new Dictionary<string, string>(EnvironmentVariableComparer)
                : runtimeThread.CopyDependencyEnvironment(),
            CollaborationMode: KernelCollaborationModeState.NormalizeOrDefault(
                session.CollaborationMode,
                session.Model),
            PromptConfiguration: promptConfiguration,
            DefaultModeRequestUserInputEnabled: session.DefaultModeRequestUserInputEnabled,
            SessionSource: session.SessionSource,
            RealtimeDeveloperInstructions: realtimeDeveloperInstructions,
            WindowsSandboxLevel: session.WindowsSandboxLevel,
            InteractionEnvelope: interactionEnvelope);

    private static bool TryReadJsonProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
