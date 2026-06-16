using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Kernel 验证决策；默认 Unspecified 不能被视为批准。
/// Kernel validation decision; default Unspecified must not be treated as approval.
/// </summary>
public enum KernelValidationDecision
{
    Unspecified = 0,
    Approved = 1,
    Rejected = 2,
    RequiresHumanGate = 3,
    NeedsRevision = 4,
}

/// <summary>
/// Kernel 验证问题严重级别。
/// Severity level for Kernel validation issues.
/// </summary>
public enum KernelValidationIssueSeverity
{
    Unspecified = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// Kernel 验证上下文，提供当前 intent、run state、graph 和策略边界。
/// Kernel validation context carrying the current intent, run state, graph, and policy boundary.
/// </summary>
public sealed record KernelValidationContext
{
    public KernelValidationContext(
        CoreIntent intent,
        KernelRunState? state = null,
        StageGraph? graph = null,
        StageNode? stage = null,
        GraphPolicySet? policySet = null,
        MetadataBag? metadata = null)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        State = state;
        Graph = graph;
        Stage = stage;
        PolicySet = policySet;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public CoreIntent Intent { get; }

    public KernelRunState? State { get; }

    public StageGraph? Graph { get; }

    public StageNode? Stage { get; }

    public GraphPolicySet? PolicySet { get; }

    public GovernanceEnvelope Governance => Intent.Governance;

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Kernel 验证问题，供拒绝、修正和人工 gate 解释原因。
/// Kernel validation issue used to explain rejection, revision, and human-gate decisions.
/// </summary>
public sealed record KernelValidationIssue
{
    public KernelValidationIssue(
        string code,
        string message,
        KernelValidationIssueSeverity severity = KernelValidationIssueSeverity.Error,
        string? sourceRef = null,
        MetadataBag? metadata = null)
    {
        Code = AbstractionGuard.RequiredText(code, nameof(code));
        Message = AbstractionGuard.RequiredText(message, nameof(message));
        Severity = severity;
        SourceRef = sourceRef;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string Code { get; }

    public string Message { get; }

    public KernelValidationIssueSeverity Severity { get; }

    public string? SourceRef { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Kernel 验证结果，是 Stable Kernel Core 批准或拒绝的统一返回值。
/// Kernel validation result, the unified approval or rejection value returned by Stable Kernel Core.
/// </summary>
public sealed record KernelValidationResult
{
    public KernelValidationResult(
        KernelValidationDecision decision,
        IReadOnlyList<KernelValidationIssue>? issues = null,
        string? rationaleRef = null,
        MetadataBag? metadata = null)
    {
        Decision = decision;
        Issues = issues ?? Array.Empty<KernelValidationIssue>();
        RationaleRef = rationaleRef;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public KernelValidationDecision Decision { get; }

    public IReadOnlyList<KernelValidationIssue> Issues { get; }

    public string? RationaleRef { get; }

    public MetadataBag Metadata { get; }

    public bool IsApproved => Decision == KernelValidationDecision.Approved;
}
