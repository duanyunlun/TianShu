using System.Text;
using TianShu.ArtifactStore;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Primitives;

namespace TianShu.ArtifactStore.Tests;

public sealed class ArtifactContentStoreTests
{
    [Fact]
    public async Task FileSystemArtifactContentStore_WhenTextContentStored_CanReadBackAfterRecreate()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-text-001");
            var firstStore = new FileSystemArtifactContentStore(rootPath);
            var written = await firstStore.WriteAsync(
                artifactId,
                new ArtifactTextContent(
                    "# Design Notes",
                    mediaType: "text/markdown",
                    encoding: "utf-8"),
                CancellationToken.None);

            var secondStore = new FileSystemArtifactContentStore(rootPath);
            var restored = await secondStore.GetAsync(artifactId, CancellationToken.None);

            Assert.Equal(1, written.Version);
            Assert.NotNull(restored);
            var text = Assert.IsType<ArtifactTextContent>(restored!.Content);
            Assert.Equal("text/markdown", text.MediaType);
            Assert.Equal("# Design Notes", text.Text);
            Assert.Equal("utf-8", text.Encoding);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactContentStore_WhenStructuredContentStored_PreservesStructuredValue()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-structured-001");
            var store = new FileSystemArtifactContentStore(rootPath);
            await store.WriteAsync(
                artifactId,
                new ArtifactStructuredContent(
                    StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["title"] = StructuredValue.FromString("Weekly Summary"),
                        ["items"] = StructuredValue.FromArray(
                            [
                                StructuredValue.FromString("alpha"),
                                StructuredValue.FromString("beta"),
                            ]),
                    }),
                    mediaType: "application/json",
                    schema: "tianshu.summary.v1"),
                CancellationToken.None);

            var restored = await store.GetAsync(artifactId, CancellationToken.None);

            Assert.NotNull(restored);
            var structured = Assert.IsType<ArtifactStructuredContent>(restored!.Content);
            Assert.Equal("tianshu.summary.v1", structured.Schema);
            Assert.Equal("Weekly Summary", structured.Value.GetProperty("title").GetString());
            Assert.Equal(2, structured.Value.GetProperty("items").Items.Count);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactContentStore_WhenBinaryReferenceOverwritten_UpdatesCurrentBinding()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-binary-001");
            var store = new FileSystemArtifactContentStore(rootPath);
            await store.WriteAsync(
                artifactId,
                new ArtifactBinaryContentReference(
                    @"files\report-v1.pdf",
                    mediaType: "application/pdf",
                    sizeInBytes: 100,
                    digest: "sha256:v1"),
                CancellationToken.None);

            var written = await store.WriteAsync(
                artifactId,
                new ArtifactBinaryContentReference(
                    @"files\report-v2.pdf",
                    mediaType: "application/pdf",
                    sizeInBytes: 200,
                    digest: "sha256:v2"),
                CancellationToken.None);

            var restored = await store.GetAsync(artifactId, CancellationToken.None);

            Assert.Equal(2, written.Version);
            Assert.NotNull(restored);
            var binaryReference = Assert.IsType<ArtifactBinaryContentReference>(restored!.Content);
            Assert.Equal(@"files\report-v2.pdf", binaryReference.Reference);
            Assert.Equal(200, binaryReference.SizeInBytes);
            Assert.Equal("sha256:v2", binaryReference.Digest);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactContentStore_WhenContentOverwritten_CanReadHistoricalVersion()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-history-001");
            var store = new FileSystemArtifactContentStore(rootPath);
            await store.WriteAsync(
                artifactId,
                new ArtifactTextContent(
                    "v1",
                    mediaType: "text/plain",
                    encoding: "utf-8"),
                CancellationToken.None);
            await store.WriteAsync(
                artifactId,
                new ArtifactTextContent(
                    "v2",
                    mediaType: "text/plain",
                    encoding: "utf-8"),
                CancellationToken.None);

            var history = await store.GetVersionAsync(artifactId, 1, CancellationToken.None);
            var current = await store.GetAsync(artifactId, CancellationToken.None);

            Assert.NotNull(history);
            Assert.NotNull(current);
            Assert.Equal("v1", Assert.IsType<ArtifactTextContent>(history!.Content).Text);
            Assert.Equal("v2", Assert.IsType<ArtifactTextContent>(current!.Content).Text);
            Assert.Equal(1, history.Version);
            Assert.Equal(2, current.Version);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactContentStore_WhenRecreated_CanListPersistedHistoryVersions()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-history-002");
            var firstStore = new FileSystemArtifactContentStore(rootPath);
            await firstStore.WriteAsync(
                artifactId,
                new ArtifactStructuredContent(
                    StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["revision"] = StructuredValue.FromNumber("1"),
                    })),
                CancellationToken.None);
            await firstStore.WriteAsync(
                artifactId,
                new ArtifactStructuredContent(
                    StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["revision"] = StructuredValue.FromNumber("2"),
                    })),
                CancellationToken.None);

            var secondStore = new FileSystemArtifactContentStore(rootPath);
            var versions = await secondStore.ListVersionsAsync(artifactId, CancellationToken.None);

            Assert.Equal(2, versions.Count);
            Assert.Equal(2, versions[0].Version);
            Assert.Equal(1, versions[1].Version);
            Assert.Equal(2, Assert.IsType<ArtifactStructuredContent>(versions[0].Content).Value.GetProperty("revision").GetInt32());
            Assert.Equal(1, Assert.IsType<ArtifactStructuredContent>(versions[1].Content).Value.GetProperty("revision").GetInt32());
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactContentStore_WhenRetentionConfigured_PrunesOldHistoryVersions()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-retention-001");
            var store = new FileSystemArtifactContentStore(
                rootPath,
                retentionOptions: new ArtifactContentStoreRetentionOptions(maxHistoryVersions: 2));
            await store.WriteAsync(artifactId, new ArtifactTextContent("v1"), CancellationToken.None);
            await store.WriteAsync(artifactId, new ArtifactTextContent("v2"), CancellationToken.None);
            await store.WriteAsync(artifactId, new ArtifactTextContent("v3"), CancellationToken.None);

            var versions = await store.ListVersionsAsync(artifactId, CancellationToken.None);
            var pruned = await store.GetVersionAsync(artifactId, 1, CancellationToken.None);
            var retained = await store.GetVersionAsync(artifactId, 2, CancellationToken.None);

            Assert.Equal(2, versions.Count);
            Assert.Equal(3, versions[0].Version);
            Assert.Equal(2, versions[1].Version);
            Assert.Null(pruned);
            Assert.NotNull(retained);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactContentStore_WhenRetentionConfigured_KeepsCurrentBindingAfterPrune()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-retention-002");
            var store = new FileSystemArtifactContentStore(
                rootPath,
                retentionOptions: new ArtifactContentStoreRetentionOptions(maxHistoryVersions: 1));
            await store.WriteAsync(artifactId, new ArtifactBinaryContentReference(@"files\v1.bin", "application/octet-stream"), CancellationToken.None);
            await store.WriteAsync(artifactId, new ArtifactBinaryContentReference(@"files\v2.bin", "application/octet-stream"), CancellationToken.None);

            var current = await store.GetAsync(artifactId, CancellationToken.None);
            var versions = await store.ListVersionsAsync(artifactId, CancellationToken.None);

            Assert.NotNull(current);
            Assert.Equal(2, current!.Version);
            Assert.Equal(@"files\v2.bin", Assert.IsType<ArtifactBinaryContentReference>(current.Content).Reference);
            var retained = Assert.Single(versions);
            Assert.Equal(2, retained.Version);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactContentStore_WhenCrossInstanceSyncEnabled_PreservesMonotonicVersionsAcrossConcurrentWrites()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-sync-001");
            var firstStore = new FileSystemArtifactContentStore(
                rootPath,
                syncOptions: new ArtifactContentStoreSyncOptions(enableCrossProcessSync: true));
            var secondStore = new FileSystemArtifactContentStore(
                rootPath,
                syncOptions: new ArtifactContentStoreSyncOptions(enableCrossProcessSync: true));

            await Task.WhenAll(
                firstStore.WriteAsync(artifactId, new ArtifactTextContent("alpha"), CancellationToken.None),
                secondStore.WriteAsync(artifactId, new ArtifactTextContent("beta"), CancellationToken.None));

            var versions = await firstStore.ListVersionsAsync(artifactId, CancellationToken.None);
            var current = await secondStore.GetAsync(artifactId, CancellationToken.None);

            Assert.Equal(2, versions.Count);
            Assert.Equal(2, versions[0].Version);
            Assert.Equal(1, versions[1].Version);
            Assert.NotNull(current);
            Assert.Equal(2, current!.Version);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactContentStore_WhenPersisted_KeepsCurrentAndHistoryFilesSeparated()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-layout-001");
            var store = new FileSystemArtifactContentStore(rootPath);
            await store.WriteAsync(artifactId, new ArtifactTextContent("layout-v1"), CancellationToken.None);
            await store.WriteAsync(artifactId, new ArtifactTextContent("layout-v2"), CancellationToken.None);

            var encodedId = EncodeFileName(artifactId.Value);
            var currentPath = Path.Combine(rootPath, "content", $"{encodedId}.json");
            var historyDirectory = Path.Combine(rootPath, "content-history", encodedId);
            var firstVersionPath = Path.Combine(historyDirectory, "00000000000000000001.json");
            var secondVersionPath = Path.Combine(historyDirectory, "00000000000000000002.json");

            Assert.True(File.Exists(currentPath));
            Assert.True(Directory.Exists(historyDirectory));
            Assert.True(File.Exists(firstVersionPath));
            Assert.True(File.Exists(secondVersionPath));
            Assert.False(Directory.Exists(Path.Combine(rootPath, "records")));
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactContentStore_WhenRetentionConfigured_PrunesOnlyHistoryFilesAndKeepsCurrentBindingFile()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-layout-002");
            var store = new FileSystemArtifactContentStore(
                rootPath,
                retentionOptions: new ArtifactContentStoreRetentionOptions(maxHistoryVersions: 1));
            await store.WriteAsync(artifactId, new ArtifactTextContent("layout-v1"), CancellationToken.None);
            await store.WriteAsync(artifactId, new ArtifactTextContent("layout-v2"), CancellationToken.None);

            var encodedId = EncodeFileName(artifactId.Value);
            var currentPath = Path.Combine(rootPath, "content", $"{encodedId}.json");
            var historyDirectory = Path.Combine(rootPath, "content-history", encodedId);
            var firstVersionPath = Path.Combine(historyDirectory, "00000000000000000001.json");
            var secondVersionPath = Path.Combine(historyDirectory, "00000000000000000002.json");
            var current = await store.GetAsync(artifactId, CancellationToken.None);

            Assert.True(File.Exists(currentPath));
            Assert.False(File.Exists(firstVersionPath));
            Assert.True(File.Exists(secondVersionPath));
            Assert.NotNull(current);
            Assert.Equal(2, current!.Version);
            Assert.Equal("layout-v2", Assert.IsType<ArtifactTextContent>(current.Content).Text);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    [Fact]
    public async Task FileSystemArtifactContentStore_WhenRestoredToSnapshot_RevertsCurrentBindingAndHistory()
    {
        var rootPath = CreateStoreRootPath();

        try
        {
            var artifactId = new ArtifactId("artifact-content-restore-001");
            var store = new FileSystemArtifactContentStore(rootPath);
            await store.WriteAsync(artifactId, new ArtifactTextContent("restore-v1"), CancellationToken.None);
            var snapshot = await store.CaptureAsync(artifactId, CancellationToken.None);
            await store.WriteAsync(artifactId, new ArtifactTextContent("restore-v2"), CancellationToken.None);

            await store.RestoreAsync(artifactId, snapshot, CancellationToken.None);

            var current = await store.GetAsync(artifactId, CancellationToken.None);
            var versions = await store.ListVersionsAsync(artifactId, CancellationToken.None);

            Assert.NotNull(snapshot);
            Assert.NotNull(current);
            Assert.Equal(1, current!.Version);
            Assert.Equal("restore-v1", Assert.IsType<ArtifactTextContent>(current.Content).Text);
            var restoredVersion = Assert.Single(versions);
            Assert.Equal(1, restoredVersion.Version);
            Assert.Equal("restore-v1", Assert.IsType<ArtifactTextContent>(restoredVersion.Content).Text);
        }
        finally
        {
            DeleteStoreRootPath(rootPath);
        }
    }

    private static string CreateStoreRootPath()
    {
        var rootPath = Path.Combine(
            AppContext.BaseDirectory,
            ".artifact-content-store-tests",
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
