using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Tools;

namespace TianShu.Contracts.Execution;

/// <summary>
/// Runtime step 种类。
/// Runtime step kind.
/// </summary>
public enum RuntimeStepKind
{
    Unspecified = 0,
    ModelInvocation = 1,
    ToolInvocation = 2,
    StateCommit = 3,
    Artifact = 4,
    Diagnostic = 5,
    HostInteraction = 6,
    ModuleCapability = 7,
}

/// <summary>
/// Runtime step 结果状态；默认 Unspecified 不代表成功。
/// Runtime step result status; default Unspecified does not mean success.
/// </summary>
public enum RuntimeStepResultStatus
{
    Unspecified = 0,
    Succeeded = 1,
    Failed = 2,
    Blocked = 3,
    Cancelled = 4,
}

/// <summary>
/// Execution plan 策略，表达执行层可采用的顺序和失败处理上限。
/// Execution-plan policy describing ordering and failure-handling ceilings for the runtime.
/// </summary>
public sealed record ExecutionPlanPolicy
{
    public ExecutionPlanPolicy(bool sequential = true, bool stopOnFailure = true, int maxParallelism = 1)
    {
        if (maxParallelism < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxParallelism), "并发度必须至少为 1。");
        }

        Sequential = sequential;
        StopOnFailure = stopOnFailure;
        MaxParallelism = maxParallelism;
    }

    public bool Sequential { get; }

    public bool StopOnFailure { get; }

    public int MaxParallelism { get; }
}

/// <summary>
/// Trace 策略，声明执行层必须写出的追踪和诊断引用。
/// Trace policy declaring trace and diagnostic references the runtime must emit.
/// </summary>
public sealed record TracePolicy
{
    public TracePolicy(
        bool enabled = true,
        bool requireDiagnosticsRef = true,
        bool requireRuntimeTraceRef = true,
        IReadOnlyList<string>? requiredEventKinds = null)
    {
        Enabled = enabled;
        RequireDiagnosticsRef = requireDiagnosticsRef;
        RequireRuntimeTraceRef = requireRuntimeTraceRef;
        RequiredEventKinds = requiredEventKinds ?? Array.Empty<string>();
    }

    public bool Enabled { get; }

    public bool RequireDiagnosticsRef { get; }

    public bool RequireRuntimeTraceRef { get; }

    public IReadOnlyList<string> RequiredEventKinds { get; }
}

/// <summary>
/// 已批准的执行计划，是 Execution Runtime 的批量入口。
/// Approved execution plan, the batch entry point for Execution Runtime.
/// </summary>
public sealed record ExecutionPlan
{
    public ExecutionPlan(
        string planId,
        StageGraphId sourceGraphId,
        CoreIntentId sourceIntentId,
        IReadOnlyList<RuntimeStep> steps,
        ExecutionPlanPolicy policy,
        TracePolicy tracePolicy,
        MetadataBag? metadata = null)
    {
        if (steps is null || steps.Count == 0)
        {
            throw new ArgumentException("ExecutionPlan 至少需要一个 RuntimeStep。", nameof(steps));
        }

        PlanId = IdentifierGuard.AgainstNullOrWhiteSpace(planId, nameof(planId));
        SourceGraphId = Require(sourceGraphId, nameof(sourceGraphId));
        SourceIntentId = Require(sourceIntentId, nameof(sourceIntentId));
        Steps = steps;
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        TracePolicy = tracePolicy ?? throw new ArgumentNullException(nameof(tracePolicy));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string PlanId { get; }

    public StageGraphId SourceGraphId { get; }

    public CoreIntentId SourceIntentId { get; }

    public IReadOnlyList<RuntimeStep> Steps { get; }

    public ExecutionPlanPolicy Policy { get; }

    public TracePolicy TracePolicy { get; }

    public MetadataBag Metadata { get; }

    private static StageGraphId Require(StageGraphId value, string paramName)
        => string.IsNullOrWhiteSpace(value.Value) ? throw new ArgumentException("值不能为空。", paramName) : value;

    private static CoreIntentId Require(CoreIntentId value, string paramName)
        => string.IsNullOrWhiteSpace(value.Value) ? throw new ArgumentException("值不能为空。", paramName) : value;
}

/// <summary>
/// 单个可执行 Runtime step 的基类，强制携带 Kernel 来源、权限、副作用、预算和 trace policy。
/// Base runtime step that requires Kernel source ids, permission, side effect, budget, and trace policy.
/// </summary>
public abstract record RuntimeStep
{
    protected RuntimeStep(
        string stepId,
        RuntimeStepKind stepKind,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        KernelBudget budget,
        ContractRef expectedOutputContract,
        TracePolicy tracePolicy,
        MetadataBag? metadata = null)
    {
        StepId = IdentifierGuard.AgainstNullOrWhiteSpace(stepId, nameof(stepId));
        StepKind = stepKind;
        SourceIntentId = Require(sourceIntentId, nameof(sourceIntentId));
        SourceGraphId = Require(sourceGraphId, nameof(sourceGraphId));
        SourceStageId = Require(sourceStageId, nameof(sourceStageId));
        SourceKernelOperationId = Require(sourceKernelOperationId, nameof(sourceKernelOperationId));
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        SideEffect = sideEffect ?? throw new ArgumentNullException(nameof(sideEffect));
        Budget = budget ?? throw new ArgumentNullException(nameof(budget));
        ExpectedOutputContract = expectedOutputContract ?? throw new ArgumentNullException(nameof(expectedOutputContract));
        TracePolicy = tracePolicy ?? throw new ArgumentNullException(nameof(tracePolicy));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string StepId { get; }

    public RuntimeStepKind StepKind { get; }

    public CoreIntentId SourceIntentId { get; }

    public StageGraphId SourceGraphId { get; }

    public StageId SourceStageId { get; }

    public KernelOperationId SourceKernelOperationId { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffect { get; }

    public KernelBudget Budget { get; }

    public ContractRef ExpectedOutputContract { get; }

    public TracePolicy TracePolicy { get; }

    public MetadataBag Metadata { get; }

    private static CoreIntentId Require(CoreIntentId value, string paramName)
        => string.IsNullOrWhiteSpace(value.Value) ? throw new ArgumentException("值不能为空。", paramName) : value;

    private static StageGraphId Require(StageGraphId value, string paramName)
        => string.IsNullOrWhiteSpace(value.Value) ? throw new ArgumentException("值不能为空。", paramName) : value;

    private static StageId Require(StageId value, string paramName)
        => string.IsNullOrWhiteSpace(value.Value) ? throw new ArgumentException("值不能为空。", paramName) : value;

    private static KernelOperationId Require(KernelOperationId value, string paramName)
        => string.IsNullOrWhiteSpace(value.Value) ? throw new ArgumentException("值不能为空。", paramName) : value;
}

/// <summary>
/// 模型调用 step。
/// Model-invocation step.
/// </summary>
public sealed record ModelInvocationStep : RuntimeStep
{
    public ModelInvocationStep(
        string stepId,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        string providerModuleId,
        ModelRoutePolicy modelRoute,
        ProviderInvocationRequest inputEnvelope,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        KernelBudget budget,
        ContractRef expectedOutputContract,
        TracePolicy tracePolicy,
        MetadataBag? metadata = null)
        : base(stepId, RuntimeStepKind.ModelInvocation, sourceIntentId, sourceGraphId, sourceStageId, sourceKernelOperationId, permission, sideEffect, budget, expectedOutputContract, tracePolicy, metadata)
    {
        ProviderModuleId = IdentifierGuard.AgainstNullOrWhiteSpace(providerModuleId, nameof(providerModuleId));
        ModelRoute = modelRoute ?? throw new ArgumentNullException(nameof(modelRoute));
        InputEnvelope = inputEnvelope ?? throw new ArgumentNullException(nameof(inputEnvelope));
    }

    public string ProviderModuleId { get; }

    public ModelRoutePolicy ModelRoute { get; }

    public ProviderInvocationRequest InputEnvelope { get; }
}

/// <summary>
/// 工具调用 step。
/// Tool-invocation step.
/// </summary>
public sealed record ToolInvocationStep : RuntimeStep
{
    public ToolInvocationStep(
        string stepId,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        string capabilityToolId,
        ToolInvocationEnvelope inputEnvelope,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        KernelBudget budget,
        ContractRef expectedOutputContract,
        TracePolicy tracePolicy,
        MetadataBag? metadata = null)
        : base(stepId, RuntimeStepKind.ToolInvocation, sourceIntentId, sourceGraphId, sourceStageId, sourceKernelOperationId, permission, sideEffect, budget, expectedOutputContract, tracePolicy, metadata)
    {
        CapabilityToolId = IdentifierGuard.AgainstNullOrWhiteSpace(capabilityToolId, nameof(capabilityToolId));
        InputEnvelope = inputEnvelope ?? throw new ArgumentNullException(nameof(inputEnvelope));
    }

    public string CapabilityToolId { get; }

    public ToolInvocationEnvelope InputEnvelope { get; }
}

/// <summary>
/// 状态提交 step。
/// State-commit step.
/// </summary>
public sealed record StateCommitStep : RuntimeStep
{
    public StateCommitStep(
        string stepId,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        string stateStoreId,
        StructuredValue commitEnvelope,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        KernelBudget budget,
        ContractRef expectedOutputContract,
        TracePolicy tracePolicy,
        MetadataBag? metadata = null)
        : base(stepId, RuntimeStepKind.StateCommit, sourceIntentId, sourceGraphId, sourceStageId, sourceKernelOperationId, permission, sideEffect, budget, expectedOutputContract, tracePolicy, metadata)
    {
        StateStoreId = IdentifierGuard.AgainstNullOrWhiteSpace(stateStoreId, nameof(stateStoreId));
        CommitEnvelope = commitEnvelope ?? throw new ArgumentNullException(nameof(commitEnvelope));
    }

    public string StateStoreId { get; }

    public StructuredValue CommitEnvelope { get; }
}

/// <summary>
/// 工件 step。
/// Artifact step.
/// </summary>
public sealed record ArtifactStep : RuntimeStep
{
    public ArtifactStep(
        string stepId,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        string artifactOperation,
        StructuredValue artifactEnvelope,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        KernelBudget budget,
        ContractRef expectedOutputContract,
        TracePolicy tracePolicy,
        MetadataBag? metadata = null)
        : base(stepId, RuntimeStepKind.Artifact, sourceIntentId, sourceGraphId, sourceStageId, sourceKernelOperationId, permission, sideEffect, budget, expectedOutputContract, tracePolicy, metadata)
    {
        ArtifactOperation = IdentifierGuard.AgainstNullOrWhiteSpace(artifactOperation, nameof(artifactOperation));
        ArtifactEnvelope = artifactEnvelope ?? throw new ArgumentNullException(nameof(artifactEnvelope));
    }

    public string ArtifactOperation { get; }

    public StructuredValue ArtifactEnvelope { get; }
}

/// <summary>
/// 诊断 step。
/// Diagnostic step.
/// </summary>
public sealed record DiagnosticStep : RuntimeStep
{
    public DiagnosticStep(
        string stepId,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        string diagnosticKind,
        StructuredValue diagnosticEnvelope,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        KernelBudget budget,
        ContractRef expectedOutputContract,
        TracePolicy tracePolicy,
        MetadataBag? metadata = null)
        : base(stepId, RuntimeStepKind.Diagnostic, sourceIntentId, sourceGraphId, sourceStageId, sourceKernelOperationId, permission, sideEffect, budget, expectedOutputContract, tracePolicy, metadata)
    {
        DiagnosticKind = IdentifierGuard.AgainstNullOrWhiteSpace(diagnosticKind, nameof(diagnosticKind));
        DiagnosticEnvelope = diagnosticEnvelope ?? throw new ArgumentNullException(nameof(diagnosticEnvelope));
    }

    public string DiagnosticKind { get; }

    public StructuredValue DiagnosticEnvelope { get; }
}

/// <summary>
/// 宿主交互 step。
/// Host-interaction step.
/// </summary>
public sealed record HostInteractionStep : RuntimeStep
{
    public HostInteractionStep(
        string stepId,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        string interactionKind,
        StructuredValue interactionEnvelope,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        KernelBudget budget,
        ContractRef expectedOutputContract,
        TracePolicy tracePolicy,
        MetadataBag? metadata = null)
        : base(stepId, RuntimeStepKind.HostInteraction, sourceIntentId, sourceGraphId, sourceStageId, sourceKernelOperationId, permission, sideEffect, budget, expectedOutputContract, tracePolicy, metadata)
    {
        InteractionKind = IdentifierGuard.AgainstNullOrWhiteSpace(interactionKind, nameof(interactionKind));
        InteractionEnvelope = interactionEnvelope ?? throw new ArgumentNullException(nameof(interactionEnvelope));
    }

    public string InteractionKind { get; }

    public StructuredValue InteractionEnvelope { get; }
}

/// <summary>
/// 模块能力调用 step。
/// Module-capability step.
/// </summary>
public sealed record ModuleCapabilityStep : RuntimeStep
{
    public ModuleCapabilityStep(
        string stepId,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        string moduleId,
        string capabilityId,
        StructuredValue inputEnvelope,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        KernelBudget budget,
        ContractRef expectedOutputContract,
        TracePolicy tracePolicy,
        MetadataBag? metadata = null)
        : base(stepId, RuntimeStepKind.ModuleCapability, sourceIntentId, sourceGraphId, sourceStageId, sourceKernelOperationId, permission, sideEffect, budget, expectedOutputContract, tracePolicy, metadata)
    {
        ModuleId = IdentifierGuard.AgainstNullOrWhiteSpace(moduleId, nameof(moduleId));
        CapabilityId = IdentifierGuard.AgainstNullOrWhiteSpace(capabilityId, nameof(capabilityId));
        InputEnvelope = inputEnvelope ?? throw new ArgumentNullException(nameof(inputEnvelope));
    }

    public string ModuleId { get; }

    public string CapabilityId { get; }

    public StructuredValue InputEnvelope { get; }
}

/// <summary>
/// Runtime step 执行结果。
/// Runtime step execution result.
/// </summary>
public sealed record RuntimeStepResult
{
    public RuntimeStepResult(
        string stepId,
        RuntimeStepKind stepKind,
        RuntimeStepResultStatus status,
        StructuredValue? output = null,
        ExecutionFailure? failure = null,
        string? diagnosticsRef = null,
        string? traceRef = null)
    {
        StepId = IdentifierGuard.AgainstNullOrWhiteSpace(stepId, nameof(stepId));
        StepKind = stepKind;
        Status = status;
        Output = output;
        Failure = failure;
        DiagnosticsRef = diagnosticsRef;
        TraceRef = traceRef;
    }

    public string StepId { get; }

    public RuntimeStepKind StepKind { get; }

    public RuntimeStepResultStatus Status { get; }

    public StructuredValue? Output { get; }

    public ExecutionFailure? Failure { get; }

    public string? DiagnosticsRef { get; }

    public string? TraceRef { get; }
}

/// <summary>
/// ExecutionPlan 执行结果。
/// ExecutionPlan execution result.
/// </summary>
public sealed record ExecutionRunResult
{
    public ExecutionRunResult(
        string planId,
        ExecutionId executionId,
        RuntimeStepResultStatus status,
        IReadOnlyList<RuntimeStepResult>? stepResults = null,
        string? diagnosticsRef = null,
        string? traceRef = null)
    {
        PlanId = IdentifierGuard.AgainstNullOrWhiteSpace(planId, nameof(planId));
        ExecutionId = executionId;
        Status = status;
        StepResults = stepResults ?? Array.Empty<RuntimeStepResult>();
        DiagnosticsRef = diagnosticsRef;
        TraceRef = traceRef;
    }

    public string PlanId { get; }

    public ExecutionId ExecutionId { get; }

    public RuntimeStepResultStatus Status { get; }

    public IReadOnlyList<RuntimeStepResult> StepResults { get; }

    public string? DiagnosticsRef { get; }

    public string? TraceRef { get; }
}

/// <summary>
/// Execution Runtime 上下文。
/// Execution Runtime context.
/// </summary>
public sealed record ExecutionRuntimeContext
{
    public ExecutionRuntimeContext(
        ExecutionId executionId,
        KernelRunId kernelRunId,
        GovernanceEnvelope governance,
        string? workingDirectory = null,
        MetadataBag? metadata = null)
    {
        ExecutionId = executionId;
        KernelRunId = kernelRunId;
        Governance = governance ?? throw new ArgumentNullException(nameof(governance));
        WorkingDirectory = workingDirectory;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public ExecutionId ExecutionId { get; }

    public KernelRunId KernelRunId { get; }

    public GovernanceEnvelope Governance { get; }

    public string? WorkingDirectory { get; }

    public MetadataBag Metadata { get; }
}
