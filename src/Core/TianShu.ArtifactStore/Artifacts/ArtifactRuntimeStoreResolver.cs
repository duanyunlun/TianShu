using TianShu.ArtifactStore;
using TianShu.Configuration;
using TianShu.ProjectionStores;

namespace TianShu.ArtifactStore;

/// <summary>
/// 根据用户级 Artifact Store manifest 创建运行时工件存储。
/// Creates runtime artifact stores from user-level Artifact Store manifests.
/// </summary>
public static class ArtifactRuntimeStoreResolver
{
    public static ArtifactRuntimeStoreSet CreateFromConfigPath(
        string? configFilePath,
        IProjectionRuntimeStores projectionRuntimeStores)
    {
        ArgumentNullException.ThrowIfNull(projectionRuntimeStores);

        var rootDirectory = ResolveRootDirectory(configFilePath);
        var projection = new TianShuArtifactStoreManifestConfiguration().Load(rootDirectory);
        var selected = SelectDefaultStore(projection);
        if (selected is null)
        {
            var inMemoryStore = new InMemoryArtifactStore(projectionRuntimeStores.Snapshots);
            return new ArtifactRuntimeStoreSet(inMemoryStore, null, null, null, "memory", null);
        }

        var (package, store) = selected.Value;
        var storeType = Normalize(store.Type) ?? "filesystem";
        if (!string.Equals(storeType, "filesystem", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(storeType, "local-filesystem", StringComparison.OrdinalIgnoreCase))
        {
            var fallback = new InMemoryArtifactStore(projectionRuntimeStores.Snapshots);
            return new ArtifactRuntimeStoreSet(fallback, null, null, null, "memory", $"不支持的工件存储类型：{store.Type}");
        }

        var root = TianShuArtifactStoreManifestConfiguration.ResolveStoreRootFullPath(package, store);
        var retentionOptions = new ArtifactContentStoreRetentionOptions(store.MaxHistoryVersions);
        var syncOptions = new ArtifactContentStoreSyncOptions(store.EnableCrossProcessSync);
        var metadataStore = new FileSystemArtifactStore(root, projectionRuntimeStores.Snapshots);
        var contentStore = new FileSystemArtifactContentStore(root, retentionOptions, syncOptions);
        var bundleStore = new CoordinatedArtifactBundleStore(metadataStore, contentStore);
        return new ArtifactRuntimeStoreSet(metadataStore, contentStore, bundleStore, new CompositeDisposable(metadataStore, contentStore), storeType, root);
    }

    private static (ArtifactStorePackageManifestValue Package, ArtifactStoreManifestValue Store)? SelectDefaultStore(
        ArtifactStoreManifestProjection projection)
    {
        var configuration = new TianShuArtifactStoreManifestConfiguration();
        var candidates = projection.Files
            .Select(file =>
            {
                try
                {
                    return configuration.Load(projection.RootDirectory, file.Path).SelectedPackage;
                }
                catch
                {
                    return null;
                }
            })
            .Where(static package => package?.Enabled == true)
            .SelectMany(static package => package!.Stores
                .Where(static store => store.Enabled)
                .Select(store => (Package: package!, Store: store)))
            .OrderBy(static item => item.Package.Priority)
            .ThenBy(static item => item.Store.Priority)
            .ToArray();

        return candidates.Length == 0 ? null : candidates[0];
    }

    private static string ResolveRootDirectory(string? configFilePath)
    {
        var path = Normalize(configFilePath);
        return path is null
            ? TianShuArtifactStoreManifestConfiguration.ResolveRootDirectory(TianShuConfigTomlPathResolver.ResolveUserConfigTomlPath())
            : TianShuArtifactStoreManifestConfiguration.ResolveRootDirectory(path);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private sealed class CompositeDisposable(params IDisposable[] disposables) : IDisposable
    {
        public void Dispose()
        {
            foreach (var disposable in disposables.Reverse())
            {
                disposable.Dispose();
            }
        }
    }
}

public sealed record ArtifactRuntimeStoreSet(
    IArtifactStore MetadataStore,
    IArtifactContentStore? ContentStore,
    IArtifactBundleStore? BundleStore,
    IDisposable? Lifetime,
    string SourceType,
    string? SourceDetail)
    : IDisposable
{
    public void Dispose()
        => Lifetime?.Dispose();
}
