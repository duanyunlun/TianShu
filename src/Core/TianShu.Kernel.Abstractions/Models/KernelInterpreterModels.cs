using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Stage 执行结果状态；默认 Unspecified 不能驱动下一跳。
/// Stage-result status; default Unspecified must not drive transitions.
/// </summary>
public enum StageResultStatus
{
    Unspecified = 0,
    Succeeded = 1,
    Failed = 2,
    Skipped = 3,
    Blocked = 4,
}

/// <summary>
/// Stage 结果，供 StageGraphInterpreter 判断下一跳。
/// Stage result used by StageGraphInterpreter to decide the next transition.
/// </summary>
public sealed record StageResult
{
    public StageResult(
        StageId stageId,
        StageResultStatus status,
        RuntimeStepResultStatus runtimeStatus = RuntimeStepResultStatus.Unspecified,
        StructuredValue? output = null,
        string? diagnosticsRef = null,
        string? traceRef = null,
        MetadataBag? metadata = null)
    {
        StageId = stageId;
        Status = status;
        RuntimeStatus = runtimeStatus;
        Output = output ?? StructuredValue.Null;
        DiagnosticsRef = diagnosticsRef;
        TraceRef = traceRef;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public StageId StageId { get; }

    public StageResultStatus Status { get; }

    public RuntimeStepResultStatus RuntimeStatus { get; }

    public StructuredValue Output { get; }

    public string? DiagnosticsRef { get; }

    public string? TraceRef { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Stage 下一跳决策，由解释器生成但仍受 Stable Kernel Core 校验。
/// Stage transition decision produced by the interpreter and still governed by Stable Kernel Core validation.
/// </summary>
public sealed record StageTransitionDecision
{
    public StageTransitionDecision(
        StageId currentStageId,
        StageTransitionKind transitionKind,
        StageId? nextStageId = null,
        bool shouldStop = false,
        KernelValidationDecision validationDecision = KernelValidationDecision.Unspecified,
        string? reason = null,
        MetadataBag? metadata = null)
    {
        CurrentStageId = currentStageId;
        TransitionKind = transitionKind;
        NextStageId = nextStageId;
        ShouldStop = shouldStop;
        ValidationDecision = validationDecision;
        Reason = reason;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public StageId CurrentStageId { get; }

    public StageTransitionKind TransitionKind { get; }

    public StageId? NextStageId { get; }

    public bool ShouldStop { get; }

    public KernelValidationDecision ValidationDecision { get; }

    public string? Reason { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// StageGraph 解释上下文，提供 intent、run state、options 和治理边界。
/// StageGraph interpreter context carrying intent, run state, options, and governance boundary.
/// </summary>
public sealed record KernelInterpreterContext
{
    public KernelInterpreterContext(
        CoreIntent intent,
        KernelRunState state,
        KernelRunOptions options,
        GovernanceEnvelope? governance = null,
        MetadataBag? metadata = null)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        State = state ?? throw new ArgumentNullException(nameof(state));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Governance = governance ?? intent.Governance;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public CoreIntent Intent { get; }

    public KernelRunState State { get; }

    public KernelRunOptions Options { get; }

    public GovernanceEnvelope Governance { get; }

    public MetadataBag Metadata { get; }
}
