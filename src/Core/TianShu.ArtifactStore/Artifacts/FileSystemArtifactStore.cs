using System.Text;
using System.Text.Json;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.ProjectionStores;

namespace TianShu.ArtifactStore;

/// <summary>
/// 基于文件系统元数据文件的 artifact 当前态存储。
/// File-system-backed artifact current-state store that persists metadata records.
/// </summary>
public sealed class FileSystemArtifactStore : IRecoverableArtifactStore, IDisposable
{
    private readonly string recordsDirectory;
    private readonly IProjectionSnapshotStore? projectionSnapshotStore;
    private readonly JsonSerializerOptions serializerOptions;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// 初始化文件持久化 artifact store。
    /// Initializes the file-backed artifact store.
    /// </summary>
    public FileSystemArtifactStore(
        string rootDirectory,
        IProjectionSnapshotStore? projectionSnapshotStore = null,
        JsonSerializerOptions? serializerOptions = null)
    {
        var normalizedRootDirectory = IdentifierGuard.AgainstNullOrWhiteSpace(rootDirectory, nameof(rootDirectory));
        recordsDirectory = Path.Combine(normalizedRootDirectory, "records");
        Directory.CreateDirectory(recordsDirectory);

        this.projectionSnapshotStore = projectionSnapshotStore;
        this.serializerOptions = serializerOptions is null
            ? new JsonSerializerOptions
            {
                WriteIndented = true,
            }
            : new JsonSerializerOptions(serializerOptions);
    }

    /// <inheritdoc />
    public async Task<ArtifactStoreRecord> PublishAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifact);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var publishedArtifact = NormalizeLifecycleState(artifact, ArtifactLifecycleState.Published);
            var existingRecord = await ReadRecordCoreAsync(publishedArtifact.Id, cancellationToken).ConfigureAwait(false);
            var record = existingRecord is null
                ? new ArtifactStoreRecord(publishedArtifact)
                : existingRecord.WithArtifact(publishedArtifact);

            await WriteRecordCoreAsync(record, cancellationToken).ConfigureAwait(false);
            await MaterializeProjectionsAsync(record, cancellationToken).ConfigureAwait(false);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ArtifactStoreRecord?> GetAsync(ArtifactId artifactId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadRecordCoreAsync(artifactId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArtifactStoreRecord>> ListAsync(ListArtifacts query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadAllRecordsCoreAsync(cancellationToken).ConfigureAwait(false);
            var filtered = records
                .Where(record => query.CollaborationSpaceId is null
                    || string.Equals(record.Artifact.CollaborationSpace.Id.Value, query.CollaborationSpaceId.Value, StringComparison.Ordinal))
                .Where(record => query.ProducedByParticipantId is null
                    || string.Equals(record.Artifact.ProducedByParticipant?.Id.Value, query.ProducedByParticipantId.Value, StringComparison.Ordinal))
                .OrderByDescending(static record => record.UpdatedAt)
                .ToArray();

            return filtered;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ArtifactStoreRecord> PromoteAsync(ArtifactId artifactId, string targetChannel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedChannel = IdentifierGuard.AgainstNullOrWhiteSpace(targetChannel, nameof(targetChannel));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existingRecord = await ReadRecordCoreAsync(artifactId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("未找到待提升的 artifact。");
            var promotedArtifact = NormalizeLifecycleState(existingRecord.Artifact, ArtifactLifecycleState.Promoted);
            var record = existingRecord.WithArtifact(promotedArtifact).WithPromotionChannel(normalizedChannel);

            await WriteRecordCoreAsync(record, cancellationToken).ConfigureAwait(false);
            await MaterializeProjectionsAsync(record, cancellationToken).ConfigureAwait(false);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ArtifactStoreRecord> AttachToTaskAsync(ArtifactId artifactId, TaskId taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existingRecord = await ReadRecordCoreAsync(artifactId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("未找到待挂接的 artifact。");
            var record = existingRecord.WithTaskAttachment(taskId);

            await WriteRecordCoreAsync(record, cancellationToken).ConfigureAwait(false);
            await MaterializeProjectionsAsync(record, cancellationToken).ConfigureAwait(false);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RestoreAsync(ArtifactId artifactId, ArtifactStoreRecord? snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentRecord = await ReadRecordCoreAsync(artifactId, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                DeleteRecordCore(artifactId);
            }
            else
            {
                await WriteRecordCoreAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }

            await RefreshProjectionStateAfterRestoreAsync(currentRecord, snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        gate.Dispose();
    }

    private async Task MaterializeProjectionsAsync(ArtifactStoreRecord record, CancellationToken cancellationToken)
    {
        var collaborationRecords = await ReadRecordsForCollaborationSpaceCoreAsync(
            record.Artifact.CollaborationSpace.Id,
            cancellationToken).ConfigureAwait(false);

        await ArtifactStoreProjectionMaterializer.MaterializeAsync(
            projectionSnapshotStore,
            record,
            collaborationRecords,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ArtifactStoreRecord?> ReadRecordCoreAsync(ArtifactId artifactId, CancellationToken cancellationToken)
    {
        var path = GetRecordPath(artifactId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<ArtifactStoreRecordDocument>(stream, serializerOptions, cancellationToken).ConfigureAwait(false);
        return document is null
            ? throw new InvalidOperationException($"无法从 '{path}' 反序列化 artifact store record。")
            : FromDocument(document);
    }

    private async Task<IReadOnlyList<ArtifactStoreRecord>> ReadAllRecordsCoreAsync(CancellationToken cancellationToken)
    {
        var results = new List<ArtifactStoreRecord>();
        foreach (var path in Directory.EnumerateFiles(recordsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<ArtifactStoreRecordDocument>(stream, serializerOptions, cancellationToken).ConfigureAwait(false);
            if (document is not null)
            {
                results.Add(FromDocument(document));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<ArtifactStoreRecord>> ReadRecordsForCollaborationSpaceCoreAsync(
        CollaborationSpaceId collaborationSpaceId,
        CancellationToken cancellationToken)
    {
        var records = await ReadAllRecordsCoreAsync(cancellationToken).ConfigureAwait(false);
        return records
            .Where(record => string.Equals(record.Artifact.CollaborationSpace.Id.Value, collaborationSpaceId.Value, StringComparison.Ordinal))
            .ToArray();
    }

    private async Task WriteRecordCoreAsync(ArtifactStoreRecord record, CancellationToken cancellationToken)
    {
        var path = GetRecordPath(record.Artifact.Id);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, ToDocument(record), serializerOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private void DeleteRecordCore(ArtifactId artifactId)
    {
        var path = GetRecordPath(artifactId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private async Task RefreshProjectionStateAfterRestoreAsync(
        ArtifactStoreRecord? currentRecord,
        ArtifactStoreRecord? restoredSnapshot,
        CancellationToken cancellationToken)
    {
        if (projectionSnapshotStore is null)
        {
            return;
        }

        if (restoredSnapshot is null)
        {
            await ArtifactStoreProjectionMaterializer.ResetArtifactAsync(
                projectionSnapshotStore,
                currentRecord?.Artifact.Id ?? throw new InvalidOperationException("恢复空快照时缺少当前 artifact。"),
                reason: "artifact_store_restore",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ArtifactStoreProjectionMaterializer.MaterializeArtifactAsync(
                projectionSnapshotStore,
                restoredSnapshot,
                cancellationToken).ConfigureAwait(false);
        }

        var affectedSpaces = new Dictionary<string, CollaborationSpaceRef>(StringComparer.Ordinal);
        if (currentRecord is not null)
        {
            affectedSpaces[currentRecord.Artifact.CollaborationSpace.Id.Value] = currentRecord.Artifact.CollaborationSpace;
        }

        if (restoredSnapshot is not null)
        {
            affectedSpaces[restoredSnapshot.Artifact.CollaborationSpace.Id.Value] = restoredSnapshot.Artifact.CollaborationSpace;
        }

        foreach (var collaborationSpace in affectedSpaces.Values)
        {
            var collaborationRecords = await ReadRecordsForCollaborationSpaceCoreAsync(
                collaborationSpace.Id,
                cancellationToken).ConfigureAwait(false);

            if (collaborationRecords.Count == 0)
            {
                await ArtifactStoreProjectionMaterializer.ResetCollectionAsync(
                    projectionSnapshotStore,
                    collaborationSpace,
                    reason: "artifact_store_restore",
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            await ArtifactStoreProjectionMaterializer.MaterializeCollectionAsync(
                projectionSnapshotStore,
                collaborationSpace,
                collaborationRecords,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private string GetRecordPath(ArtifactId artifactId)
        => Path.Combine(recordsDirectory, $"{EncodeFileName(artifactId.Value)}.json");

    private static string EncodeFileName(string value)
        => Convert.ToHexString(Encoding.UTF8.GetBytes(value));

    private static Artifact NormalizeLifecycleState(Artifact artifact, ArtifactLifecycleState state)
        => new(
            artifact.Id,
            artifact.CollaborationSpace,
            artifact.Name,
            artifact.Kind,
            artifact.ProducedByParticipant,
            artifact.Lineage,
            state,
            artifact.ExecutionTrace,
            artifact.Metadata);

    private static ArtifactStoreRecordDocument ToDocument(ArtifactStoreRecord record)
        => new()
        {
            Artifact = ToDocument(record.Artifact),
            PromotionChannels = record.PromotionChannels.ToArray(),
            AttachedTaskIds = record.AttachedTaskIds.Select(static taskId => taskId.Value).ToArray(),
            Version = record.Version,
            UpdatedAt = record.UpdatedAt,
        };

    private static ArtifactStoreRecord FromDocument(ArtifactStoreRecordDocument document)
        => new(
            FromDocument(document.Artifact ?? throw new InvalidOperationException("Artifact document 不能为空。")),
            document.PromotionChannels ?? Array.Empty<string>(),
            (document.AttachedTaskIds ?? Array.Empty<string>()).Select(static taskId => new TaskId(taskId)).ToArray(),
            document.Version <= 0 ? 1 : document.Version,
            document.UpdatedAt == default ? DateTimeOffset.UtcNow : document.UpdatedAt);

    private static ArtifactDocument ToDocument(Artifact artifact)
        => new()
        {
            Id = artifact.Id.Value,
            CollaborationSpace = new CollaborationSpaceRefDocument
            {
                Id = artifact.CollaborationSpace.Id.Value,
                Key = artifact.CollaborationSpace.Key,
                DisplayName = artifact.CollaborationSpace.DisplayName,
            },
            Name = artifact.Name,
            Kind = artifact.Kind,
            ProducedByParticipant = artifact.ProducedByParticipant is null
                ? null
                : new ParticipantRefDocument
                {
                    Id = artifact.ProducedByParticipant.Id.Value,
                    Kind = artifact.ProducedByParticipant.Kind,
                    DisplayName = artifact.ProducedByParticipant.DisplayName,
                },
            Lineage = artifact.Lineage is null
                ? null
                : new ArtifactLineageDocument
                {
                    ParentArtifact = artifact.Lineage.ParentArtifact is null ? null : ToDocument(artifact.Lineage.ParentArtifact),
                    ProducedByExecutionId = artifact.Lineage.ProducedByExecutionId?.Value,
                    SourceArtifact = artifact.Lineage.SourceArtifact is null ? null : ToDocument(artifact.Lineage.SourceArtifact),
                },
            State = artifact.State,
            ExecutionTrace = artifact.ExecutionTrace is null
                ? null
                : new ExecutionTraceRefDocument
                {
                    TraceId = artifact.ExecutionTrace.TraceId.Value,
                    Summary = artifact.ExecutionTrace.Summary,
                },
            MetadataEntries = artifact.Metadata.Entries.Count == 0
                ? null
                : new Dictionary<string, StructuredValue>(artifact.Metadata.Entries, StringComparer.Ordinal),
        };

    private static Artifact FromDocument(ArtifactDocument document)
        => new(
            new ArtifactId(document.Id ?? throw new InvalidOperationException("Artifact Id 不能为空。")),
            new CollaborationSpaceRef(
                new CollaborationSpaceId(document.CollaborationSpace?.Id ?? throw new InvalidOperationException("CollaborationSpace Id 不能为空。")),
                document.CollaborationSpace.Key ?? throw new InvalidOperationException("CollaborationSpace Key 不能为空。"),
                document.CollaborationSpace.DisplayName ?? throw new InvalidOperationException("CollaborationSpace DisplayName 不能为空。")),
            document.Name ?? throw new InvalidOperationException("Artifact 名称不能为空。"),
            document.Kind,
            document.ProducedByParticipant is null
                ? null
                : new ParticipantRef(
                    new ParticipantId(document.ProducedByParticipant.Id ?? throw new InvalidOperationException("Participant Id 不能为空。")),
                    document.ProducedByParticipant.Kind,
                    document.ProducedByParticipant.DisplayName ?? throw new InvalidOperationException("Participant DisplayName 不能为空。")),
            document.Lineage is null
                ? null
                : new ArtifactLineage(
                    document.Lineage.ParentArtifact is null ? null : FromDocument(document.Lineage.ParentArtifact),
                    string.IsNullOrWhiteSpace(document.Lineage.ProducedByExecutionId) ? null : new ExecutionId(document.Lineage.ProducedByExecutionId),
                    document.Lineage.SourceArtifact is null ? null : FromDocument(document.Lineage.SourceArtifact)),
            document.State,
            document.ExecutionTrace is null
                ? null
                : new ExecutionTraceRef(
                    new ExecutionTraceId(document.ExecutionTrace.TraceId ?? throw new InvalidOperationException("ExecutionTrace Id 不能为空。")),
                    document.ExecutionTrace.Summary),
            document.MetadataEntries is null
                ? MetadataBag.Empty
                : new MetadataBag(new Dictionary<string, StructuredValue>(document.MetadataEntries, StringComparer.Ordinal)));

    private static ArtifactRefDocument ToDocument(ArtifactRef artifactRef)
        => new()
        {
            Id = artifactRef.Id.Value,
            Name = artifactRef.Name,
            Kind = artifactRef.Kind,
        };

    private static ArtifactRef FromDocument(ArtifactRefDocument document)
        => new(
            new ArtifactId(document.Id ?? throw new InvalidOperationException("ArtifactRef Id 不能为空。")),
            document.Name,
            document.Kind);

    private sealed class ArtifactStoreRecordDocument
    {
        public ArtifactDocument? Artifact { get; set; }

        public string[]? PromotionChannels { get; set; }

        public string[]? AttachedTaskIds { get; set; }

        public long Version { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class ArtifactDocument
    {
        public string? Id { get; set; }

        public CollaborationSpaceRefDocument? CollaborationSpace { get; set; }

        public string? Name { get; set; }

        public ArtifactKind Kind { get; set; }

        public ParticipantRefDocument? ProducedByParticipant { get; set; }

        public ArtifactLineageDocument? Lineage { get; set; }

        public ArtifactLifecycleState State { get; set; }

        public ExecutionTraceRefDocument? ExecutionTrace { get; set; }

        public Dictionary<string, StructuredValue>? MetadataEntries { get; set; }
    }

    private sealed class CollaborationSpaceRefDocument
    {
        public string? Id { get; set; }

        public string? Key { get; set; }

        public string? DisplayName { get; set; }
    }

    private sealed class ParticipantRefDocument
    {
        public string? Id { get; set; }

        public ParticipantKind Kind { get; set; }

        public string? DisplayName { get; set; }
    }

    private sealed class ArtifactLineageDocument
    {
        public ArtifactRefDocument? ParentArtifact { get; set; }

        public string? ProducedByExecutionId { get; set; }

        public ArtifactRefDocument? SourceArtifact { get; set; }
    }

    private sealed class ArtifactRefDocument
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Kind { get; set; }
    }

    private sealed class ExecutionTraceRefDocument
    {
        public string? TraceId { get; set; }

        public string? Summary { get; set; }
    }
}
