using System.Text;
using TianShu.ArtifactStore;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;
using TianShu.ProjectionStores;

namespace TianShu.ArtifactStore.Tests;

public sealed class ArtifactBundleStoreTests
{
    [Fact]
    public async Task FileSystemArtifactBundleStore_WhenPublishWithContentSucceeds_PersistsMetadataAndContent()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifact = CreateArtifact("artifact-bundle-001", "space-bundle", "participant-bundle", "bundle.md");
            var store = new FileSystemArtifactBundleStore(rootPath);

            var result = await store.PublishWithContentAsync(
                artifact,
                new ArtifactTextContent(
                    "# bundle",
                    mediaType: "text/markdown",
                    encoding: "utf-8"),
                CancellationToken.None);

            var metadataStore = new FileSystemArtifactStore(rootPath);
            var contentStore = new FileSystemArtifactContentStore(rootPath);
            var persistedMetadata = await metadataStore.GetAsync(artifact.Id, CancellationToken.None);
            var persistedContent = await contentStore.GetAsync(artifact.Id, CancellationToken.None);

            Assert.Equal(1, result.Metadata.Version);
            Assert.Equal(1, result.Content.Version);
            Assert.NotNull(persistedMetadata);
            Assert.NotNull(persistedContent);
            Assert.Equal("bundle.md", persistedMetadata!.Artifact.Name);
            Assert.Equal("# bundle", Assert.IsType<ArtifactTextContent>(persistedContent!.Content).Text);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task CoordinatedArtifactBundleStore_WhenMetadataPublishFails_DoesNotWriteContent()
    {
        var metadataStore = new FakeRecoverableArtifactStore
        {
            PublishException = new InvalidOperationException("metadata failed"),
        };
        var contentStore = new RecordingArtifactContentStore();
        var bundleStore = new CoordinatedArtifactBundleStore(metadataStore, contentStore);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => bundleStore.PublishWithContentAsync(
            CreateArtifact("artifact-bundle-010", "space-bundle", "participant-bundle", "metadata-fail.md"),
            new ArtifactTextContent("content"),
            CancellationToken.None));

        Assert.Equal("metadata failed", exception.Message);
        Assert.Equal(0, contentStore.WriteCount);
    }

    [Fact]
    public async Task CoordinatedArtifactBundleStore_WhenContentWriteFailsOnFirstPublish_RemovesMetadataRecord()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var metadataStore = new FileSystemArtifactStore(rootPath);
            var contentStore = new RecordingArtifactContentStore
            {
                WriteException = new IOException("content failed"),
            };
            var bundleStore = new CoordinatedArtifactBundleStore(metadataStore, contentStore);
            var artifact = CreateArtifact("artifact-bundle-020", "space-bundle", "participant-bundle", "rollback-first.md");

            await Assert.ThrowsAsync<IOException>(() => bundleStore.PublishWithContentAsync(
                artifact,
                new ArtifactTextContent("content"),
                CancellationToken.None));

            var restored = await metadataStore.GetAsync(artifact.Id, CancellationToken.None);
            Assert.Null(restored);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task CoordinatedArtifactBundleStore_WhenContentWriteFails_ResetsArtifactProjectionSnapshots()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var projectionStore = new InMemoryProjectionSnapshotStore();
            var metadataStore = new FileSystemArtifactStore(rootPath, projectionStore);
            var contentStore = new RecordingArtifactContentStore
            {
                WriteException = new IOException("content failed"),
            };
            var bundleStore = new CoordinatedArtifactBundleStore(metadataStore, contentStore);
            var artifact = CreateArtifact("artifact-bundle-025", "space-bundle", "participant-bundle", "rollback-projection.md");

            await Assert.ThrowsAsync<IOException>(() => bundleStore.PublishWithContentAsync(
                artifact,
                new ArtifactTextContent("content"),
                CancellationToken.None));

            var artifactSnapshot = await projectionStore.GetAsync(
                new ProjectionSnapshotKey(ProjectionScopeKind.Artifact, artifact.Id.Value),
                CancellationToken.None);
            var collectionSnapshot = await projectionStore.GetAsync(
                new ProjectionSnapshotKey(ProjectionScopeKind.ArtifactCollection, artifact.CollaborationSpace.Id.Value),
                CancellationToken.None);

            Assert.NotNull(artifactSnapshot);
            Assert.NotNull(collectionSnapshot);
            Assert.Null(artifactSnapshot!.Delta);
            Assert.NotNull(artifactSnapshot.Reset);
            Assert.Null(collectionSnapshot!.Delta);
            Assert.NotNull(collectionSnapshot.Reset);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task CoordinatedArtifactBundleStore_WhenContentWriteFails_RestoresPreviousMetadataSnapshot()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var metadataStore = new FileSystemArtifactStore(rootPath);
            var existingArtifact = CreateArtifact("artifact-bundle-030", "space-bundle", "participant-bundle", "before.md");
            var existingRecord = await metadataStore.PublishAsync(existingArtifact, CancellationToken.None);
            var contentStore = new RecordingArtifactContentStore
            {
                WriteException = new IOException("content failed"),
            };
            var bundleStore = new CoordinatedArtifactBundleStore(metadataStore, contentStore);
            var updatedArtifact = CreateArtifact("artifact-bundle-030", "space-bundle", "participant-bundle", "after.md");

            await Assert.ThrowsAsync<IOException>(() => bundleStore.PublishWithContentAsync(
                updatedArtifact,
                new ArtifactTextContent("updated"),
                CancellationToken.None));

            var restored = await metadataStore.GetAsync(updatedArtifact.Id, CancellationToken.None);

            Assert.NotNull(restored);
            Assert.Equal(existingRecord.Artifact.Name, restored!.Artifact.Name);
            Assert.Equal(existingRecord.Version, restored.Version);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactBundleStore_WhenPublishedWithContent_SeparatesMetadataAndContentPayload()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifact = CreateArtifact("artifact-bundle-040", "space-bundle", "participant-bundle", "split.md");
            const string contentText = "# separated bundle payload";
            var store = new FileSystemArtifactBundleStore(rootPath);

            await store.PublishWithContentAsync(
                artifact,
                new ArtifactTextContent(contentText, mediaType: "text/markdown", encoding: "utf-8"),
                CancellationToken.None);

            var encodedId = EncodeFileName(artifact.Id.Value);
            var metadataPath = Path.Combine(rootPath, "records", $"{encodedId}.json");
            var contentPath = Path.Combine(rootPath, "content", $"{encodedId}.json");
            var historyPath = Path.Combine(rootPath, "content-history", encodedId, "00000000000000000001.json");
            var metadataJson = await File.ReadAllTextAsync(metadataPath, CancellationToken.None);
            var contentJson = await File.ReadAllTextAsync(contentPath, CancellationToken.None);

            Assert.True(File.Exists(metadataPath));
            Assert.True(File.Exists(contentPath));
            Assert.True(File.Exists(historyPath));
            Assert.DoesNotContain(contentText, metadataJson, StringComparison.Ordinal);
            Assert.Contains(contentText, contentJson, StringComparison.Ordinal);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task CoordinatedArtifactBundleStore_WhenRecoverableContentPartiallyAdvances_RestoresMetadataAndContentSnapshots()
    {
        var artifact = CreateArtifact("artifact-bundle-050", "space-bundle", "participant-bundle", "before.md");
        var metadataStore = new FakeRecoverableArtifactStore();
        var initialMetadata = await metadataStore.PublishAsync(artifact, CancellationToken.None);
        var contentStore = new RecoverableContentStoreThatFailsAfterAdvance(
            artifact.Id,
            new ArtifactTextContent("before-content"));
        var bundleStore = new CoordinatedArtifactBundleStore(metadataStore, contentStore);
        var updatedArtifact = CreateArtifact("artifact-bundle-050", "space-bundle", "participant-bundle", "after.md");

        await Assert.ThrowsAsync<IOException>(() => bundleStore.PublishWithContentAsync(
            updatedArtifact,
            new ArtifactTextContent("after-content"),
            CancellationToken.None));

        var restoredMetadata = await metadataStore.GetAsync(updatedArtifact.Id, CancellationToken.None);
        var restoredContent = await contentStore.GetAsync(updatedArtifact.Id, CancellationToken.None);
        var restoredVersions = await contentStore.ListVersionsAsync(updatedArtifact.Id, CancellationToken.None);

        Assert.NotNull(restoredMetadata);
        Assert.Equal(initialMetadata.Artifact.Name, restoredMetadata!.Artifact.Name);
        Assert.Equal(initialMetadata.Version, restoredMetadata.Version);
        Assert.NotNull(restoredContent);
        Assert.Equal(1, restoredContent!.Version);
        Assert.Equal("before-content", Assert.IsType<ArtifactTextContent>(restoredContent.Content).Text);
        var restoredVersion = Assert.Single(restoredVersions);
        Assert.Equal(1, restoredVersion.Version);
        Assert.Equal("before-content", Assert.IsType<ArtifactTextContent>(restoredVersion.Content).Text);
    }

    [Fact]
    public async Task FakeRecoverableArtifactStore_ShouldSupportPromoteAndAttachWithoutNotSupportedFallbacks()
    {
        var artifact = CreateArtifact("artifact-bundle-060", "space-bundle", "participant-bundle", "fake-store.md");
        var store = new FakeRecoverableArtifactStore();

        var published = await store.PublishAsync(artifact, CancellationToken.None);
        var promoted = await store.PromoteAsync(artifact.Id, "stable", CancellationToken.None);
        var attached = await store.AttachToTaskAsync(artifact.Id, new TaskId("task-bundle-060"), CancellationToken.None);

        Assert.Equal(ArtifactLifecycleState.Published, published.Artifact.State);
        Assert.Equal(ArtifactLifecycleState.Promoted, promoted.Artifact.State);
        Assert.Contains("stable", promoted.PromotionChannels, StringComparer.Ordinal);
        Assert.Contains(attached.AttachedTaskIds, static item => item.Value == "task-bundle-060");
        Assert.True(promoted.Version > published.Version);
        Assert.True(attached.Version > promoted.Version);
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

    private static string CreateStoreRootPath()
    {
        var rootPath = Path.Combine(
            AppContext.BaseDirectory,
            ".artifact-bundle-store-tests",
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

    private sealed class FakeRecoverableArtifactStore : IRecoverableArtifactStore
    {
        private readonly Dictionary<ArtifactId, ArtifactStoreRecord> records = new();

        public Exception? PublishException { get; init; }

        public Task<ArtifactStoreRecord> PublishAsync(Artifact artifact, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PublishException is not null)
            {
                throw PublishException;
            }

            var publishedArtifact = new Artifact(
                artifact.Id,
                artifact.CollaborationSpace,
                artifact.Name,
                artifact.Kind,
                artifact.ProducedByParticipant,
                artifact.Lineage,
                ArtifactLifecycleState.Published,
                artifact.ExecutionTrace,
                artifact.Metadata);
            var nextRecord = records.TryGetValue(artifact.Id, out var existing)
                ? existing.WithArtifact(publishedArtifact)
                : new ArtifactStoreRecord(publishedArtifact);
            records[artifact.Id] = nextRecord;
            return Task.FromResult(nextRecord);
        }

        public Task<ArtifactStoreRecord?> GetAsync(ArtifactId artifactId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            records.TryGetValue(artifactId, out var record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<ArtifactStoreRecord>> ListAsync(ListArtifacts query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ArtifactStoreRecord>>(records.Values.ToArray());
        }

        public Task<ArtifactStoreRecord> PromoteAsync(ArtifactId artifactId, string targetChannel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedChannel = IdentifierGuard.AgainstNullOrWhiteSpace(targetChannel, nameof(targetChannel));
            if (!records.TryGetValue(artifactId, out var existing))
            {
                throw new KeyNotFoundException("未找到待提升的 artifact。");
            }

            var promotedArtifact = new Artifact(
                existing.Artifact.Id,
                existing.Artifact.CollaborationSpace,
                existing.Artifact.Name,
                existing.Artifact.Kind,
                existing.Artifact.ProducedByParticipant,
                existing.Artifact.Lineage,
                ArtifactLifecycleState.Promoted,
                existing.Artifact.ExecutionTrace,
                existing.Artifact.Metadata);
            var promotedRecord = existing.WithArtifact(promotedArtifact).WithPromotionChannel(normalizedChannel);
            records[artifactId] = promotedRecord;
            return Task.FromResult(promotedRecord);
        }

        public Task<ArtifactStoreRecord> AttachToTaskAsync(ArtifactId artifactId, TaskId taskId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!records.TryGetValue(artifactId, out var existing))
            {
                throw new KeyNotFoundException("未找到待挂接的 artifact。");
            }

            var attachedRecord = existing.WithTaskAttachment(taskId);
            records[artifactId] = attachedRecord;
            return Task.FromResult(attachedRecord);
        }

        public Task RestoreAsync(ArtifactId artifactId, ArtifactStoreRecord? snapshot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot is null)
            {
                records.Remove(artifactId);
            }
            else
            {
                records[artifactId] = snapshot;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingArtifactContentStore : IArtifactContentStore
    {
        private readonly Dictionary<ArtifactId, ArtifactContentBinding> bindings = new();

        public int WriteCount { get; private set; }

        public Exception? WriteException { get; init; }

        public Task<ArtifactContentBinding> WriteAsync(
            ArtifactId artifactId,
            ArtifactContent content,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            if (WriteException is not null)
            {
                throw WriteException;
            }

            var binding = bindings.TryGetValue(artifactId, out var existing)
                ? existing.WithContent(content)
                : new ArtifactContentBinding(artifactId, content);
            bindings[artifactId] = binding;
            return Task.FromResult(binding);
        }

        public Task<ArtifactContentBinding?> GetAsync(ArtifactId artifactId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bindings.TryGetValue(artifactId, out var binding);
            return Task.FromResult(binding);
        }

        public Task<ArtifactContentBinding?> GetVersionAsync(ArtifactId artifactId, long version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bindings.TryGetValue(artifactId, out var binding);
            return Task.FromResult(binding?.Version == version ? binding : null);
        }

        public Task<IReadOnlyList<ArtifactContentBinding>> ListVersionsAsync(ArtifactId artifactId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bindings.TryGetValue(artifactId, out var binding);
            return Task.FromResult<IReadOnlyList<ArtifactContentBinding>>(binding is null ? [] : [binding]);
        }
    }

    private sealed class RecoverableContentStoreThatFailsAfterAdvance : IRecoverableArtifactContentStore
    {
        private readonly ArtifactId artifactId;
        private readonly List<ArtifactContentBinding> historyBindings = new();
        private ArtifactContentBinding? currentBinding;

        public RecoverableContentStoreThatFailsAfterAdvance(ArtifactId artifactId, ArtifactContent initialContent)
        {
            this.artifactId = artifactId;
            currentBinding = new ArtifactContentBinding(artifactId, initialContent);
            historyBindings.Add(currentBinding);
        }

        public Task<ArtifactContentStoreSnapshot?> CaptureAsync(ArtifactId artifactId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ArtifactContentStoreSnapshot?>(
                currentBinding is null && historyBindings.Count == 0
                    ? null
                    : new ArtifactContentStoreSnapshot(currentBinding, historyBindings.ToArray()));
        }

        public Task RestoreAsync(ArtifactId artifactId, ArtifactContentStoreSnapshot? snapshot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentBinding = snapshot?.CurrentBinding;
            historyBindings.Clear();
            if (snapshot is not null)
            {
                historyBindings.AddRange(snapshot.HistoryBindings);
            }

            return Task.CompletedTask;
        }

        public Task<ArtifactContentBinding> WriteAsync(ArtifactId artifactId, ArtifactContent content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextBinding = currentBinding is null
                ? new ArtifactContentBinding(artifactId, content)
                : currentBinding.WithContent(content);
            currentBinding = nextBinding;
            historyBindings.Insert(0, nextBinding);
            throw new IOException("content partially advanced");
        }

        public Task<ArtifactContentBinding?> GetAsync(ArtifactId artifactId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(currentBinding);
        }

        public Task<ArtifactContentBinding?> GetVersionAsync(ArtifactId artifactId, long version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(historyBindings.FirstOrDefault(binding => binding.Version == version));
        }

        public Task<IReadOnlyList<ArtifactContentBinding>> ListVersionsAsync(ArtifactId artifactId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ArtifactContentBinding>>(historyBindings.ToArray());
        }
    }
}
