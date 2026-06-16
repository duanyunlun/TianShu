using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Strategies;

/// <summary>
/// Kernel trace replay 兼容性检查器。
/// Kernel trace replay compatibility checker.
/// </summary>
public sealed class ReplayCompatibilityChecker
{
    private static readonly KernelTraceEventKind[] RequiredEvents =
    [
        KernelTraceEventKind.IntentAccepted,
        KernelTraceEventKind.GraphValidated,
        KernelTraceEventKind.ExecutionPlanCreated,
    ];

    public ReplayCompatibilityResult Check(KernelRunTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var missing = RequiredEvents
            .Where(required => trace.Events.All(item => item.Kind != required))
            .Select(static item => item.ToString())
            .ToArray();
        var rejected = trace.Events.Any(static item => item.Kind == KernelTraceEventKind.Rejected);

        return new ReplayCompatibilityResult(
            Compatible: missing.Length == 0 && !rejected,
            MissingEventKinds: missing,
            HasPolicyRejection: rejected);
    }
}

/// <summary>
/// Replay compatibility 检查结果。
/// Replay compatibility check result.
/// </summary>
public sealed record ReplayCompatibilityResult(
    bool Compatible,
    IReadOnlyList<string> MissingEventKinds,
    bool HasPolicyRejection);
