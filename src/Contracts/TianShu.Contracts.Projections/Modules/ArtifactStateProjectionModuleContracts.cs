using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Artifacts;

/// <summary>
/// Artifact / State / Projection Module 统一入口，供 Execution Runtime 通过 ArtifactStep 或 ModuleCapabilityStep 调用。
/// Unified Artifact / State / Projection Module entry point invoked by Execution Runtime through ArtifactStep or ModuleCapabilityStep.
/// </summary>
public interface IArtifactStateProjectionModule : IModuleHealthCheck
{
    ValueTask<ArtifactModuleMutationResult> ExecuteArtifactAsync(
        ArtifactModuleMutationInvocation invocation,
        CancellationToken cancellationToken);

    ValueTask<ArtifactProjectionModuleQueryResult> QueryProjectionAsync(
        ArtifactProjectionModuleQueryInvocation invocation,
        CancellationToken cancellationToken);

    ValueTask<ArtifactCheckpointMaterializationResult> MaterializeCheckpointAsync(
        ArtifactCheckpointMaterializationInvocation invocation,
        CancellationToken cancellationToken);
}

/// <summary>
/// Artifact / State / Projection Module 调用上下文，承载 RuntimeStep 来源和 Kernel run 关联。
/// Invocation context carrying RuntimeStep source and Kernel-run association.
/// </summary>
public sealed record ArtifactModuleInvocationContext
{
    public ArtifactModuleInvocationContext(
        string runtimeStepId,
        CoreIntentId sourceIntentId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        KernelOperationId sourceKernelOperationId,
        KernelRunId kernelRunId,
        ExecutionId executionId,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        MetadataBag? metadata = null)
    {
        RuntimeStepId = IdentifierGuard.AgainstNullOrWhiteSpace(runtimeStepId, nameof(runtimeStepId));
        SourceIntentId = sourceIntentId;
        SourceGraphId = sourceGraphId;
        SourceStageId = sourceStageId;
        SourceKernelOperationId = sourceKernelOperationId;
        KernelRunId = kernelRunId;
        ExecutionId = executionId;
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        SideEffect = sideEffect ?? throw new ArgumentNullException(nameof(sideEffect));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string RuntimeStepId { get; }

    public CoreIntentId SourceIntentId { get; }

    public StageGraphId SourceGraphId { get; }

    public StageId SourceStageId { get; }

    public KernelOperationId SourceKernelOperationId { get; }

    public KernelRunId KernelRunId { get; }

    public ExecutionId ExecutionId { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffect { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Artifact mutation 调用，必须保留原始 ArtifactStep。
/// Artifact mutation invocation that preserves the original ArtifactStep.
/// </summary>
public sealed record ArtifactModuleMutationInvocation(
    ArtifactStep Step,
    ArtifactModuleMutation Mutation,
    ArtifactModuleInvocationContext Context)
{
    public ArtifactStep Step { get; } = Step ?? throw new ArgumentNullException(nameof(Step));

    public ArtifactModuleMutation Mutation { get; } = Mutation ?? throw new ArgumentNullException(nameof(Mutation));

    public ArtifactModuleInvocationContext Context { get; } = Context ?? throw new ArgumentNullException(nameof(Context));
}

public abstract record ArtifactModuleMutation(string OperationName);

public sealed record PublishArtifactModuleMutation(PublishArtifact Command)
    : ArtifactModuleMutation("publish")
{
    public PublishArtifact Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

public sealed record PromoteArtifactModuleMutation(PromoteArtifact Command)
    : ArtifactModuleMutation("promote")
{
    public PromoteArtifact Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

public sealed record AttachArtifactToTaskModuleMutation(AttachArtifactToTask Command)
    : ArtifactModuleMutation("attach")
{
    public AttachArtifactToTask Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

/// <summary>
/// Artifact module record view，避免向上层暴露 ArtifactStoreRecord 私有存储结构。
/// Artifact module record view that avoids exposing ArtifactStoreRecord private storage shape upward.
/// </summary>
public sealed record ArtifactModuleRecord
{
    public ArtifactModuleRecord(
        Artifact artifact,
        IReadOnlyList<string>? promotionChannels = null,
        IReadOnlyList<TaskId>? attachedTaskIds = null,
        long version = 1,
        DateTimeOffset? updatedAt = null)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "版本号必须大于零。");
        }

        Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        PromotionChannels = promotionChannels ?? Array.Empty<string>();
        AttachedTaskIds = attachedTaskIds ?? Array.Empty<TaskId>();
        Version = version;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public Artifact Artifact { get; }

    public IReadOnlyList<string> PromotionChannels { get; }

    public IReadOnlyList<TaskId> AttachedTaskIds { get; }

    public long Version { get; }

    public DateTimeOffset UpdatedAt { get; }
}

public sealed record ArtifactModuleMutationResult
{
    public ArtifactModuleMutationResult(
        bool success,
        string operationName,
        ArtifactModuleRecord? record = null,
        string? degradedReason = null)
    {
        Success = success;
        OperationName = IdentifierGuard.AgainstNullOrWhiteSpace(operationName, nameof(operationName));
        Record = record;
        DegradedReason = degradedReason;
    }

    public bool Success { get; }

    public string OperationName { get; }

    public ArtifactModuleRecord? Record { get; }

    public string? DegradedReason { get; }
}

public sealed record ArtifactProjectionModuleQueryInvocation(
    ArtifactProjectionModuleQuery Query,
    ArtifactModuleInvocationContext Context)
{
    public ArtifactProjectionModuleQuery Query { get; } = Query ?? throw new ArgumentNullException(nameof(Query));

    public ArtifactModuleInvocationContext Context { get; } = Context ?? throw new ArgumentNullException(nameof(Context));
}

public abstract record ArtifactProjectionModuleQuery;

public sealed record ReadProjectionSnapshotModuleQuery(ProjectionScopeKind ScopeKind, string ScopeKey)
    : ArtifactProjectionModuleQuery
{
    public string ScopeKey { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(ScopeKey, nameof(ScopeKey));
}

/// <summary>
/// 投影视图快照的只读契约视图，不暴露 ProjectionStores 的存储记录。
/// Read-only contract view of a projection snapshot without exposing ProjectionStores storage records.
/// </summary>
public sealed record ProjectionSnapshotView
{
    public ProjectionSnapshotView(
        ProjectionScopeKind scopeKind,
        string scopeKey,
        ProjectionDelta? delta = null,
        ProjectionReset? reset = null,
        long version = 1,
        DateTimeOffset? updatedAt = null)
    {
        if ((delta is null) == (reset is null))
        {
            throw new ArgumentException("Projection snapshot view must contain exactly one of delta or reset.");
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "版本号必须大于零。");
        }

        ScopeKind = scopeKind;
        ScopeKey = IdentifierGuard.AgainstNullOrWhiteSpace(scopeKey, nameof(scopeKey));
        Delta = delta;
        Reset = reset;
        Version = version;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public ProjectionScopeKind ScopeKind { get; }

    public string ScopeKey { get; }

    public ProjectionDelta? Delta { get; }

    public ProjectionReset? Reset { get; }

    public long Version { get; }

    public DateTimeOffset UpdatedAt { get; }
}

public sealed record ArtifactProjectionModuleQueryResult(
    ProjectionSnapshotView? Snapshot = null,
    IReadOnlyList<string>? DegradedSources = null)
{
    public IReadOnlyList<string> DegradedSources { get; } = DegradedSources ?? Array.Empty<string>();
}

public sealed record ArtifactCheckpointMaterializationInvocation(
    ArtifactCheckpointMaterializationRequest Request,
    ArtifactModuleInvocationContext Context)
{
    public ArtifactCheckpointMaterializationRequest Request { get; } = Request ?? throw new ArgumentNullException(nameof(Request));

    public ArtifactModuleInvocationContext Context { get; } = Context ?? throw new ArgumentNullException(nameof(Context));
}

/// <summary>
/// Checkpoint materialization request，必须同时关联 Kernel run、StageGraph、Stage 和 Execution。
/// Checkpoint materialization request that must associate Kernel run, StageGraph, Stage, and Execution.
/// </summary>
public sealed record ArtifactCheckpointMaterializationRequest
{
    public ArtifactCheckpointMaterializationRequest(
        KernelRunId kernelRunId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        ExecutionId executionId,
        ExecutionTraceId traceId,
        RecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!string.Equals(checkpoint.ExecutionId.Value, executionId.Value, StringComparison.Ordinal))
        {
            throw new ArgumentException("Checkpoint execution id must match the materialization execution id.", nameof(checkpoint));
        }

        KernelRunId = kernelRunId;
        SourceGraphId = sourceGraphId;
        SourceStageId = sourceStageId;
        ExecutionId = executionId;
        TraceId = traceId;
        Checkpoint = checkpoint;
    }

    public KernelRunId KernelRunId { get; }

    public StageGraphId SourceGraphId { get; }

    public StageId SourceStageId { get; }

    public ExecutionId ExecutionId { get; }

    public ExecutionTraceId TraceId { get; }

    public RecoveryCheckpoint Checkpoint { get; }
}

public sealed record ArtifactCheckpointMaterializationResult
{
    public ArtifactCheckpointMaterializationResult(
        bool success,
        KernelRunId kernelRunId,
        StageGraphId sourceGraphId,
        StageId sourceStageId,
        ExecutionId executionId,
        string? checkpointRef = null,
        ExecutionTrace? trace = null,
        string? degradedReason = null)
    {
        Success = success;
        KernelRunId = kernelRunId;
        SourceGraphId = sourceGraphId;
        SourceStageId = sourceStageId;
        ExecutionId = executionId;
        CheckpointRef = checkpointRef;
        Trace = trace;
        DegradedReason = degradedReason;
    }

    public bool Success { get; }

    public KernelRunId KernelRunId { get; }

    public StageGraphId SourceGraphId { get; }

    public StageId SourceStageId { get; }

    public ExecutionId ExecutionId { get; }

    public string? CheckpointRef { get; }

    public ExecutionTrace? Trace { get; }

    public string? DegradedReason { get; }
}
