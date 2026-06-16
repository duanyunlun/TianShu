using TianShu.ArtifactStore;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime.Tests;

public sealed class ArtifactRuntimeStoreResolverTests
{
    [Fact]
    public async Task CreateFromConfigPath_WhenFilesystemManifestExists_PersistsArtifactMetadata()
    {
        using var temp = TempTianShuHome.Create();
        var configPath = Path.Combine(temp.Root, "tianshu.toml");
        File.WriteAllText(configPath, "model = \"gpt-test\"\n");
        WriteManifest(Path.Combine(temp.Root, "modules", "artifacts", "stores", "builtin", "store.toml"));

        using (var first = ArtifactRuntimeStoreResolver.CreateFromConfigPath(configPath, new ProjectionStores.InMemoryProjectionRuntimeStores()))
        {
            await first.MetadataStore.PublishAsync(CreateArtifact("artifact-001"), CancellationToken.None);
        }

        using var second = ArtifactRuntimeStoreResolver.CreateFromConfigPath(configPath, new ProjectionStores.InMemoryProjectionRuntimeStores());
        var record = await second.MetadataStore.GetAsync(new ArtifactId("artifact-001"), CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("artifact-001", record.Artifact.Id.Value);
    }

    [Fact]
    public void CreateFromConfigPath_WhenNoManifestExists_UsesInMemoryFallback()
    {
        using var temp = TempTianShuHome.Create();
        var configPath = Path.Combine(temp.Root, "tianshu.toml");
        File.WriteAllText(configPath, "model = \"gpt-test\"\n");

        using var storeSet = ArtifactRuntimeStoreResolver.CreateFromConfigPath(configPath, new ProjectionStores.InMemoryProjectionRuntimeStores());

        Assert.Equal("memory", storeSet.SourceType);
        Assert.Null(storeSet.SourceDetail);
    }

    private static Artifact CreateArtifact(string id)
        => new(
            new ArtifactId(id),
            new CollaborationSpaceRef(new CollaborationSpaceId("space-artifacts"), "space-artifacts", "Artifact Space"),
            "report.md",
            ArtifactKind.Document,
            producedByParticipant: new ParticipantRef(new ParticipantId("agent-artifacts"), ParticipantKind.Agent, "Artifact Agent"));

    private static void WriteManifest(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            """
            id = "builtin"
            display_name = "TianShu Builtin Artifact Store"
            enabled = true
            type = "builtin"
            priority = 0

            [[stores]]
            id = "local-filesystem"
            enabled = true
            type = "filesystem"
            root = "./data"
            max_history_versions = 20
            enable_cross_process_sync = true
            priority = 0
            """);
    }

    private sealed class TempTianShuHome : IDisposable
    {
        private TempTianShuHome(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TempTianShuHome Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-artifact-runtime-store-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TempTianShuHome(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
