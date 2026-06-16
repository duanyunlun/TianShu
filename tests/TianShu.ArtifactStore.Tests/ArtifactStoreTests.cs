using System.Text;
using TianShu.ArtifactStore;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;
using TianShu.ProjectionStores;

namespace TianShu.ArtifactStore.Tests;

public sealed class ArtifactStoreTests
{
    [Fact]
    public async Task PublishAsync_WhenArtifactStored_MaterializesArtifactAndCollectionProjections()
    {
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var store = new InMemoryArtifactStore(snapshotStore);
        var artifact = CreateArtifact(
            artifactId: "artifact-001",
            collaborationSpaceId: "space-001",
            producedByParticipantId: "participant-001",
            name: "summary.md");

        var record = await store.PublishAsync(artifact, CancellationToken.None);
        var artifactSnapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Artifact, "artifact-001"),
            CancellationToken.None);
        var collectionSnapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.ArtifactCollection, "space-001"),
            CancellationToken.None);

        Assert.Equal(ArtifactLifecycleState.Published, record.Artifact.State);
        Assert.NotNull(artifactSnapshot);
        var projection = Assert.IsType<ArtifactProjectionPayload>(artifactSnapshot!.Delta!.Payload).Projection;
        Assert.Equal("artifact-001", projection.ArtifactId.Value);
        Assert.Equal("summary.md", projection.Name);
        Assert.Equal(ArtifactKind.Document, projection.Kind);
        Assert.Equal(ArtifactLifecycleState.Published, projection.State);
        Assert.Equal("participant-001", projection.ProducedByParticipant?.Id.Value);

        Assert.NotNull(collectionSnapshot);
        var collection = Assert.IsType<ArtifactCollectionProjectionPayload>(collectionSnapshot!.Delta!.Payload).Projection;
        var item = Assert.Single(collection.Items);
        Assert.Equal("space-001", collection.CollaborationSpace.Id.Value);
        Assert.Equal("artifact-001", item.ArtifactId.Value);
        Assert.Equal(ArtifactLifecycleState.Published, item.State);
    }

    [Fact]
    public async Task ListAsync_WhenFilteredBySpaceAndProducer_ReturnsMatchingRecords()
    {
        var store = new InMemoryArtifactStore();
        await store.PublishAsync(CreateArtifact("artifact-010", "space-a", "participant-a", "alpha.md"), CancellationToken.None);
        await store.PublishAsync(CreateArtifact("artifact-011", "space-a", "participant-b", "beta.md"), CancellationToken.None);
        await store.PublishAsync(CreateArtifact("artifact-012", "space-b", "participant-a", "gamma.md"), CancellationToken.None);

        var results = await store.ListAsync(
            new ListArtifacts(
                new CollaborationSpaceId("space-a"),
                new ParticipantId("participant-a")),
            CancellationToken.None);

        var record = Assert.Single(results);
        Assert.Equal("artifact-010", record.Artifact.Id.Value);
    }

    [Fact]
    public async Task PromoteAsync_WhenArtifactExists_UpdatesStateAndCollectionProjection()
    {
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var store = new InMemoryArtifactStore(snapshotStore);
        await store.PublishAsync(CreateArtifact("artifact-020", "space-a", "participant-a", "release-notes.md"), CancellationToken.None);

        var record = await store.PromoteAsync(new ArtifactId("artifact-020"), "stable", CancellationToken.None);
        var collectionSnapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.ArtifactCollection, "space-a"),
            CancellationToken.None);

        Assert.Equal(ArtifactLifecycleState.Promoted, record.Artifact.State);
        Assert.Contains("stable", record.PromotionChannels);
        Assert.Equal(3, record.Version);
        Assert.NotNull(collectionSnapshot);
        var item = Assert.Single(Assert.IsType<ArtifactCollectionProjectionPayload>(collectionSnapshot!.Delta!.Payload).Projection.Items);
        Assert.Equal(ArtifactLifecycleState.Promoted, item.State);
        Assert.Contains("stable", item.PromotionChannels);
    }

    [Fact]
    public async Task AttachToTaskAsync_WhenSameTaskAttachedTwice_DeduplicatesAttachment()
    {
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var store = new InMemoryArtifactStore(snapshotStore);
        await store.PublishAsync(CreateArtifact("artifact-030", "space-a", "participant-a", "task-output.json"), CancellationToken.None);

        var first = await store.AttachToTaskAsync(new ArtifactId("artifact-030"), new TaskId("task-030"), CancellationToken.None);
        var second = await store.AttachToTaskAsync(new ArtifactId("artifact-030"), new TaskId("task-030"), CancellationToken.None);
        var collectionSnapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.ArtifactCollection, "space-a"),
            CancellationToken.None);

        var taskId = Assert.Single(first.AttachedTaskIds);
        Assert.Equal("task-030", taskId.Value);
        Assert.Single(second.AttachedTaskIds);
        Assert.Equal(first.Version, second.Version);
        Assert.NotNull(collectionSnapshot);
        var item = Assert.Single(Assert.IsType<ArtifactCollectionProjectionPayload>(collectionSnapshot!.Delta!.Payload).Projection.Items);
        Assert.Equal("task-030", Assert.Single(item.AttachedTaskIds).Value);
    }

    [Fact]
    public async Task FileSystemArtifactStore_WhenRecreated_CanRecoverPersistedCurrentState()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var firstStore = new FileSystemArtifactStore(rootPath);
            await firstStore.PublishAsync(CreateArtifact("artifact-fs-001", "space-fs", "participant-fs", "persisted.md"), CancellationToken.None);
            await firstStore.PromoteAsync(new ArtifactId("artifact-fs-001"), "stable", CancellationToken.None);
            await firstStore.AttachToTaskAsync(new ArtifactId("artifact-fs-001"), new TaskId("task-fs-001"), CancellationToken.None);

            var secondStore = new FileSystemArtifactStore(rootPath);
            var record = await secondStore.GetAsync(new ArtifactId("artifact-fs-001"), CancellationToken.None);
            var list = await secondStore.ListAsync(new ListArtifacts(new CollaborationSpaceId("space-fs"), null), CancellationToken.None);

            Assert.NotNull(record);
            Assert.Equal(ArtifactLifecycleState.Promoted, record!.Artifact.State);
            Assert.Contains("stable", record.PromotionChannels);
            Assert.Equal("task-fs-001", Assert.Single(record.AttachedTaskIds).Value);
            Assert.Equal(4, record.Version);
            Assert.Single(list);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactStore_WhenUpdatedAfterReload_RebuildsArtifactCollectionProjection()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var firstStore = new FileSystemArtifactStore(rootPath);
            await firstStore.PublishAsync(CreateArtifact("artifact-fs-010", "space-fs-proj", "participant-fs", "projected.md"), CancellationToken.None);

            var snapshotStore = new InMemoryProjectionSnapshotStore();
            var secondStore = new FileSystemArtifactStore(rootPath, snapshotStore);
            await secondStore.PromoteAsync(new ArtifactId("artifact-fs-010"), "release", CancellationToken.None);

            var artifactSnapshot = await snapshotStore.GetAsync(
                new ProjectionSnapshotKey(ProjectionScopeKind.Artifact, "artifact-fs-010"),
                CancellationToken.None);
            var collectionSnapshot = await snapshotStore.GetAsync(
                new ProjectionSnapshotKey(ProjectionScopeKind.ArtifactCollection, "space-fs-proj"),
                CancellationToken.None);

            Assert.NotNull(artifactSnapshot);
            Assert.NotNull(collectionSnapshot);
            Assert.Equal(
                ArtifactLifecycleState.Promoted,
                Assert.IsType<ArtifactProjectionPayload>(artifactSnapshot!.Delta!.Payload).Projection.State);

            var item = Assert.Single(Assert.IsType<ArtifactCollectionProjectionPayload>(collectionSnapshot!.Delta!.Payload).Projection.Items);
            Assert.Equal(ArtifactLifecycleState.Promoted, item.State);
            Assert.Contains("release", item.PromotionChannels);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactStore_WhenPublishingMetadata_DoesNotCreateContentDirectories()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifact = CreateArtifact("artifact-fs-020", "space-fs-meta", "participant-fs", "metadata-only.md");
            var store = new FileSystemArtifactStore(rootPath);

            await store.PublishAsync(artifact, CancellationToken.None);

            var recordPath = Path.Combine(rootPath, "records", $"{EncodeFileName(artifact.Id.Value)}.json");

            Assert.True(File.Exists(recordPath));
            Assert.False(Directory.Exists(Path.Combine(rootPath, "content")));
            Assert.False(Directory.Exists(Path.Combine(rootPath, "content-history")));
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task ArtifactStateProjectionModuleAdapter_ShouldExecuteArtifactStepOperations()
    {
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        IProjectionSnapshotSource projectionSource = snapshotStore;
        var store = new InMemoryArtifactStore(snapshotStore);
        var module = new ArtifactStateProjectionModuleAdapter(store, projectionSource);
        var publishStep = CreateArtifactStep("artifact-module-publish", "publish");
        var artifact = CreateArtifact("artifact-module-001", "space-module", "participant-module", "module.md");

        var publish = await module.ExecuteArtifactAsync(
            new ArtifactModuleMutationInvocation(
                publishStep,
                new PublishArtifactModuleMutation(new PublishArtifact(artifact)),
                CreateModuleContext(publishStep)),
            CancellationToken.None);
        var promoteStep = CreateArtifactStep("artifact-module-promote", "promote");
        var promote = await module.ExecuteArtifactAsync(
            new ArtifactModuleMutationInvocation(
                promoteStep,
                new PromoteArtifactModuleMutation(new PromoteArtifact(artifact.Id, "stable")),
                CreateModuleContext(promoteStep)),
            CancellationToken.None);
        var attachStep = CreateArtifactStep("artifact-module-attach", "attach");
        var attach = await module.ExecuteArtifactAsync(
            new ArtifactModuleMutationInvocation(
                attachStep,
                new AttachArtifactToTaskModuleMutation(new AttachArtifactToTask(artifact.Id, new TaskId("task-module-001"))),
                CreateModuleContext(attachStep)),
            CancellationToken.None);

        Assert.Equal(ModuleKind.ArtifactStateProjection, module.Descriptor.Kind);
        Assert.True(publish.Success);
        Assert.Equal(ArtifactLifecycleState.Published, publish.Record?.Artifact.State);
        Assert.Equal(ArtifactLifecycleState.Promoted, promote.Record?.Artifact.State);
        Assert.Contains("stable", promote.Record!.PromotionChannels);
        Assert.Equal("task-module-001", Assert.Single(attach.Record!.AttachedTaskIds).Value);
    }

    [Fact]
    public async Task ArtifactStateProjectionModuleAdapter_ShouldReadProjectionThroughReadOnlySource()
    {
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        IProjectionSnapshotSource projectionSource = snapshotStore;
        var store = new InMemoryArtifactStore(snapshotStore);
        var module = new ArtifactStateProjectionModuleAdapter(store, projectionSource);
        var artifact = CreateArtifact("artifact-module-projection", "space-module-projection", "participant-module", "projection.md");
        var step = CreateArtifactStep("artifact-module-projection", "publish");
        await module.ExecuteArtifactAsync(
            new ArtifactModuleMutationInvocation(
                step,
                new PublishArtifactModuleMutation(new PublishArtifact(artifact)),
                CreateModuleContext(step)),
            CancellationToken.None);

        var query = await module.QueryProjectionAsync(
            new ArtifactProjectionModuleQueryInvocation(
                new ReadProjectionSnapshotModuleQuery(ProjectionScopeKind.Artifact, artifact.Id.Value),
                CreateModuleContext(CreateArtifactStep("artifact-module-query", "projection.read"))),
            CancellationToken.None);

        Assert.NotNull(query.Snapshot);
        Assert.Equal(ProjectionScopeKind.Artifact, query.Snapshot!.ScopeKind);
        Assert.Equal("artifact", query.Snapshot.Delta?.Payload.Kind);
        Assert.Empty(query.DegradedSources);
    }

    [Fact]
    public async Task ArtifactStateProjectionModuleAdapter_ShouldMaterializeCheckpointWithKernelSources()
    {
        var projectionStores = new InMemoryProjectionRuntimeStores();
        var module = new ArtifactStateProjectionModuleAdapter(
            new InMemoryArtifactStore(projectionStores.Snapshots),
            projectionStores.Snapshots,
            projectionStores);
        var step = CreateArtifactStep("artifact-module-checkpoint", "checkpoint");
        var request = new ArtifactCheckpointMaterializationRequest(
            new KernelRunId("kernel-run-artifact-test"),
            step.SourceGraphId,
            step.SourceStageId,
            new ExecutionId("execution-artifact-test"),
            new ExecutionTraceId("trace-artifact-test"),
            new RecoveryCheckpoint(
                new ExecutionId("execution-artifact-test"),
                step.SourceStageId.Value,
                StructuredValue.FromString("checkpoint-state")));

        var result = await module.MaterializeCheckpointAsync(
            new ArtifactCheckpointMaterializationInvocation(request, CreateModuleContext(step)),
            CancellationToken.None);

        var replay = await projectionStores.ReplayCheckpoints.GetAsync(
            ReplayCheckpointKey.From(request.Checkpoint),
            CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("kernel-run-artifact-test", result.KernelRunId.Value);
        Assert.Contains("/graphs/graph-artifact/", result.CheckpointRef, StringComparison.Ordinal);
        Assert.NotNull(replay);
        Assert.Single(result.Trace!.RecoveryCheckpoints);
    }

    private static Artifact CreateArtifact(
        string artifactId,
        string collaborationSpaceId,
        string producedByParticipantId,
        string name)
    {
        var collaboration = new CollaborationSpaceRef(
            new CollaborationSpaceId(collaborationSpaceId),
            collaborationSpaceId,
            collaborationSpaceId);
        var participant = ParticipantRef.From(
            new ServiceParticipant(
                new ParticipantId(producedByParticipantId),
                producedByParticipantId,
                "publisher"));

        return new Artifact(
            new ArtifactId(artifactId),
            collaboration,
            name,
            ArtifactKind.Document,
            participant,
            state: ArtifactLifecycleState.Draft);
    }

    private static ArtifactStep CreateArtifactStep(string stepId, string operationName)
        => new(
            stepId,
            new CoreIntentId("intent-artifact"),
            new StageGraphId("graph-artifact"),
            new StageId("stage-artifact"),
            new KernelOperationId("operation-artifact"),
            operationName,
            StructuredValue.FromString("artifact"),
            new PermissionEnvelope(scopes: ["artifact.write"], requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.WorkspaceWrite, ["artifact"], reversible: false, requiresAudit: true),
            new KernelBudget(tokenBudget: 100, timeBudgetMs: 1000, costBudget: 1, retryBudget: 1, toolCallBudget: 1),
            new ContractRef("artifact.output", "v1"),
            new TracePolicy());

    private static ArtifactModuleInvocationContext CreateModuleContext(ArtifactStep step)
        => new(
            step.StepId,
            step.SourceIntentId,
            step.SourceGraphId,
            step.SourceStageId,
            step.SourceKernelOperationId,
            new KernelRunId("kernel-run-artifact"),
            new ExecutionId("execution-artifact"),
            step.Permission,
            step.SideEffect,
            step.Metadata);

    private static string CreateStoreRootPath()
    {
        var rootPath = Path.Combine(
            AppContext.BaseDirectory,
            ".artifact-store-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        return rootPath;
    }

    private static void DeleteStoreRootPath(string rootPath)
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static string EncodeFileName(string value)
        => Convert.ToHexString(Encoding.UTF8.GetBytes(value));
}
