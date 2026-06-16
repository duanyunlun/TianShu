using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Projections;
using TianShu.ProjectionStores;

namespace TianShu.ArtifactStore;

/// <summary>
/// 将现有 ArtifactStore 与 ProjectionStores 包裹为 Artifact / State / Projection Module。
/// Wraps the existing ArtifactStore and ProjectionStores as the Artifact / State / Projection Module.
/// </summary>
public sealed class ArtifactStateProjectionModuleAdapter : IArtifactStateProjectionModule
{
    private readonly IArtifactStore artifactStore;
    private readonly IProjectionSnapshotSource projectionSource;
    private readonly IProjectionRuntimeStores? projectionRuntimeStores;

    public ArtifactStateProjectionModuleAdapter(
        IArtifactStore artifactStore,
        IProjectionSnapshotSource projectionSource,
        IProjectionRuntimeStores? projectionRuntimeStores = null,
        ModuleDescriptor? descriptor = null)
    {
        this.artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        this.projectionSource = projectionSource ?? throw new ArgumentNullException(nameof(projectionSource));
        this.projectionRuntimeStores = projectionRuntimeStores;
        Descriptor = descriptor ?? BuiltInModuleDescriptors.ArtifactStateProjection();
    }

    public ModuleDescriptor Descriptor { get; }

    public async ValueTask<ArtifactModuleMutationResult> ExecuteArtifactAsync(
        ArtifactModuleMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var record = invocation.Mutation switch
        {
            PublishArtifactModuleMutation mutation => await artifactStore
                .PublishAsync(mutation.Command.Artifact, cancellationToken)
                .ConfigureAwait(false),
            PromoteArtifactModuleMutation mutation => await artifactStore
                .PromoteAsync(mutation.Command.ArtifactId, mutation.Command.TargetChannel, cancellationToken)
                .ConfigureAwait(false),
            AttachArtifactToTaskModuleMutation mutation => await artifactStore
                .AttachToTaskAsync(mutation.Command.ArtifactId, mutation.Command.TaskId, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new NotSupportedException($"不支持的 artifact module operation：{invocation.Mutation.OperationName}。"),
        };

        return new ArtifactModuleMutationResult(
            success: true,
            operationName: invocation.Mutation.OperationName,
            record: ToModuleRecord(record));
    }

    public async ValueTask<ArtifactProjectionModuleQueryResult> QueryProjectionAsync(
        ArtifactProjectionModuleQueryInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        return invocation.Query switch
        {
            ReadProjectionSnapshotModuleQuery query => new ArtifactProjectionModuleQueryResult(
                Snapshot: ToSnapshotView(await projectionSource
                    .GetAsync(new ProjectionSnapshotKey(query.ScopeKind, query.ScopeKey), cancellationToken)
                    .ConfigureAwait(false))),
            _ => new ArtifactProjectionModuleQueryResult(
                DegradedSources: [$"unsupported_projection_query:{invocation.Query.GetType().Name}"]),
        };
    }

    public async ValueTask<ArtifactCheckpointMaterializationResult> MaterializeCheckpointAsync(
        ArtifactCheckpointMaterializationInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        if (projectionRuntimeStores is null)
        {
            return new ArtifactCheckpointMaterializationResult(
                success: false,
                invocation.Request.KernelRunId,
                invocation.Request.SourceGraphId,
                invocation.Request.SourceStageId,
                invocation.Request.ExecutionId,
                degradedReason: "projection_runtime_stores_unavailable");
        }

        var trace = await projectionRuntimeStores
            .RecordRecoveryCheckpointAsync(
                invocation.Request.TraceId,
                invocation.Request.ExecutionId,
                invocation.Request.Checkpoint,
                cancellationToken)
            .ConfigureAwait(false);

        return new ArtifactCheckpointMaterializationResult(
            success: true,
            invocation.Request.KernelRunId,
            invocation.Request.SourceGraphId,
            invocation.Request.SourceStageId,
            invocation.Request.ExecutionId,
            checkpointRef: CreateCheckpointRef(invocation.Request),
            trace: trace);
    }

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(new ModuleSmokeCheckResult(
            Descriptor.ModuleId,
            passed: true,
            ModuleHealthStatus.Healthy,
            diagnosticsRefs: ["diagnostics://module/artifact-state-projection/healthy"]));

    private static ArtifactModuleRecord ToModuleRecord(ArtifactStoreRecord record)
        => new(
            record.Artifact,
            record.PromotionChannels,
            record.AttachedTaskIds,
            record.Version,
            record.UpdatedAt);

    private static ProjectionSnapshotView? ToSnapshotView(ProjectionSnapshotRecord? record)
        => record is null
            ? null
            : new ProjectionSnapshotView(
                record.Key.ScopeKind,
                record.Key.ScopeKey,
                record.Delta,
                record.Reset,
                record.Version,
                record.UpdatedAt);

    private static string CreateCheckpointRef(ArtifactCheckpointMaterializationRequest request)
        => $"checkpoint://kernel/{request.KernelRunId.Value}/graphs/{request.SourceGraphId.Value}/stages/{request.SourceStageId.Value}/executions/{request.ExecutionId.Value}";
}
