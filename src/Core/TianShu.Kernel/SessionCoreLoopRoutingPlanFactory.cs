using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel;

/// <summary>
/// Core Loop routing plan 工厂，集中完成运行时配置、观察态与模型路由委托进入 Kernel 入口计划的规则。
/// Core-loop routing-plan factory that centralizes runtime config, observed state, and model-route delegation into a Kernel entry plan.
/// </summary>
public static class SessionCoreLoopRoutingPlanFactory
{
    /// <summary>
    /// 创建 Core Loop 入口计划。
    /// Creates the core-loop entry plan.
    /// </summary>
    public static SessionCoreLoopEntryPlan Plan(SessionCoreLoopRoutingPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var registryContext = StageRegistryPlanningContextFactory.CreateContext(request.RawConfig);
        var observedState = SessionObservedStateFactory.Create(
            workspaceCwd: request.WorkspaceCwd,
            workspaceSandboxMode: request.WorkspaceSandboxMode,
            workspaceWebSearchMode: request.WorkspaceWebSearchMode,
            workspaceWindowsSandboxLevel: request.WorkspaceWindowsSandboxLevel,
            allowLoginShell: request.AllowLoginShell,
            artifactRefs: request.ArtifactRefs,
            stageRegistryIssues: registryContext.Issues,
            memoryMode: request.MemoryMode,
            approvalPolicy: request.ApprovalPolicy,
            policySandboxMode: request.PolicySandboxMode,
            policyWebSearchMode: request.PolicyWebSearchMode,
            defaultModeRequestUserInputEnabled: request.DefaultModeRequestUserInputEnabled);
        var input = new SessionOrchestrationInput(
            request.Input.SessionId,
            request.Input.ThreadId,
            request.Input.CorrelationId,
            request.Input.PreviousStageId,
            request.Input.RequestedStageId,
            request.Input.Checkpoints,
            request.Input.ContextLedgerSegments,
            request.Input.ContextBudgetTokens,
            observedState);

        return registryContext.EntryPlanner.PlanEntry(
            input,
            request.ResolveModelRoute,
            request.StartedAt);
    }
}

/// <summary>
/// Core Loop routing plan 请求，封装 Kernel 规划所需的配置、输入、观察态和路由委托。
/// Core-loop routing-plan request wrapping config, input, observed state, and route delegate required by Kernel planning.
/// </summary>
public sealed record SessionCoreLoopRoutingPlanRequest
{
    /// <summary>
    /// 初始化 Core Loop routing plan 请求。
    /// Initializes the core-loop routing-plan request.
    /// </summary>
    public SessionCoreLoopRoutingPlanRequest(
        Dictionary<string, object?> rawConfig,
        SessionOrchestrationInput input,
        Func<SessionCoreLoopRouteRequest, SessionCoreLoopRouteResult> resolveModelRoute,
        string? workspaceCwd,
        string? workspaceSandboxMode,
        string? workspaceWebSearchMode,
        string? workspaceWindowsSandboxLevel,
        bool allowLoginShell,
        IReadOnlyList<ArtifactRef>? artifactRefs,
        string? memoryMode,
        string? approvalPolicy,
        string? policySandboxMode,
        string? policyWebSearchMode,
        bool defaultModeRequestUserInputEnabled,
        DateTimeOffset? startedAt = null)
    {
        RawConfig = rawConfig ?? throw new ArgumentNullException(nameof(rawConfig));
        Input = input ?? throw new ArgumentNullException(nameof(input));
        ResolveModelRoute = resolveModelRoute ?? throw new ArgumentNullException(nameof(resolveModelRoute));
        WorkspaceCwd = workspaceCwd;
        WorkspaceSandboxMode = workspaceSandboxMode;
        WorkspaceWebSearchMode = workspaceWebSearchMode;
        WorkspaceWindowsSandboxLevel = workspaceWindowsSandboxLevel;
        AllowLoginShell = allowLoginShell;
        ArtifactRefs = artifactRefs ?? Array.Empty<ArtifactRef>();
        MemoryMode = memoryMode;
        ApprovalPolicy = approvalPolicy;
        PolicySandboxMode = policySandboxMode;
        PolicyWebSearchMode = policyWebSearchMode;
        DefaultModeRequestUserInputEnabled = defaultModeRequestUserInputEnabled;
        StartedAt = startedAt;
    }

    public Dictionary<string, object?> RawConfig { get; }

    public SessionOrchestrationInput Input { get; }

    public Func<SessionCoreLoopRouteRequest, SessionCoreLoopRouteResult> ResolveModelRoute { get; }

    public string? WorkspaceCwd { get; }

    public string? WorkspaceSandboxMode { get; }

    public string? WorkspaceWebSearchMode { get; }

    public string? WorkspaceWindowsSandboxLevel { get; }

    public bool AllowLoginShell { get; }

    public IReadOnlyList<ArtifactRef> ArtifactRefs { get; }

    public string? MemoryMode { get; }

    public string? ApprovalPolicy { get; }

    public string? PolicySandboxMode { get; }

    public string? PolicyWebSearchMode { get; }

    public bool DefaultModeRequestUserInputEnabled { get; }

    public DateTimeOffset? StartedAt { get; }
}
