using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Primitives;

namespace TianShu.ArtifactStore;

/// <summary>
/// 基于文件系统的 artifact 当前内容存储。
/// File-system-backed store for current artifact content bindings.
/// </summary>
public sealed class FileSystemArtifactContentStore : IRecoverableArtifactContentStore, IDisposable
{
    private readonly string contentDirectory;
    private readonly string versionDirectory;
    private readonly ArtifactContentStoreRetentionOptions retentionOptions;
    private readonly ArtifactContentStoreSyncOptions syncOptions;
    private readonly JsonSerializerOptions serializerOptions;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Semaphore? crossProcessSemaphore;

    /// <summary>
    /// 初始化文件系统 artifact 内容存储。
    /// Initializes the file-system artifact content store.
    /// </summary>
    public FileSystemArtifactContentStore(
        string rootDirectory,
        ArtifactContentStoreRetentionOptions? retentionOptions = null,
        ArtifactContentStoreSyncOptions? syncOptions = null,
        JsonSerializerOptions? serializerOptions = null)
    {
        var normalizedRootDirectory = Path.GetFullPath(
            IdentifierGuard.AgainstNullOrWhiteSpace(rootDirectory, nameof(rootDirectory)));
        contentDirectory = Path.Combine(normalizedRootDirectory, "content");
        versionDirectory = Path.Combine(normalizedRootDirectory, "content-history");
        Directory.CreateDirectory(contentDirectory);
        Directory.CreateDirectory(versionDirectory);

        this.retentionOptions = retentionOptions ?? new ArtifactContentStoreRetentionOptions();
        this.syncOptions = syncOptions ?? new ArtifactContentStoreSyncOptions();
        this.serializerOptions = serializerOptions is null
            ? new JsonSerializerOptions
            {
                WriteIndented = true,
            }
            : new JsonSerializerOptions(serializerOptions);
        crossProcessSemaphore = this.syncOptions.EnableCrossProcessSync
            ? new Semaphore(1, 1, BuildMutexName(normalizedRootDirectory))
            : null;
    }

    /// <inheritdoc />
    public async Task<ArtifactContentBinding> WriteAsync(
        ArtifactId artifactId,
        ArtifactContent content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(content);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var syncScope = AcquireCrossProcessSync(cancellationToken);
            var existingBinding = await ReadBindingCoreAsync(artifactId, cancellationToken).ConfigureAwait(false);
            var binding = existingBinding is null
                ? new ArtifactContentBinding(artifactId, content)
                : existingBinding.WithContent(content);

            await WriteBindingCoreAsync(binding, cancellationToken).ConfigureAwait(false);
            await WriteVersionSnapshotCoreAsync(binding, cancellationToken).ConfigureAwait(false);
            PruneHistoryCore(binding.ArtifactId);
            return binding;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ArtifactContentBinding?> GetAsync(ArtifactId artifactId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var syncScope = AcquireCrossProcessSync(cancellationToken);
            return await ReadBindingCoreAsync(artifactId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ArtifactContentBinding?> GetVersionAsync(
        ArtifactId artifactId,
        long version,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "版本号必须大于零。");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var syncScope = AcquireCrossProcessSync(cancellationToken);
            return await ReadVersionCoreAsync(artifactId, version, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArtifactContentBinding>> ListVersionsAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var syncScope = AcquireCrossProcessSync(cancellationToken);
            return await ReadAllVersionsCoreAsync(artifactId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ArtifactContentStoreSnapshot?> CaptureAsync(ArtifactId artifactId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var syncScope = AcquireCrossProcessSync(cancellationToken);
            var currentBinding = await ReadBindingCoreAsync(artifactId, cancellationToken).ConfigureAwait(false);
            var historyBindings = await ReadAllVersionsCoreAsync(artifactId, cancellationToken).ConfigureAwait(false);

            return currentBinding is null && historyBindings.Count == 0
                ? null
                : new ArtifactContentStoreSnapshot(currentBinding, historyBindings);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RestoreAsync(ArtifactId artifactId, ArtifactContentStoreSnapshot? snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var syncScope = AcquireCrossProcessSync(cancellationToken);

            if (snapshot?.CurrentBinding is null)
            {
                DeleteBindingCore(artifactId);
            }
            else
            {
                await WriteBindingCoreAsync(snapshot.CurrentBinding, cancellationToken).ConfigureAwait(false);
            }

            DeleteVersionDirectoryCore(artifactId);
            foreach (var binding in snapshot?.HistoryBindings ?? Array.Empty<ArtifactContentBinding>())
            {
                await WriteVersionSnapshotCoreAsync(binding, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        crossProcessSemaphore?.Dispose();
        gate.Dispose();
    }

    private IDisposable? AcquireCrossProcessSync(CancellationToken cancellationToken)
    {
        if (crossProcessSemaphore is null)
        {
            return null;
        }

        while (!crossProcessSemaphore.WaitOne(100))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return new CrossProcessSemaphoreScope(crossProcessSemaphore);
    }

    private async Task<ArtifactContentBinding?> ReadBindingCoreAsync(ArtifactId artifactId, CancellationToken cancellationToken)
    {
        var path = GetBindingPath(artifactId);
        return await ReadBindingDocumentCoreAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ArtifactContentBinding?> ReadVersionCoreAsync(
        ArtifactId artifactId,
        long version,
        CancellationToken cancellationToken)
    {
        var path = GetVersionPath(artifactId, version);
        return await ReadBindingDocumentCoreAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ArtifactContentBinding>> ReadAllVersionsCoreAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken)
    {
        var artifactVersionDirectory = GetVersionDirectory(artifactId);
        if (!Directory.Exists(artifactVersionDirectory))
        {
            return Array.Empty<ArtifactContentBinding>();
        }

        var results = new List<ArtifactContentBinding>();
        foreach (var path in Directory.EnumerateFiles(artifactVersionDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = await ReadBindingDocumentCoreAsync(path, cancellationToken).ConfigureAwait(false);
            if (binding is not null)
            {
                results.Add(binding);
            }
        }

        return results
            .OrderByDescending(static binding => binding.Version)
            .ToArray();
    }

    private async Task<ArtifactContentBinding?> ReadBindingDocumentCoreAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<ArtifactContentBindingDocument>(
            stream,
            serializerOptions,
            cancellationToken).ConfigureAwait(false);
        return document is null
            ? throw new InvalidOperationException($"无法从 '{path}' 反序列化 artifact content binding。")
            : FromDocument(document);
    }

    private async Task WriteBindingCoreAsync(ArtifactContentBinding binding, CancellationToken cancellationToken)
    {
        var path = GetBindingPath(binding.ArtifactId);
        await WriteBindingDocumentCoreAsync(path, binding, cancellationToken).ConfigureAwait(false);
    }

    private void DeleteBindingCore(ArtifactId artifactId)
    {
        var path = GetBindingPath(artifactId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private async Task WriteVersionSnapshotCoreAsync(ArtifactContentBinding binding, CancellationToken cancellationToken)
    {
        var artifactVersionDirectory = GetVersionDirectory(binding.ArtifactId);
        Directory.CreateDirectory(artifactVersionDirectory);
        var path = GetVersionPath(binding.ArtifactId, binding.Version);
        await WriteBindingDocumentCoreAsync(path, binding, cancellationToken).ConfigureAwait(false);
    }

    private void PruneHistoryCore(ArtifactId artifactId)
    {
        var maxHistoryVersions = retentionOptions.MaxHistoryVersions;
        if (maxHistoryVersions is null)
        {
            return;
        }

        var artifactVersionDirectory = GetVersionDirectory(artifactId);
        if (!Directory.Exists(artifactVersionDirectory))
        {
            return;
        }

        var filesToDelete = Directory
            .EnumerateFiles(artifactVersionDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(static path => new
            {
                Path = path,
                Version = TryParseVersion(path),
            })
            .Where(static item => item.Version is not null)
            .OrderByDescending(static item => item.Version)
            .Skip(maxHistoryVersions.Value)
            .Select(static item => item.Path)
            .ToArray();

        foreach (var path in filesToDelete)
        {
            File.Delete(path);
        }
    }

    private async Task WriteBindingDocumentCoreAsync(
        string path,
        ArtifactContentBinding binding,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream,
            ToDocument(binding),
            serializerOptions,
            cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private string GetBindingPath(ArtifactId artifactId)
        => Path.Combine(contentDirectory, $"{EncodeFileName(artifactId.Value)}.json");

    private string GetVersionDirectory(ArtifactId artifactId)
        => Path.Combine(versionDirectory, EncodeFileName(artifactId.Value));

    private string GetVersionPath(ArtifactId artifactId, long version)
        => Path.Combine(GetVersionDirectory(artifactId), $"{version:D20}.json");

    private void DeleteVersionDirectoryCore(ArtifactId artifactId)
    {
        var path = GetVersionDirectory(artifactId);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static long? TryParseVersion(string path)
        => long.TryParse(Path.GetFileNameWithoutExtension(path), out var version)
            ? version
            : null;

    private static string BuildMutexName(string normalizedRootDirectory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRootDirectory)));
        return $@"TianShu.ArtifactContentStore.{hash}";
    }

    private static string EncodeFileName(string value)
        => Convert.ToHexString(Encoding.UTF8.GetBytes(value));

    private static ArtifactContentBindingDocument ToDocument(ArtifactContentBinding binding)
        => new()
        {
            ArtifactId = binding.ArtifactId.Value,
            Version = binding.Version,
            UpdatedAt = binding.UpdatedAt,
            Content = ToDocument(binding.Content),
        };

    private static ArtifactContentBinding FromDocument(ArtifactContentBindingDocument document)
        => new(
            new ArtifactId(document.ArtifactId ?? throw new InvalidOperationException("ArtifactId 不能为空。")),
            FromDocument(document.Content ?? throw new InvalidOperationException("Content document 不能为空。")),
            document.Version <= 0 ? 1 : document.Version,
            document.UpdatedAt == default ? DateTimeOffset.UtcNow : document.UpdatedAt);

    private static ArtifactContentDocument ToDocument(ArtifactContent content)
        => content switch
        {
            ArtifactTextContent text => new ArtifactContentDocument
            {
                Kind = ArtifactContentKind.Text,
                MediaType = text.MediaType,
                MetadataEntries = ToMetadataEntries(text.Metadata),
                Text = text.Text,
                Encoding = text.Encoding,
            },
            ArtifactStructuredContent structured => new ArtifactContentDocument
            {
                Kind = ArtifactContentKind.Structured,
                MediaType = structured.MediaType,
                MetadataEntries = ToMetadataEntries(structured.Metadata),
                StructuredValue = structured.Value,
                Schema = structured.Schema,
            },
            ArtifactBinaryContentReference binaryReference => new ArtifactContentDocument
            {
                Kind = ArtifactContentKind.BinaryReference,
                MediaType = binaryReference.MediaType,
                MetadataEntries = ToMetadataEntries(binaryReference.Metadata),
                Reference = binaryReference.Reference,
                SizeInBytes = binaryReference.SizeInBytes,
                Digest = binaryReference.Digest,
            },
            _ => throw new InvalidOperationException($"不支持的 artifact content 类型：{content.GetType().FullName}"),
        };

    private static ArtifactContent FromDocument(ArtifactContentDocument document)
        => document.Kind switch
        {
            ArtifactContentKind.Text => new ArtifactTextContent(
                document.Text ?? throw new InvalidOperationException("Text content 不能为空。"),
                document.MediaType ?? throw new InvalidOperationException("MediaType 不能为空。"),
                document.Encoding ?? throw new InvalidOperationException("Encoding 不能为空。"),
                FromMetadataEntries(document.MetadataEntries)),
            ArtifactContentKind.Structured => new ArtifactStructuredContent(
                document.StructuredValue ?? throw new InvalidOperationException("StructuredValue 不能为空。"),
                document.MediaType ?? throw new InvalidOperationException("MediaType 不能为空。"),
                document.Schema,
                FromMetadataEntries(document.MetadataEntries)),
            ArtifactContentKind.BinaryReference => new ArtifactBinaryContentReference(
                document.Reference ?? throw new InvalidOperationException("Reference 不能为空。"),
                document.MediaType ?? throw new InvalidOperationException("MediaType 不能为空。"),
                document.SizeInBytes,
                document.Digest,
                FromMetadataEntries(document.MetadataEntries)),
            _ => throw new InvalidOperationException($"不支持的 artifact content kind：{document.Kind}"),
        };

    private static Dictionary<string, StructuredValue>? ToMetadataEntries(MetadataBag metadata)
        => metadata.Count == 0
            ? null
            : new Dictionary<string, StructuredValue>(metadata.Entries, StringComparer.Ordinal);

    private static MetadataBag FromMetadataEntries(Dictionary<string, StructuredValue>? entries)
        => entries is null
            ? MetadataBag.Empty
            : new MetadataBag(new Dictionary<string, StructuredValue>(entries, StringComparer.Ordinal));

    private sealed class ArtifactContentBindingDocument
    {
        public string? ArtifactId { get; set; }

        public ArtifactContentDocument? Content { get; set; }

        public long Version { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class ArtifactContentDocument
    {
        public ArtifactContentKind Kind { get; set; }

        public string? MediaType { get; set; }

        public Dictionary<string, StructuredValue>? MetadataEntries { get; set; }

        public string? Text { get; set; }

        public string? Encoding { get; set; }

        public StructuredValue? StructuredValue { get; set; }

        public string? Schema { get; set; }

        public string? Reference { get; set; }

        public long? SizeInBytes { get; set; }

        public string? Digest { get; set; }
    }

    private sealed class CrossProcessSemaphoreScope(Semaphore semaphore) : IDisposable
    {
        private Semaphore? ownedSemaphore = semaphore;

        public void Dispose()
        {
            if (ownedSemaphore is null)
            {
                return;
            }

            ownedSemaphore.Release();
            ownedSemaphore = null;
        }
    }
}
