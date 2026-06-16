namespace TianShu.RuntimeComposition;

/// <summary>
/// Kernel turn 产品路径类型。
/// Product execution path kind for a Kernel turn.
/// </summary>
public enum KernelRuntimeTurnPathKind
{
    KernelRuntimeLoop,
    FailClosed,
}

/// <summary>
/// 默认 turn 路径判定使用的产品能力声明。
/// Product capability declaration used by default turn-path decisions.
/// </summary>
public enum KernelRuntimeTurnCapability
{
    BasicTurn,

    /// <summary>
    /// 需要完整产品投影 parity 的能力声明；当前由 23.4.1+ 的 Host Gateway / typed decision request 按 turn 特征填充。
    /// Capability marker for full product-projection parity; filled by the 23.4.1+ Host Gateway / typed decision request from turn features.
    /// </summary>
    FullProductProjection,

    Steer,
    Interrupt,
    Resume,
    HighRiskTool,
    SubagentJob,
}

/// <summary>
/// Host/CLI 提交给 RuntimeComposition 的路径判定请求；旧 AppHost turn loop 已移除为可选产品路径。
/// Path decision request submitted by Host/CLI to RuntimeComposition; the legacy AppHost turn loop is no longer a selectable product path.
/// </summary>
public sealed record KernelRuntimeTurnPathRequest(
    bool ExplicitKernelRuntimeLoopRequested,
    bool DefaultKernelRuntimeLoopEnabled,
    IReadOnlyList<KernelRuntimeTurnCapability> RequiredCapabilities,
    bool ExplicitAppHostControlPlaneRequested = false)
{
    public static KernelRuntimeTurnPathRequest ForCliSend(
        bool explicitKernelRuntimeLoopRequested,
        bool defaultKernelRuntimeLoopEnabled = false,
        bool explicitAppHostControlPlaneRequested = false)
        => new(
            explicitKernelRuntimeLoopRequested,
            defaultKernelRuntimeLoopEnabled,
            [KernelRuntimeTurnCapability.BasicTurn],
            explicitAppHostControlPlaneRequested);
}

/// <summary>
/// RuntimeComposition 产出的默认 turn 路径判定。
/// Default turn-path decision produced by RuntimeComposition.
/// </summary>
public sealed record KernelRuntimeTurnPathDecision(
    KernelRuntimeTurnPathKind PathKind,
    string ExecutionPath,
    bool UseKernelRuntimeLoop,
    bool UseAppHostControlPlane,
    string? FallbackReason,
    string? FailureCode,
    IReadOnlyList<KernelRuntimeTurnCapability> RequiredCapabilities,
    IReadOnlyList<KernelRuntimeTurnCapability> LegacyFallbackCapabilities);

/// <summary>
/// 默认 turn 路径判定策略；新 Kernel→Runtime loop 是唯一 turn 路径，不支持的能力 fail-closed。
/// Default turn-path decision policy; the new Kernel→Runtime loop is the only turn path, and unsupported capabilities fail closed.
/// </summary>
public static class KernelRuntimeTurnPathSelector
{
    private static readonly KernelRuntimeTurnCapability[] KernelRuntimeSupportedCapabilities =
    [
        KernelRuntimeTurnCapability.BasicTurn,
    ];

    private static readonly KernelRuntimeTurnCapability[] LegacyFallbackAllowedCapabilities = [];

    public static KernelRuntimeTurnPathDecision Decide(KernelRuntimeTurnPathRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requiredCapabilities = NormalizeRequiredCapabilities(request.RequiredCapabilities);
        if (request.ExplicitKernelRuntimeLoopRequested && request.ExplicitAppHostControlPlaneRequested)
        {
            return FailClosed(requiredCapabilities, "kernel_runtime_legacy_apphost_removed");
        }

        if (request.ExplicitAppHostControlPlaneRequested)
        {
            return FailClosed(requiredCapabilities, "kernel_runtime_legacy_apphost_removed");
        }

        if (request.ExplicitKernelRuntimeLoopRequested)
        {
            return NewLoopDecision(requiredCapabilities);
        }

        var unsupportedByKernel = requiredCapabilities
            .Where(static capability => !KernelRuntimeSupportedCapabilities.Contains(capability))
            .ToArray();
        if (request.DefaultKernelRuntimeLoopEnabled && unsupportedByKernel.Length == 0)
        {
            return NewLoopDecision(requiredCapabilities);
        }

        return FailClosed(
            requiredCapabilities,
            request.DefaultKernelRuntimeLoopEnabled
                ? "kernel_runtime_capability_unsupported"
                : "kernel_runtime_default_disabled");
    }

    private static KernelRuntimeTurnPathDecision NewLoopDecision(IReadOnlyList<KernelRuntimeTurnCapability> requiredCapabilities)
        => new(
            KernelRuntimeTurnPathKind.KernelRuntimeLoop,
            "kernel-runtime-loop",
            UseKernelRuntimeLoop: true,
            UseAppHostControlPlane: false,
            FallbackReason: null,
            FailureCode: null,
            requiredCapabilities,
            LegacyFallbackAllowedCapabilities);

    private static KernelRuntimeTurnPathDecision FailClosed(
        IReadOnlyList<KernelRuntimeTurnCapability> requiredCapabilities,
        string failureCode)
        => new(
            KernelRuntimeTurnPathKind.FailClosed,
            "fail-closed",
            UseKernelRuntimeLoop: false,
            UseAppHostControlPlane: false,
            FallbackReason: null,
            FailureCode: failureCode,
            requiredCapabilities,
            LegacyFallbackAllowedCapabilities);

    private static IReadOnlyList<KernelRuntimeTurnCapability> NormalizeRequiredCapabilities(
        IReadOnlyList<KernelRuntimeTurnCapability>? requiredCapabilities)
    {
        if (requiredCapabilities is null || requiredCapabilities.Count == 0)
        {
            return [KernelRuntimeTurnCapability.BasicTurn];
        }

        return requiredCapabilities
            .Distinct()
            .ToArray();
    }
}
