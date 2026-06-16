using TianShu.AppHost.State;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;

namespace TianShu.RuntimeComposition;

/// <summary>
/// AppHost Core Loop 模型路由解析器，负责把 Stage route kind 固化为 turn 模型绑定。
/// AppHost core-loop model route resolver that pins a stage route kind to turn model bindings.
/// </summary>
internal sealed class AppHostCoreLoopModelRouteResolver
{
    private readonly Func<string?, string?> normalize;

    public AppHostCoreLoopModelRouteResolver(Func<string?, string?> normalize)
    {
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
    }

    public AppHostCoreLoopModelRouteResolution Resolve(
        string? threadId,
        KernelThreadSessionState session,
        ModelRouteKind routeKind,
        TurnRequestContext context,
        IReadOnlyList<ModelRouteKind> registeredRouteKinds,
        Dictionary<string, object?> rawConfig)
    {
        var routeSetId = normalize(session.ModelRouteSetId) ?? TianShu.Configuration.TianShuModelRouteSetDefaults.DefaultRouteSetId;
        var outcome = DefaultModelRouter.Instance.Resolve(new DefaultModelRouteResolutionContext(
            StructuredValue.FromPlainObject(rawConfig),
            new ModelRouteResolutionRequest(
                routeSetId,
                routeKind,
                workspacePath: session.Cwd,
                threadId: threadId,
                registeredRouteKinds: registeredRouteKinds),
            SessionReasoningEffort: session.CollaborationMode?.Settings.ReasoningEffort,
            SessionReasoningSummary: session.ReasoningSummary,
            SessionVerbosity: session.Verbosity,
            RequireEnvironmentSecretValue: false,
            ValidateProtocolBinding: false));
        if (!outcome.Succeeded)
        {
            var failure = outcome.Failure!;
            throw new InvalidOperationException($"模型路由解析失败：{failure.ReasonCode}，{failure.Message}");
        }

        var result = outcome.Result!;
        return new AppHostCoreLoopModelRouteResolution(
            context with
            {
                Model = result.Model,
                ModelProvider = result.ProviderId,
                ProviderBaseUrl = result.BaseUrl,
                ProviderApiKeyEnvironmentVariable = result.ApiKeyEnvironmentVariable,
                ProviderWireApi = result.Protocol,
                ReasoningSummary = result.ReasoningSummary,
                Verbosity = result.Verbosity,
                CollaborationMode = ApplyModelRouteCollaborationMode(context.CollaborationMode, result),
                ModelRouteSetId = result.RouteSetId,
                ModelRouteKind = routeKind.Value,
                ModelRouteDiagnosticsCorrelationId = result.DiagnosticsCorrelationId,
            },
            result);
    }

    private static KernelCollaborationModeState ApplyModelRouteCollaborationMode(
        KernelCollaborationModeState? collaborationMode,
        ModelRouteResolutionResult result)
    {
        var current = KernelCollaborationModeState.NormalizeOrDefault(
            collaborationMode,
            result.Model,
            result.ReasoningEffort);
        return current with
        {
            Settings = new KernelCollaborationModeSettings(
                result.Model,
                result.ReasoningEffort ?? current.Settings.ReasoningEffort,
                current.Settings.DeveloperInstructions),
        };
    }
}

internal sealed record AppHostCoreLoopModelRouteResolution(
    TurnRequestContext Context,
    ModelRouteResolutionResult Result);
