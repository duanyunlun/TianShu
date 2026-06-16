using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using TianShu.AppHost.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost.Tools;

internal sealed record KernelPluginSkillRoot(string RootPath, string Namespace);

internal sealed record KernelPluginSourceInfo(
    string Type,
    string Path,
    string? Sha256 = null,
    string IntegrityStatus = "not-declared",
    string? Signer = null,
    string TrustStatus = "not-declared",
    string SignatureStatus = "not-declared");

internal sealed record KernelPluginInterfaceInfo(
    string? DisplayName,
    string? ShortDescription,
    string? LongDescription,
    string? DeveloperName,
    string? Category,
    IReadOnlyList<string> Capabilities,
    string? WebsiteUrl,
    string? PrivacyPolicyUrl,
    string? TermsOfServiceUrl,
    string? DefaultPrompt,
    string? BrandColor,
    string? ComposerIcon,
    string? Logo,
    IReadOnlyList<string> Screenshots);

internal sealed record KernelPluginMarketplacePluginSummary(
    string Id,
    string Name,
    KernelPluginSourceInfo Source,
    bool Installed,
    bool Enabled,
    string InstallPolicy,
    string AuthPolicy,
    KernelPluginInterfaceInfo? Interface);

internal sealed record KernelPluginMarketplaceSummary(string Name, string MarketplacePath, IReadOnlyList<KernelPluginMarketplacePluginSummary> Plugins);

internal sealed record KernelPluginListRequest(IReadOnlyList<string>? Cwds, bool ForceRemoteSync = false);

internal sealed record KernelPluginListResult(IReadOnlyList<KernelPluginMarketplaceSummary> Marketplaces, string? RemoteSyncError);

internal sealed record KernelRemotePluginState(string PluginId, bool Enabled);

internal sealed record KernelPluginInstallRequest(string MarketplacePath, string PluginName, string? Cwd = null);

internal sealed record KernelPluginInstallResult(string PluginKey, string InstalledPath, string AuthPolicy);

internal sealed record KernelPluginReadRequest(string MarketplacePath, string PluginName);

internal sealed record KernelPluginUninstallRequest(string PluginId, string? Cwd = null);

internal sealed record KernelPluginSkillSummary(
    string Name,
    string Description,
    string? ShortDescription,
    object? Interface,
    string Path);

internal sealed record KernelPluginAppSummary(
    string Id,
    string Name,
    string? Description,
    string? InstallUrl);

internal sealed record KernelPluginReadResult(
    string MarketplaceName,
    string MarketplacePath,
    KernelPluginMarketplacePluginSummary Summary,
    string? Description,
    IReadOnlyList<KernelPluginSkillSummary> Skills,
    IReadOnlyList<KernelPluginAppSummary> Apps,
    IReadOnlyList<string> McpServers);

internal sealed record KernelPluginCapabilitySummary(
    string ConfigName,
    string DisplayName,
    bool HasSkills,
    IReadOnlyList<string> McpServerNames,
    IReadOnlyList<string> AppIds);

internal sealed class KernelPluginInstallException : Exception
{
    public KernelPluginInstallException(string message, bool invalidRequest, Exception? innerException = null)
        : base(message, innerException)
    {
        InvalidRequest = invalidRequest;
    }

    public bool InvalidRequest { get; }
}

internal sealed class KernelPluginsManager
{
    // 插件 manifest 是外部 JSON 协议面；camelCase 兼容只能留在 .tianshu-plugin/.mcp/.app/marketplace bridge 内。
    // Plugin manifests are external JSON surfaces; camelCase compatibility must stay inside manifest bridges.
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private const string PluginsCacheRelativePath = "plugins/cache";
    private const string PluginManifestRelativePath = ".tianshu-plugin/plugin.json";
    private const string MarketplaceRelativePath = ".agents/plugins/marketplace.json";
    private const string CuratedMarketplaceRelativePath = ".tmp/plugins/.agents/plugins/marketplace.json";
    private const string CuratedPluginsShaRelativePath = ".tmp/plugins.sha";
    private const string RemoteMarketplacesCacheRelativePath = "plugins/marketplaces";
    private const string DefaultPluginVersion = "local";
    private const string DefaultSkillsDirectoryName = "skills";
    private const string DefaultMcpConfigFileName = ".mcp.json";
    private const string DefaultAppConfigFileName = ".app.json";
    private const long DefaultRemoteArchiveMaxBytes = 50L * 1024L * 1024L;
    private const long DefaultRemoteMarketplaceMaxBytes = 2L * 1024L * 1024L;

    private readonly string homePath;
    private readonly Func<CancellationToken, Task<Dictionary<string, string>>>? loadConfigOverridesAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<KernelRemotePluginState>>>? syncRemotePluginStatesAsync;
    private readonly HttpClient httpClient;
    private readonly object cacheGate = new();
    private IReadOnlyList<KernelLoadedPlugin>? cachedPlugins;
    private string? cachedSignature;

    private sealed record KernelParsedPluginManifest(
        string Name,
        string? Description,
        KernelPluginInterfaceInfo? Interface,
        string SkillsRootPath,
        string McpConfigPath,
        string AppConfigPath);

    private sealed record CuratedMarketplacePluginEntry(
        string PluginName,
        string PluginKey,
        string SourceType,
        string SourcePath,
        string? ExpectedSha256,
        string? Signer,
        MarketplacePluginSignature? Signature);

    private sealed record MarketplacePluginSource(string SourceType, string SourcePath);

    private sealed record CuratedMarketplaceState(
        string MarketplaceName,
        IReadOnlyList<CuratedMarketplacePluginEntry> Plugins);

    private sealed record PluginMarketplaceTrustPolicy(
        bool RequireSigner,
        ISet<string> TrustedSigners,
        IReadOnlyDictionary<string, PluginMarketplaceSignerTrust> Signers,
        IReadOnlyDictionary<string, PluginMarketplaceCertificateAuthorityTrust> CertificateAuthorities,
        IReadOnlyDictionary<string, PluginMarketplaceTransparencyLogTrust> TransparencyLogs,
        bool AllowRemoteArchiveSources,
        long RemoteArchiveMaxBytes,
        bool AllowRemoteMarketplaceSources,
        long RemoteMarketplaceMaxBytes);

    private sealed record PluginMarketplaceSignerTrust(string? PublicKeySha256, string? PublicKey);

    private sealed record PluginMarketplaceCertificateAuthorityTrust(bool Enabled, string? CertificateSha256, string? Certificate);

    private sealed record PluginMarketplaceTransparencyLogTrust(bool Enabled, string? PublicKeySha256, string? PublicKey);

    private sealed record PluginRemoteMarketplaceDefinition(
        string Id,
        bool Enabled,
        string? Url,
        string? Sha256);

    private sealed class PluginRemoteMarketplaceBuilder
    {
        public bool? Enabled { get; set; }

        public string? Url { get; set; }

        public string? Sha256 { get; set; }
    }

    private sealed record MarketplacePluginSignature(
        string? Algorithm,
        string? PublicKey,
        string? Signature,
        IReadOnlyList<string> CertificateChain,
        bool CertificateChainDeclared,
        MarketplacePluginTransparencyLog TransparencyLog,
        MarketplacePluginRevocationCheck RevocationCheck)
    {
        public bool IsDeclared =>
            !string.IsNullOrWhiteSpace(Algorithm)
            || !string.IsNullOrWhiteSpace(PublicKey)
            || !string.IsNullOrWhiteSpace(Signature)
            || CertificateChainDeclared
            || TransparencyLog.Declared
            || RevocationCheck.Declared;
    }

    private sealed record MarketplacePluginTransparencyLog(
        bool Declared,
        string? Kind,
        string? LogId,
        long? LogIndex,
        long? TreeSize,
        string? RootHash,
        MarketplacePluginTransparencyCheckpoint Checkpoint,
        IReadOnlyList<MarketplacePluginTransparencyProofNode> InclusionProof);

    private sealed record MarketplacePluginTransparencyCheckpoint(
        bool Declared,
        bool EnvelopeDeclared,
        string? Text,
        string? Origin,
        long? TreeSize,
        string? RootHash,
        string? Signature);

    private sealed record MarketplacePluginTransparencyProofNode(
        bool EnvelopeDeclared,
        string? Text,
        string? Hash,
        string? Position);

    private sealed record MarketplacePluginRevocationCheck(
        bool Declared,
        bool OcspDeclared,
        IReadOnlyList<string> OcspResponses,
        bool CrlDeclared,
        IReadOnlyList<string> Crls,
        bool ProofDeclared,
        string? ProofKind,
        string? Proof,
        bool StatusDeclared,
        string? Status,
        string? CheckedAt);

    private sealed record KernelParsedSkillMetadata(
        string Name,
        string Description,
        string? ShortDescription);

    public KernelPluginsManager(
        Func<CancellationToken, Task<Dictionary<string, string>>>? loadConfigOverridesAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<KernelRemotePluginState>>>? syncRemotePluginStatesAsync = null,
        string? tianShuHome = null,
        HttpClient? httpClient = null)
    {
        this.loadConfigOverridesAsync = loadConfigOverridesAsync;
        this.syncRemotePluginStatesAsync = syncRemotePluginStatesAsync;
        this.httpClient = httpClient ?? KernelCustomCaSupport.CreateHttpClient(TimeSpan.FromSeconds(60));
        homePath = NormalizePath(tianShuHome) ?? TianShuHomePathUtilities.ResolveTianShuHomePath();
    }

    public KernelPluginsManager(
        Func<CancellationToken, Task<Dictionary<string, string>>>? loadConfigOverridesAsync,
        string? tianShuHome)
        : this(loadConfigOverridesAsync, syncRemotePluginStatesAsync: null, tianShuHome)
    {
    }

    public async Task<KernelPluginInstallResult> InstallAsync(KernelPluginInstallRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pluginName = NormalizeRequiredSegment(request.PluginName, "plugin name");
        var marketplacePath = NormalizePath(request.MarketplacePath);
        if (string.IsNullOrWhiteSpace(marketplacePath) || !Path.IsPathFullyQualified(marketplacePath))
        {
            throw new KernelPluginInstallException("marketplacePath 必须是绝对路径。", invalidRequest: true);
        }

        var resolved = ResolveMarketplacePluginFromPath(marketplacePath!, pluginName);
        var trustPolicy = await ReadMarketplaceTrustPolicyAsync(cancellationToken).ConfigureAwait(false);
        var installedPath = InstallResolvedMarketplacePlugin(
            resolved,
            pluginVersion: null,
            trustPolicy,
            cancellationToken);
        PersistEnabledPluginConfig(resolved.PluginKey, request.Cwd);

        return new KernelPluginInstallResult(resolved.PluginKey, installedPath, resolved.AuthPolicy);
    }

    public async Task<KernelPluginListResult> ListAsync(KernelPluginListRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configuredStates = await ReadConfiguredPluginStatesAsync(cancellationToken).ConfigureAwait(false);
        var trustPolicy = await ReadMarketplaceTrustPolicyAsync(cancellationToken).ConfigureAwait(false);
        string? remoteSyncError = null;
        if (request.ForceRemoteSync)
        {
            var remoteMarketplaceSyncConfigured = false;
            try
            {
                var remoteMarketplaces = await ReadRemoteMarketplaceDefinitionsAsync(cancellationToken).ConfigureAwait(false);
                remoteMarketplaceSyncConfigured = remoteMarketplaces.Any(static item => item.Enabled);
                if (remoteMarketplaceSyncConfigured)
                {
                    SyncRemoteMarketplaces(remoteMarketplaces, trustPolicy, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                remoteSyncError = ex.Message;
            }

            if (syncRemotePluginStatesAsync is null)
            {
                if (!remoteMarketplaceSyncConfigured && string.IsNullOrWhiteSpace(remoteSyncError))
                {
                    remoteSyncError = "remote plugin sync is not configured";
                }
            }
            else
            {
                try
                {
                    var remoteStates = await syncRemotePluginStatesAsync(cancellationToken).ConfigureAwait(false);
                    configuredStates = ReconcileCuratedRemotePluginStates(configuredStates, remoteStates, trustPolicy, cancellationToken);
                }
                catch (Exception ex)
                {
                    remoteSyncError = MergeRemoteSyncError(remoteSyncError, ex.Message);
                }
            }
        }

        var seenPluginKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var marketplaces = new List<KernelPluginMarketplaceSummary>();
        foreach (var marketplacePath in DiscoverMarketplacePathsForList(request.Cwds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            marketplaces.Add(BuildMarketplaceSummary(marketplacePath, configuredStates, trustPolicy, seenPluginKeys));
        }

        return new KernelPluginListResult(
            Marketplaces: marketplaces,
            RemoteSyncError: remoteSyncError);
    }

    public async Task<IReadOnlyList<KernelPluginMarketplaceSummary>> ListMarketplacesAsync(IReadOnlyList<string>? cwds, CancellationToken cancellationToken)
    {
        var result = await ListAsync(new KernelPluginListRequest(cwds), cancellationToken).ConfigureAwait(false);
        return result.Marketplaces;
    }

    private static string? MergeRemoteSyncError(string? existing, string? next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return next;
        }

        return $"{existing}; {next}";
    }

    public async Task<KernelPluginReadResult> ReadAsync(KernelPluginReadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pluginName = NormalizeRequiredSegment(request.PluginName, "plugin name");
        var marketplacePath = NormalizePath(request.MarketplacePath);
        if (string.IsNullOrWhiteSpace(marketplacePath) || !Path.IsPathFullyQualified(marketplacePath))
        {
            throw new KernelPluginInstallException("marketplacePath 必须是绝对路径。", invalidRequest: true);
        }

        var resolved = ResolveMarketplacePluginFromPath(marketplacePath!, pluginName);
        if (!TryReadPluginManifest(resolved.SourcePath, out var manifest, out var manifestError))
        {
            throw new KernelPluginInstallException(
                manifestError ?? "missing or invalid .tianshu-plugin/plugin.json",
                invalidRequest: true);
        }

        var configuredStates = await ReadConfiguredPluginStatesAsync(cancellationToken).ConfigureAwait(false);
        var trustPolicy = await ReadMarketplaceTrustPolicyAsync(cancellationToken).ConfigureAwait(false);
        var summary = new KernelPluginMarketplacePluginSummary(
            Id: resolved.PluginKey,
            Name: pluginName,
            Source: BuildPluginSourceInfo(resolved, trustPolicy),
            Installed: IsPluginInstalled(resolved.PluginKey),
            Enabled: configuredStates.TryGetValue(resolved.PluginKey, out var configuredEnabled) && configuredEnabled,
            InstallPolicy: resolved.InstallPolicy,
            AuthPolicy: resolved.AuthPolicy,
            Interface: ApplyMarketplaceCategory(manifest.Interface, resolved.Category));

        var skills = LoadPluginSkillSummaries(manifest.SkillsRootPath, manifest.Name);
        var apps = LoadPluginAppSummaries(manifest.AppConfigPath);
        var mcpServers = LoadPluginMcpServerNames(resolved.SourcePath, manifest.McpConfigPath);

        return new KernelPluginReadResult(
            MarketplaceName: resolved.MarketplaceName,
            MarketplacePath: marketplacePath!,
            Summary: summary,
            Description: manifest.Description,
            Skills: skills,
            Apps: apps,
            McpServers: mcpServers);
    }

    public Task UninstallAsync(KernelPluginUninstallRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parsed = ParsePluginKey(request.PluginId, throwOnInvalid: true)!.Value;
        try
        {
            RemoveInstalledPluginStore(request.PluginId);
            RemovePersistedPluginConfig(request.PluginId, request.Cwd);
            return Task.CompletedTask;
        }
        catch (KernelPluginInstallException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KernelPluginInstallException($"failed to uninstall plugin: {ex.Message}", invalidRequest: false, ex);
        }
    }

    public async Task<IReadOnlyList<KernelPluginSkillRoot>> GetEffectiveSkillRootsAsync(CancellationToken cancellationToken)
    {
        var roots = new Dictionary<string, KernelPluginSkillRoot>(PathComparer);
        foreach (var plugin in (await LoadConfiguredPluginsAsync(cancellationToken).ConfigureAwait(false)).Where(static plugin => plugin.IsActive))
        {
            foreach (var root in plugin.SkillRoots)
            {
                roots.TryAdd(root.RootPath, root);
            }
        }

        return roots.Values
            .OrderBy(static root => root.RootPath, PathComparer)
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<string, KernelPluginMcpServerDefinition>> GetEffectiveMcpServersAsync(CancellationToken cancellationToken)
    {
        var servers = new Dictionary<string, KernelPluginMcpServerDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in (await LoadConfiguredPluginsAsync(cancellationToken).ConfigureAwait(false)).Where(static plugin => plugin.IsActive))
        {
            foreach (var pair in plugin.McpServers)
            {
                servers.TryAdd(pair.Key, pair.Value);
            }
        }

        return servers;
    }

    public async Task<IReadOnlyList<string>> GetEffectiveAppIdsAsync(CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in (await LoadConfiguredPluginsAsync(cancellationToken).ConfigureAwait(false)).Where(static plugin => plugin.IsActive))
        {
            foreach (var appId in plugin.AppIds)
            {
                if (seen.Add(appId))
                {
                    ids.Add(appId);
                }
            }
        }

        return ids;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetEffectiveAppPluginDisplayNamesAsync(CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in (await LoadConfiguredPluginsAsync(cancellationToken).ConfigureAwait(false)).Where(static plugin => plugin.IsActive))
        {
            var displayName = ResolvePluginDisplayName(plugin);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            foreach (var appId in plugin.AppIds)
            {
                if (!map.TryGetValue(appId, out var names))
                {
                    names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[appId] = names;
                }

                names.Add(displayName!);
            }
        }

        return map.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }
    public async Task<IReadOnlyList<KernelPluginCapabilitySummary>> GetEffectiveCapabilitySummariesAsync(CancellationToken cancellationToken)
    {
        var summaries = new List<KernelPluginCapabilitySummary>();
        foreach (var plugin in (await LoadConfiguredPluginsAsync(cancellationToken).ConfigureAwait(false)).Where(static plugin => plugin.IsActive))
        {
            var displayName = ResolvePluginDisplayName(plugin);
            var configName = Normalize(plugin.ConfigKey);
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(configName))
            {
                continue;
            }

            summaries.Add(new KernelPluginCapabilitySummary(
                ConfigName: configName!,
                DisplayName: displayName!,
                HasSkills: plugin.SkillRoots.Count > 0,
                McpServerNames: plugin.McpServers.Keys
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                AppIds: plugin.AppIds
                    .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray()));
        }

        return summaries
            .OrderBy(static summary => summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static summary => summary.ConfigName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ClearCache()
    {
        lock (cacheGate)
        {
            cachedPlugins = null;
            cachedSignature = null;
        }
    }

    private async Task<IReadOnlyList<KernelLoadedPlugin>> LoadConfiguredPluginsAsync(CancellationToken cancellationToken)
    {
        var signature = await BuildConfigSignatureAsync(cancellationToken).ConfigureAwait(false);
        lock (cacheGate)
        {
            if (cachedPlugins is not null && string.Equals(cachedSignature, signature, StringComparison.Ordinal))
            {
                return cachedPlugins;
            }
        }

        IReadOnlyList<KernelLoadedPlugin> loaded;
        if (!await IsPluginsFeatureEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            loaded = Array.Empty<KernelLoadedPlugin>();
        }
        else
        {
            loaded = (await ReadConfiguredPluginStatesAsync(cancellationToken).ConfigureAwait(false))
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => (pair.Key, pair.Value))
                .Select(pair => LoadPlugin(pair.Key, pair.Value))
                .ToArray();
        }

        lock (cacheGate)
        {
            cachedSignature = signature;
            cachedPlugins = loaded;
        }

        return loaded;
    }

    private async Task<string> BuildConfigSignatureAsync(CancellationToken cancellationToken)
    {
        if (loadConfigOverridesAsync is not null)
        {
            var values = await loadConfigOverridesAsync(cancellationToken).ConfigureAwait(false);
            var relevant = values
                .Where(static pair =>
                    pair.Key.StartsWith("plugins.", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Key, "plugins.enabled", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return JsonSerializer.Serialize(relevant);
        }

        var featureOverride = await ReadPluginsFeatureOverrideAsync(cancellationToken).ConfigureAwait(false);
        return string.Concat(
            DescribeFile(GetConfigTomlPath()),
            "|plugins:",
            NormalizeRawConfigValue(featureOverride ?? string.Empty) ?? "<default>");
    }

    private async Task<string?> ReadPluginsFeatureOverrideAsync(CancellationToken cancellationToken)
    {
        if (loadConfigOverridesAsync is null)
        {
            return null;
        }

        var values = await loadConfigOverridesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var key in new[]
        {
            "plugins.enabled",
            "plugins.\"enabled\"",
        })
        {
            if (values.TryGetValue(key, out var rawValue))
            {
                return rawValue;
            }
        }

        return null;
    }

    private async Task<bool> IsPluginsFeatureEnabledAsync(CancellationToken cancellationToken)
    {
        var enabled = ReadPluginsFeatureEnabledFromToml() ?? false;
        var featureOverride = await ReadPluginsFeatureOverrideAsync(cancellationToken).ConfigureAwait(false);
        if (bool.TryParse(NormalizeRawConfigValue(featureOverride ?? string.Empty), out var parsed))
        {
            enabled = parsed;
        }

        return enabled;
    }

    private bool? ReadPluginsFeatureEnabledFromToml()
    {
        var configTomlPath = GetConfigTomlPath();
        if (!File.Exists(configTomlPath))
        {
            return null;
        }

        try
        {
            if (Toml.ToModel(File.ReadAllText(configTomlPath)) is not TomlTable root
                || !root.TryGetValue("plugins", out var pluginsValue)
                || pluginsValue is not TomlTable plugins)
            {
                return null;
            }

            return ReadTomlBool(plugins, "enabled");
        }
        catch
        {
            return null;
        }
    }

    private static string DescribeFile(string path)
    {
        if (!File.Exists(path))
        {
            return "missing";
        }

        var info = new FileInfo(path);
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }

    private async Task<Dictionary<string, bool>> ReadConfiguredPluginStatesAsync(CancellationToken cancellationToken)
    {
        if (loadConfigOverridesAsync is not null)
        {
            var values = await loadConfigOverridesAsync(cancellationToken).ConfigureAwait(false);
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            const string prefix = "plugins.installed.";
            const string suffix = ".enabled";
            foreach (var pair in values)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Length <= prefix.Length + suffix.Length)
                {
                    continue;
                }

                var pluginKey = pair.Key[prefix.Length..^suffix.Length];
                var normalized = NormalizeRawConfigValue(pair.Value);
                result[pluginKey] = !bool.TryParse(normalized, out var enabled) || enabled;
            }

            return result;
        }

        return ReadConfiguredPluginStatesFromToml();
    }

    private async Task<PluginMarketplaceTrustPolicy> ReadMarketplaceTrustPolicyAsync(CancellationToken cancellationToken)
    {
        var trustedSigners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var signerTrusts = new Dictionary<string, PluginMarketplaceSignerTrust>(StringComparer.OrdinalIgnoreCase);
        var certificateAuthorities = new Dictionary<string, PluginMarketplaceCertificateAuthorityTrust>(StringComparer.OrdinalIgnoreCase);
        var transparencyLogs = new Dictionary<string, PluginMarketplaceTransparencyLogTrust>(StringComparer.OrdinalIgnoreCase);
        var requireSigner = false;
        var allowRemoteArchiveSources = false;
        var remoteArchiveMaxBytes = DefaultRemoteArchiveMaxBytes;
        var allowRemoteMarketplaceSources = false;
        var remoteMarketplaceMaxBytes = DefaultRemoteMarketplaceMaxBytes;
        if (loadConfigOverridesAsync is not null)
        {
            var values = await loadConfigOverridesAsync(cancellationToken).ConfigureAwait(false);
            requireSigner = ReadConfigBool(values, false,
                "plugins.marketplace_trust.require_signer");
            allowRemoteArchiveSources = ReadConfigBool(values, false,
                "plugins.marketplace_trust.allow_remote_archive_sources");
            remoteArchiveMaxBytes = ReadConfigLong(values, DefaultRemoteArchiveMaxBytes,
                "plugins.marketplace_trust.remote_archive_max_bytes");
            allowRemoteMarketplaceSources = ReadConfigBool(values, false,
                "plugins.marketplace_trust.allow_remote_marketplace_sources");
            remoteMarketplaceMaxBytes = ReadConfigLong(values, DefaultRemoteMarketplaceMaxBytes,
                "plugins.marketplace_trust.remote_marketplace_max_bytes");

            foreach (var signer in ReadConfigStringSet(values,
                         "plugins.marketplace_trust.trusted_signers"))
            {
                _ = trustedSigners.Add(signer);
            }

            foreach (var pair in ReadConfigSignerTrusts(values))
            {
                signerTrusts[pair.Key] = pair.Value;
            }

            foreach (var pair in ReadConfigCertificateAuthorityTrusts(values))
            {
                certificateAuthorities[pair.Key] = pair.Value;
            }

            foreach (var pair in ReadConfigTransparencyLogTrusts(values))
            {
                transparencyLogs[pair.Key] = pair.Value;
            }

            return new PluginMarketplaceTrustPolicy(
                requireSigner,
                trustedSigners,
                signerTrusts,
                certificateAuthorities,
                transparencyLogs,
                allowRemoteArchiveSources,
                remoteArchiveMaxBytes,
                allowRemoteMarketplaceSources,
                remoteMarketplaceMaxBytes);
        }

        var configTomlPath = GetConfigTomlPath();
        if (!File.Exists(configTomlPath))
        {
            return new PluginMarketplaceTrustPolicy(
                false,
                trustedSigners,
                signerTrusts,
                certificateAuthorities,
                transparencyLogs,
                false,
                DefaultRemoteArchiveMaxBytes,
                false,
                DefaultRemoteMarketplaceMaxBytes);
        }

        try
        {
            if (Toml.ToModel(File.ReadAllText(configTomlPath)) is TomlTable root
                && root.TryGetValue("plugins", out var pluginsValue)
                && pluginsValue is TomlTable plugins)
            {
                var trustTable = ReadTomlTable(plugins, "marketplace_trust");
                if (trustTable is not null)
                {
                    requireSigner = ReadTomlBool(trustTable, "require_signer") ?? false;
                    allowRemoteArchiveSources = ReadTomlBool(trustTable, "allow_remote_archive_sources") ?? false;
                    remoteArchiveMaxBytes = ReadTomlLong(trustTable, "remote_archive_max_bytes") ?? DefaultRemoteArchiveMaxBytes;
                    allowRemoteMarketplaceSources = ReadTomlBool(trustTable, "allow_remote_marketplace_sources") ?? false;
                    remoteMarketplaceMaxBytes = ReadTomlLong(trustTable, "remote_marketplace_max_bytes") ?? DefaultRemoteMarketplaceMaxBytes;

                    foreach (var signer in ReadTomlStringSet(trustTable, "trusted_signers"))
                    {
                        _ = trustedSigners.Add(signer);
                    }

                    foreach (var pair in ReadTomlSignerTrusts(trustTable))
                    {
                        signerTrusts[pair.Key] = pair.Value;
                    }

                    foreach (var pair in ReadTomlCertificateAuthorityTrusts(trustTable))
                    {
                        certificateAuthorities[pair.Key] = pair.Value;
                    }

                    foreach (var pair in ReadTomlTransparencyLogTrusts(trustTable))
                    {
                        transparencyLogs[pair.Key] = pair.Value;
                    }
                }
            }
        }
        catch
        {
            return new PluginMarketplaceTrustPolicy(
                false,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, PluginMarketplaceSignerTrust>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, PluginMarketplaceCertificateAuthorityTrust>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, PluginMarketplaceTransparencyLogTrust>(StringComparer.OrdinalIgnoreCase),
                false,
                DefaultRemoteArchiveMaxBytes,
                false,
                DefaultRemoteMarketplaceMaxBytes);
        }

        return new PluginMarketplaceTrustPolicy(
            requireSigner,
            trustedSigners,
            signerTrusts,
            certificateAuthorities,
            transparencyLogs,
            allowRemoteArchiveSources,
            remoteArchiveMaxBytes,
            allowRemoteMarketplaceSources,
            remoteMarketplaceMaxBytes);
    }

    private async Task<IReadOnlyList<PluginRemoteMarketplaceDefinition>> ReadRemoteMarketplaceDefinitionsAsync(CancellationToken cancellationToken)
    {
        if (loadConfigOverridesAsync is not null)
        {
            var values = await loadConfigOverridesAsync(cancellationToken).ConfigureAwait(false);
            return ReadConfigRemoteMarketplaceDefinitions(values);
        }

        var configTomlPath = GetConfigTomlPath();
        if (!File.Exists(configTomlPath))
        {
            return [];
        }

        try
        {
            if (Toml.ToModel(File.ReadAllText(configTomlPath)) is TomlTable root
                && root.TryGetValue("plugins", out var pluginsValue)
                && pluginsValue is TomlTable plugins)
            {
                return ReadTomlRemoteMarketplaceDefinitions(plugins);
            }
        }
        catch
        {
            return [];
        }

        return [];
    }

    private Dictionary<string, bool> ReadConfiguredPluginStatesFromToml()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var configTomlPath = GetConfigTomlPath();
        if (!File.Exists(configTomlPath))
        {
            return result;
        }

        try
        {
            if (Toml.ToModel(File.ReadAllText(configTomlPath)) is TomlTable root
                && root.TryGetValue("plugins", out var pluginsValue)
                && pluginsValue is TomlTable plugins)
            {
                if (plugins.TryGetValue("installed", out var installedValue)
                    && installedValue is TomlTable installed)
                {
                    foreach (var pair in installed)
                    {
                        if (pair.Value is TomlTable pluginTable)
                        {
                            result[pair.Key] = ReadTomlBool(pluginTable, "enabled") ?? true;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        if (result.Count == 0)
        {
            foreach (var pair in ReadConfiguredPluginStatesFallback(configTomlPath))
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    private static bool IsReservedPluginsTable(string key)
        => string.Equals(key, "installed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "marketplace_trust", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "remote_marketplaces", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, bool> ReadConfiguredPluginStatesFallback(string configTomlPath)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        string? currentPluginKey = null;
        foreach (var rawLine in File.ReadLines(configTomlPath))
        {
            var line = Normalize(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var commentIndex = line.IndexOf('#');
            if (commentIndex >= 0)
            {
                line = Normalize(line[..commentIndex]);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentPluginKey = TryParsePluginSectionKey(line);
                continue;
            }

            if (currentPluginKey is null)
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = Normalize(line[..separatorIndex]);
            if (!string.Equals(key, "enabled", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Normalize(line[(separatorIndex + 1)..]);
            if (bool.TryParse(value, out var enabled))
            {
                result[currentPluginKey] = enabled;
            }
        }

        return result;
    }

    private static string? TryParsePluginSectionKey(string line)
    {
        var normalized = Normalize(line.Trim('[', ']'));
        if (string.IsNullOrWhiteSpace(normalized)
            || !normalized.StartsWith("plugins.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var key = Normalize(normalized["plugins.".Length..]);
        return string.IsNullOrWhiteSpace(key)
            ? null
            : key.Trim().Trim('"').Trim('\'');
    }

    private KernelLoadedPlugin LoadPlugin(string pluginKey, bool enabled)
    {
        var plugin = ParsePluginKey(pluginKey, throwOnInvalid: false);
        var rootPath = plugin is null
            ? GetPluginsCacheRoot()
            : ResolveInstalledPluginRoot(plugin.Value.PluginName, plugin.Value.MarketplaceName)
                ?? Path.Combine(GetPluginInstallRoot(plugin.Value.PluginName, plugin.Value.MarketplaceName), DefaultPluginVersion);

        if (!enabled)
        {
            return new KernelLoadedPlugin(pluginKey, null, rootPath, false, [], new Dictionary<string, KernelPluginMcpServerDefinition>(StringComparer.OrdinalIgnoreCase), [], null);
        }

        if (plugin is null)
        {
            return new KernelLoadedPlugin(pluginKey, null, rootPath, true, [], new Dictionary<string, KernelPluginMcpServerDefinition>(StringComparer.OrdinalIgnoreCase), [], $"invalid plugin key `{pluginKey}`; expected <plugin>@<marketplace>");
        }

        if (!Directory.Exists(rootPath))
        {
            return new KernelLoadedPlugin(pluginKey, null, rootPath, true, [], new Dictionary<string, KernelPluginMcpServerDefinition>(StringComparer.OrdinalIgnoreCase), [], "path does not exist or is not a directory");
        }

        if (!TryReadManifestName(rootPath, out var manifestName, out var manifestError))
        {
            return new KernelLoadedPlugin(pluginKey, null, rootPath, true, [], new Dictionary<string, KernelPluginMcpServerDefinition>(StringComparer.OrdinalIgnoreCase), [], manifestError);
        }

        var skillRoots = new List<KernelPluginSkillRoot>();
        var skillsRoot = Path.Combine(rootPath, DefaultSkillsDirectoryName);
        if (Directory.Exists(skillsRoot))
        {
            skillRoots.Add(new KernelPluginSkillRoot(Path.GetFullPath(skillsRoot), manifestName!));
        }

        return new KernelLoadedPlugin(
            pluginKey,
            manifestName,
            rootPath,
            true,
            skillRoots,
            LoadPluginMcpServers(rootPath),
            LoadPluginAppIds(rootPath),
            null);
    }

    private Dictionary<string, KernelPluginMcpServerDefinition> LoadPluginMcpServers(string pluginRoot)
    {
        var result = new Dictionary<string, KernelPluginMcpServerDefinition>(StringComparer.OrdinalIgnoreCase);
        var configPath = Path.Combine(pluginRoot, DefaultMcpConfigFileName);
        if (!File.Exists(configPath))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!TryReadObject(document.RootElement, out var rootObject))
            {
                return result;
            }

            if (!TryGetProperty(rootObject, out var serversElement, "mcpServers", "mcp_servers")
                || serversElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in serversElement.EnumerateObject())
            {
                var server = ParsePluginMcpServer(pluginRoot, property.Name, property.Value);
                if (server is not null)
                {
                    result.TryAdd(property.Name, server);
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static KernelPluginMcpServerDefinition? ParsePluginMcpServer(string pluginRoot, string name, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var cwd = ReadString(element, "cwd");
        if (!string.IsNullOrWhiteSpace(cwd) && !Path.IsPathRooted(cwd))
        {
            cwd = Path.GetFullPath(Path.Combine(pluginRoot, cwd));
        }

        var startupTimeout = ReadDouble(element, "startupTimeoutSec", "startup_timeout_sec") is double startupTimeoutSec
            ? (TimeSpan?)TimeSpan.FromSeconds(startupTimeoutSec)
            : ReadDouble(element, "startupTimeoutMs", "startup_timeout_ms") is double startupTimeoutMs
                ? TimeSpan.FromMilliseconds(startupTimeoutMs)
                : null;

        var toolTimeout = ReadDouble(element, "toolTimeoutSec", "tool_timeout_sec") is double timeoutSec
            ? (TimeSpan?)TimeSpan.FromSeconds(timeoutSec)
            : ReadDouble(element, "toolTimeoutMs", "tool_timeout_ms") is double timeoutMs
                ? TimeSpan.FromMilliseconds(timeoutMs)
                : null;

        return new KernelPluginMcpServerDefinition(
            Name: name,
            Enabled: ReadBool(element, "enabled") ?? true,
            Command: ReadString(element, "command"),
            Args: ReadStringArray(element, "args"),
            Env: ReadStringMap(element, "env"),
            EnvVars: ReadStringArray(element, "envVars", "env_vars"),
            Cwd: NormalizePath(cwd),
            Url: ReadString(element, "url"),
            BearerTokenEnvVar: ReadString(element, "bearerTokenEnvVar", "bearer_token_env_var"),
            HttpHeaders: ReadStringMap(element, "httpHeaders", "http_headers"),
            EnvHttpHeaders: ReadStringMap(element, "envHttpHeaders", "env_http_headers"),
            StartupTimeout: startupTimeout,
            ToolTimeout: toolTimeout,
            EnabledTools: ReadStringArray(element, "enabledTools", "enabled_tools"),
            DisabledTools: ReadStringArray(element, "disabledTools", "disabled_tools"));
    }

    private static IReadOnlyList<string> LoadPluginAppIds(string pluginRoot)
    {
        var configPath = Path.Combine(pluginRoot, DefaultAppConfigFileName);
        if (!File.Exists(configPath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!TryReadObject(document.RootElement, out var rootObject)
                || !TryGetProperty(rootObject, out var appsElement, "apps")
                || appsElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var ids = appsElement.EnumerateObject()
                .Select(static property => ReadString(property.Value, "id"))
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return ids;
        }
        catch
        {
            return [];
        }
    }

    private static bool TryReadPluginManifest(string pluginRoot, out KernelParsedPluginManifest manifest, out string? error)
    {
        manifest = null!;
        error = null;
        var manifestPath = Path.Combine(pluginRoot, PluginManifestRelativePath);
        if (!File.Exists(manifestPath))
        {
            error = $"missing or invalid .tianshu-plugin/plugin.json: {manifestPath}";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!TryReadObject(document.RootElement, out var rootObject))
            {
                error = $"missing or invalid .tianshu-plugin/plugin.json: {manifestPath}";
                return false;
            }

            var manifestName = ReadString(rootObject, "name");
            if (string.IsNullOrWhiteSpace(manifestName))
            {
                manifestName = Path.GetFileName(pluginRoot);
            }

            if (string.IsNullOrWhiteSpace(manifestName))
            {
                error = $"missing or invalid .tianshu-plugin/plugin.json: {manifestPath}";
                return false;
            }

            manifest = new KernelParsedPluginManifest(
                Name: manifestName!,
                Description: ReadString(rootObject, "description"),
                Interface: TryReadPluginInterface(rootObject, pluginRoot),
                SkillsRootPath: ResolvePluginContentPath(pluginRoot, ReadString(rootObject, "skills"), DefaultSkillsDirectoryName),
                McpConfigPath: ResolvePluginContentPath(pluginRoot, ReadString(rootObject, "mcpServers", "mcp_servers"), DefaultMcpConfigFileName),
                AppConfigPath: ResolvePluginContentPath(pluginRoot, ReadString(rootObject, "apps"), DefaultAppConfigFileName));
            return true;
        }
        catch
        {
            error = $"missing or invalid .tianshu-plugin/plugin.json: {manifestPath}";
            return false;
        }
    }

    private static KernelPluginInterfaceInfo? TryReadPluginInterface(JsonElement rootObject, string pluginRoot)
    {
        if (!TryGetProperty(rootObject, out var interfaceElement, "interface") || interfaceElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var capabilities = ReadStringArray(interfaceElement, "capabilities");
        var screenshots = ReadStringArray(interfaceElement, "screenshots")
            .Select(path => NormalizePath(ResolvePluginRelativePath(pluginRoot, path)))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .ToArray();
        var interfaceInfo = new KernelPluginInterfaceInfo(
            DisplayName: ReadString(interfaceElement, "displayName", "display_name"),
            ShortDescription: ReadString(interfaceElement, "shortDescription", "short_description"),
            LongDescription: ReadString(interfaceElement, "longDescription", "long_description"),
            DeveloperName: ReadString(interfaceElement, "developerName", "developer_name"),
            Category: ReadString(interfaceElement, "category"),
            Capabilities: capabilities,
            WebsiteUrl: ReadString(interfaceElement, "websiteUrl", "websiteURL", "website_url"),
            PrivacyPolicyUrl: ReadString(interfaceElement, "privacyPolicyUrl", "privacyPolicyURL", "privacy_policy_url"),
            TermsOfServiceUrl: ReadString(interfaceElement, "termsOfServiceUrl", "termsOfServiceURL", "terms_of_service_url"),
            DefaultPrompt: ReadString(interfaceElement, "defaultPrompt", "default_prompt"),
            BrandColor: ReadString(interfaceElement, "brandColor", "brand_color"),
            ComposerIcon: NormalizePath(ResolvePluginRelativePath(pluginRoot, ReadString(interfaceElement, "composerIcon", "composer_icon"))),
            Logo: NormalizePath(ResolvePluginRelativePath(pluginRoot, ReadString(interfaceElement, "logo"))),
            Screenshots: screenshots);
        return HasInterfaceContent(interfaceInfo) ? interfaceInfo : null;
    }

    private static bool HasInterfaceContent(KernelPluginInterfaceInfo interfaceInfo)
        => !string.IsNullOrWhiteSpace(interfaceInfo.DisplayName)
           || !string.IsNullOrWhiteSpace(interfaceInfo.ShortDescription)
           || !string.IsNullOrWhiteSpace(interfaceInfo.LongDescription)
           || !string.IsNullOrWhiteSpace(interfaceInfo.DeveloperName)
           || !string.IsNullOrWhiteSpace(interfaceInfo.Category)
           || interfaceInfo.Capabilities.Count > 0
           || !string.IsNullOrWhiteSpace(interfaceInfo.WebsiteUrl)
           || !string.IsNullOrWhiteSpace(interfaceInfo.PrivacyPolicyUrl)
           || !string.IsNullOrWhiteSpace(interfaceInfo.TermsOfServiceUrl)
           || !string.IsNullOrWhiteSpace(interfaceInfo.DefaultPrompt)
           || !string.IsNullOrWhiteSpace(interfaceInfo.BrandColor)
           || !string.IsNullOrWhiteSpace(interfaceInfo.ComposerIcon)
           || !string.IsNullOrWhiteSpace(interfaceInfo.Logo)
           || interfaceInfo.Screenshots.Count > 0;

    private static KernelPluginInterfaceInfo? ApplyMarketplaceCategory(KernelPluginInterfaceInfo? interfaceInfo, string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return interfaceInfo;
        }

        if (interfaceInfo is null)
        {
            return new KernelPluginInterfaceInfo(
                DisplayName: null,
                ShortDescription: null,
                LongDescription: null,
                DeveloperName: null,
                Category: category,
                Capabilities: [],
                WebsiteUrl: null,
                PrivacyPolicyUrl: null,
                TermsOfServiceUrl: null,
                DefaultPrompt: null,
                BrandColor: null,
                ComposerIcon: null,
                Logo: null,
                Screenshots: []);
        }

        return interfaceInfo with { Category = category };
    }

    private static IReadOnlyList<KernelPluginSkillSummary> LoadPluginSkillSummaries(string skillsRootPath, string pluginNamespace)
    {
        if (!Directory.Exists(skillsRootPath))
        {
            return [];
        }

        var skills = new List<KernelPluginSkillSummary>();
        foreach (var skillFile in Directory.EnumerateFiles(skillsRootPath, "SKILL.md", SearchOption.AllDirectories))
        {
            var skillDir = Path.GetDirectoryName(skillFile);
            if (string.IsNullOrWhiteSpace(skillDir))
            {
                continue;
            }

            var metadata = ParseSkillMetadata(skillFile, Path.GetFileName(skillDir));
            var name = string.IsNullOrWhiteSpace(pluginNamespace)
                ? metadata.Name
                : $"{pluginNamespace}:{metadata.Name}";
            skills.Add(new KernelPluginSkillSummary(
                Name: name,
                Description: metadata.Description,
                ShortDescription: metadata.ShortDescription,
                Interface: null,
                Path: Path.GetFullPath(skillDir)));
        }

        return skills
            .OrderBy(static skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static skill => skill.Path, PathComparer)
            .ToArray();
    }

    private static KernelParsedSkillMetadata ParseSkillMetadata(string skillFile, string fallbackName)
    {
        var lines = File.ReadAllLines(skillFile);
        var name = fallbackName;
        string? description = null;
        string? shortDescription = null;

        if (lines.Length > 0 && string.Equals(Normalize(lines[0]), "---", StringComparison.Ordinal))
        {
            for (var index = 1; index < lines.Length; index++)
            {
                var line = Normalize(lines[index]);
                if (string.Equals(line, "---", StringComparison.Ordinal))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = Normalize(line[..separatorIndex])?.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                var value = Normalize(line[(separatorIndex + 1)..]?.Trim().Trim('"', '\''));
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                switch (key)
                {
                    case "name":
                        name = value!;
                        break;
                    case "description":
                        description = value;
                        break;
                    case "shortdescription":
                        shortDescription = value;
                        break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = TryReadSkillFallbackDescription(lines) ?? $"Skill: {name}";
        }

        return new KernelParsedSkillMetadata(name, description!, shortDescription);
    }

    private static string? TryReadSkillFallbackDescription(IEnumerable<string> lines)
    {
        var inFrontMatter = false;
        foreach (var rawLine in lines)
        {
            var line = Normalize(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                inFrontMatter = !inFrontMatter;
                continue;
            }

            if (inFrontMatter || line.StartsWith('#'))
            {
                continue;
            }

            return line;
        }

        return null;
    }

    private static IReadOnlyList<KernelPluginAppSummary> LoadPluginAppSummaries(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!TryReadObject(document.RootElement, out var rootObject)
                || !TryGetProperty(rootObject, out var appsElement, "apps")
                || appsElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var apps = new List<KernelPluginAppSummary>();
            foreach (var property in appsElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = ReadString(property.Value, "id") ?? property.Name;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var name = ReadString(property.Value, "name") ?? id;
                apps.Add(new KernelPluginAppSummary(
                    Id: id,
                    Name: name!,
                    Description: ReadString(property.Value, "description"),
                    InstallUrl: ReadString(property.Value, "installUrl") ?? ReadString(property.Value, "install_url")));
            }

            return apps
                .OrderBy(static app => app.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> LoadPluginMcpServerNames(string pluginRoot, string configPath)
    {
        if (!File.Exists(configPath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!TryReadObject(document.RootElement, out var rootObject)
                || !TryGetProperty(rootObject, out var serversElement, "mcpServers", "mcp_servers")
                || serversElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return serversElement.EnumerateObject()
                .Select(static property => property.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private bool IsPluginInstalled(string pluginKey)
    {
        var plugin = ParsePluginKey(pluginKey, throwOnInvalid: false);
        if (plugin is null)
        {
            return false;
        }

        return ResolveInstalledPluginRoot(plugin.Value.PluginName, plugin.Value.MarketplaceName) is not null;
    }

    private static string ResolvePluginContentPath(string pluginRoot, string? configuredPath, string defaultRelativePath)
        => NormalizePath(ResolvePluginRelativePath(pluginRoot, configuredPath))
           ?? Path.GetFullPath(Path.Combine(pluginRoot, defaultRelativePath));

    private static string? ResolvePluginRelativePath(string pluginRoot, string? configuredPath)
    {
        var normalized = Normalize(configuredPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (!normalized!.StartsWith("./", StringComparison.Ordinal))
        {
            return null;
        }

        var relativePath = normalized[2..];
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var resolved = Path.GetFullPath(Path.Combine(pluginRoot, relativePath));
        return IsPathWithinRoot(resolved, pluginRoot) ? resolved : null;
    }

    private KernelPluginMarketplaceSummary BuildMarketplaceSummary(
        string marketplacePath,
        IReadOnlyDictionary<string, bool> configuredStates,
        PluginMarketplaceTrustPolicy trustPolicy,
        ISet<string> seenPluginKeys)
    {
        using var document = LoadMarketplaceDocument(marketplacePath);
        if (!TryReadObject(document.RootElement, out var marketplaceObject))
        {
            throw new KernelPluginInstallException($"invalid marketplace file {marketplacePath}: root must be an object", invalidRequest: true);
        }

        var marketplaceName = ReadString(marketplaceObject, "name");
        if (string.IsNullOrWhiteSpace(marketplaceName))
        {
            throw new KernelPluginInstallException($"invalid marketplace file {marketplacePath}: missing marketplace name", invalidRequest: true);
        }

        if (!TryGetProperty(marketplaceObject, out var pluginsElement, "plugins") || pluginsElement.ValueKind != JsonValueKind.Array)
        {
            throw new KernelPluginInstallException($"invalid marketplace file {marketplacePath}: missing plugins array", invalidRequest: true);
        }

        var plugins = new List<KernelPluginMarketplacePluginSummary>();
        foreach (var pluginElement in pluginsElement.EnumerateArray())
        {
            var pluginName = ReadString(pluginElement, "name");
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new KernelPluginInstallException($"invalid marketplace file {marketplacePath}: missing plugin name", invalidRequest: true);
            }

            var pluginKey = $"{pluginName}@{marketplaceName}";
            if (!seenPluginKeys.Add(pluginKey))
            {
                continue;
            }

            var resolved = CreateMarketplacePluginResolution(marketplacePath, marketplaceName!, pluginElement);
            var enabled = configuredStates.TryGetValue(pluginKey, out var configuredEnabled) && configuredEnabled;
            plugins.Add(new KernelPluginMarketplacePluginSummary(
                Id: pluginKey,
                Name: pluginName!,
                Source: BuildPluginSourceInfo(resolved, trustPolicy),
                Installed: IsPluginInstalled(pluginKey),
                Enabled: enabled,
                InstallPolicy: resolved.InstallPolicy,
                AuthPolicy: resolved.AuthPolicy,
                Interface: resolved.Interface));
        }

        return new KernelPluginMarketplaceSummary(marketplaceName!, marketplacePath, plugins);
    }

    private MarketplacePluginResolution ResolveMarketplacePluginFromPath(string marketplacePath, string pluginName)
    {
        using var document = LoadMarketplaceDocument(marketplacePath);
        if (!TryReadObject(document.RootElement, out var marketplaceObject))
        {
            throw new KernelPluginInstallException($"invalid marketplace file {marketplacePath}: root must be an object", invalidRequest: true);
        }

        var marketplaceName = ReadString(marketplaceObject, "name");
        if (string.IsNullOrWhiteSpace(marketplaceName))
        {
            throw new KernelPluginInstallException($"invalid marketplace file {marketplacePath}: missing marketplace name", invalidRequest: true);
        }

        if (!TryGetProperty(marketplaceObject, out var pluginsElement, "plugins") || pluginsElement.ValueKind != JsonValueKind.Array)
        {
            throw new KernelPluginInstallException($"invalid marketplace file {marketplacePath}: missing plugins array", invalidRequest: true);
        }

        var matches = pluginsElement.EnumerateArray()
            .Where(item => string.Equals(ReadString(item, "name"), pluginName, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length > 1)
        {
            throw new KernelPluginInstallException($"multiple marketplace plugin entries matched {pluginName} in marketplace {marketplaceName}", invalidRequest: true);
        }

        if (matches.Length == 1)
        {
            return CreateMarketplacePluginResolution(marketplacePath, marketplaceName!, matches[0]);
        }

        throw new KernelPluginInstallException($"plugin `{pluginName}` was not found in marketplace `{marketplaceName}`", invalidRequest: true);
    }
    private MarketplacePluginResolution ResolveMarketplacePlugin(string cwd, string pluginName, string marketplaceName)
    {
        foreach (var marketplacePath in DiscoverMarketplacePaths(cwd))
        {
            using var document = LoadMarketplaceDocument(marketplacePath);
            if (!TryReadObject(document.RootElement, out var marketplaceObject))
            {
                throw new KernelPluginInstallException($"invalid marketplace file `{marketplacePath}`: root must be an object", invalidRequest: true);
            }

            var discoveredMarketplaceName = ReadString(marketplaceObject, "name");
            if (string.IsNullOrWhiteSpace(discoveredMarketplaceName))
            {
                throw new KernelPluginInstallException($"invalid marketplace file `{marketplacePath}`: missing marketplace name", invalidRequest: true);
            }

            if (!TryGetProperty(marketplaceObject, out var pluginsElement, "plugins") || pluginsElement.ValueKind != JsonValueKind.Array)
            {
                throw new KernelPluginInstallException($"invalid marketplace file `{marketplacePath}`: missing plugins array", invalidRequest: true);
            }

            if (!string.Equals(discoveredMarketplaceName, marketplaceName, StringComparison.Ordinal))
            {
                continue;
            }

            var matches = pluginsElement.EnumerateArray()
                .Where(item => string.Equals(ReadString(item, "name"), pluginName, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length > 1)
            {
                throw new KernelPluginInstallException($"multiple marketplace plugin entries matched `{pluginName}` in marketplace `{marketplaceName}`", invalidRequest: true);
            }

            if (matches.Length == 1)
            {
                return CreateMarketplacePluginResolution(marketplacePath, marketplaceName, matches[0]);
            }
        }

        throw new KernelPluginInstallException($"plugin `{pluginName}` was not found in marketplace `{marketplaceName}`", invalidRequest: true);
    }

    private static JsonDocument LoadMarketplaceDocument(string marketplacePath)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(marketplacePath));
        }
        catch (Exception ex)
        {
            throw new KernelPluginInstallException($"invalid marketplace file `{marketplacePath}`: {ex.Message}", invalidRequest: true, ex);
        }
    }

    private static MarketplacePluginSource ResolveMarketplacePluginSource(string marketplacePath, JsonElement pluginElement)
    {
        if (pluginElement.ValueKind != JsonValueKind.Object
            || !TryGetProperty(pluginElement, out var sourceElement, "source")
            || sourceElement.ValueKind != JsonValueKind.Object)
        {
            throw new KernelPluginInstallException($"invalid marketplace file `{marketplacePath}`: plugin source is missing", invalidRequest: true);
        }

        var sourceType = Normalize(ReadString(sourceElement, "source")) ?? "local";
        if (!string.Equals(sourceType, "local", StringComparison.Ordinal)
            && !string.Equals(sourceType, "archive", StringComparison.Ordinal)
            && !string.Equals(sourceType, "remote_archive", StringComparison.Ordinal))
        {
            throw new KernelPluginInstallException($"invalid marketplace file `{marketplacePath}`: only local, archive, and remote_archive plugin sources are supported", invalidRequest: true);
        }

        if (string.Equals(sourceType, "remote_archive", StringComparison.Ordinal))
        {
            var url = Normalize(ReadString(sourceElement, "url"));
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new KernelPluginInstallException($"invalid marketplace file `{marketplacePath}`: remote archive source url is missing", invalidRequest: true);
            }

            return new MarketplacePluginSource(sourceType, url!);
        }

        var relativePath = ReadString(sourceElement, "path");
        if (string.IsNullOrWhiteSpace(relativePath) || !relativePath.StartsWith("./", StringComparison.Ordinal))
        {
            throw new KernelPluginInstallException($"invalid marketplace file `{marketplacePath}`: plugin source path must start with `./`", invalidRequest: true);
        }

        var marketplaceDirectory = Path.GetDirectoryName(marketplacePath) ?? throw new KernelPluginInstallException("failed to resolve marketplace directory", invalidRequest: true);
        var resolvedPath = Path.GetFullPath(Path.Combine(marketplaceDirectory, relativePath[2..]));
        if (!IsPathWithinRoot(resolvedPath, marketplaceDirectory))
        {
            throw new KernelPluginInstallException($"invalid marketplace file `{marketplacePath}`: plugin source path escapes the marketplace directory", invalidRequest: true);
        }

        return new MarketplacePluginSource(sourceType, resolvedPath);
    }

    private MarketplacePluginResolution CreateMarketplacePluginResolution(string marketplacePath, string marketplaceName, JsonElement pluginElement)
    {
        var pluginName = ReadString(pluginElement, "name")
            ?? throw new KernelPluginInstallException($"invalid marketplace file `{marketplacePath}`: missing plugin name", invalidRequest: true);
        var source = ResolveMarketplacePluginSource(marketplacePath, pluginElement);
        var expectedSha256 = ReadMarketplaceSha256(pluginElement);
        var signer = ReadMarketplaceSigner(pluginElement);
        var signature = ReadMarketplaceSignature(pluginElement);
        KernelPluginInterfaceInfo? interfaceInfo = null;
        if (string.Equals(source.SourceType, "local", StringComparison.Ordinal)
            && TryReadPluginManifest(source.SourcePath, out var manifest, out _))
        {
            interfaceInfo = ApplyMarketplaceCategory(manifest.Interface, ReadString(pluginElement, "category"));
        }

        return new MarketplacePluginResolution(
            PluginKey: $"{pluginName}@{marketplaceName}",
            PluginName: pluginName,
            SourceType: source.SourceType,
            SourcePath: source.SourcePath,
            MarketplaceName: marketplaceName,
            InstallPolicy: Normalize(ReadString(pluginElement, "installPolicy", "install_policy")) ?? "AVAILABLE",
            AuthPolicy: Normalize(ReadString(pluginElement, "authPolicy", "auth_policy")) ?? "ON_INSTALL",
            Category: ReadString(pluginElement, "category"),
            Interface: interfaceInfo,
            ExpectedSha256: expectedSha256,
            Signer: signer,
            Signature: signature);
    }

    private static string? ReadMarketplaceSha256(JsonElement pluginElement)
    {
        var direct = Normalize(ReadString(pluginElement, "sha256", "checksumSha256", "checksum_sha256"));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (pluginElement.ValueKind == JsonValueKind.Object
            && pluginElement.TryGetProperty("integrity", out var integrity)
            && integrity.ValueKind == JsonValueKind.Object)
        {
            return Normalize(ReadString(integrity, "sha256"));
        }

        return null;
    }

    private static string? ReadMarketplaceSigner(JsonElement pluginElement)
    {
        var direct = Normalize(ReadString(pluginElement, "signer", "signerId", "signer_id"));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (pluginElement.ValueKind == JsonValueKind.Object
            && pluginElement.TryGetProperty("integrity", out var integrity)
            && integrity.ValueKind == JsonValueKind.Object)
        {
            return Normalize(ReadString(integrity, "signer", "signerId", "signer_id"));
        }

        return null;
    }

    private static MarketplacePluginSignature ReadMarketplaceSignature(JsonElement pluginElement)
    {
        if (pluginElement.ValueKind != JsonValueKind.Object
            || !pluginElement.TryGetProperty("integrity", out var integrity)
            || integrity.ValueKind != JsonValueKind.Object)
        {
            return new MarketplacePluginSignature(
                null,
                null,
                null,
                Array.Empty<string>(),
                false,
                new MarketplacePluginTransparencyLog(false, null, null, null, null, null, EmptyTransparencyCheckpoint, Array.Empty<MarketplacePluginTransparencyProofNode>()),
                new MarketplacePluginRevocationCheck(false, false, Array.Empty<string>(), false, Array.Empty<string>(), false, null, null, false, null, null));
        }

        var certificateChain = ReadMarketplaceCertificateChain(integrity, out var certificateChainDeclared);
        return new MarketplacePluginSignature(
            Algorithm: Normalize(ReadString(integrity, "signature_algorithm", "signatureAlgorithm")),
            PublicKey: Normalize(ReadString(integrity, "public_key", "publicKey")),
            Signature: Normalize(ReadString(integrity, "signature")),
            CertificateChain: certificateChain,
            CertificateChainDeclared: certificateChainDeclared,
            TransparencyLog: ReadMarketplaceTransparencyLog(integrity),
            RevocationCheck: ReadMarketplaceRevocationCheck(integrity));
    }

    private static MarketplacePluginTransparencyLog ReadMarketplaceTransparencyLog(JsonElement integrity)
    {
        if (!TryGetProperty(integrity, out var proofElement, "transparency_log", "transparencyLog", "rekor_proof", "rekorProof"))
        {
            return new MarketplacePluginTransparencyLog(false, null, null, null, null, null, EmptyTransparencyCheckpoint, Array.Empty<MarketplacePluginTransparencyProofNode>());
        }

        if (proofElement.ValueKind == JsonValueKind.String)
        {
            return new MarketplacePluginTransparencyLog(true, null, Normalize(proofElement.GetString()), null, null, null, EmptyTransparencyCheckpoint, Array.Empty<MarketplacePluginTransparencyProofNode>());
        }

        if (proofElement.ValueKind != JsonValueKind.Object)
        {
            return new MarketplacePluginTransparencyLog(true, null, null, null, null, null, EmptyTransparencyCheckpoint, Array.Empty<MarketplacePluginTransparencyProofNode>());
        }

        return new MarketplacePluginTransparencyLog(
            true,
            Normalize(ReadString(proofElement, "kind")),
            Normalize(ReadString(proofElement, "log_id", "logId", "log", "id")),
            ReadLong(proofElement, "log_index", "logIndex"),
            ReadLong(proofElement, "tree_size", "treeSize"),
            Normalize(ReadString(proofElement, "root_hash", "rootHash")),
            ReadTransparencyCheckpoint(proofElement),
            ReadTransparencyInclusionProof(proofElement));
    }

    private static MarketplacePluginTransparencyCheckpoint EmptyTransparencyCheckpoint { get; } =
        new(false, false, null, null, null, null, null);

    private static MarketplacePluginTransparencyCheckpoint ReadTransparencyCheckpoint(JsonElement proofElement)
    {
        if (!TryGetProperty(proofElement, out var checkpointElement, "checkpoint"))
        {
            return EmptyTransparencyCheckpoint;
        }

        if (checkpointElement.ValueKind == JsonValueKind.String)
        {
            return new MarketplacePluginTransparencyCheckpoint(
                true,
                false,
                Normalize(checkpointElement.GetString()),
                null,
                null,
                null,
                null);
        }

        if (checkpointElement.ValueKind != JsonValueKind.Object)
        {
            return new MarketplacePluginTransparencyCheckpoint(true, false, null, null, null, null, null);
        }

        return new MarketplacePluginTransparencyCheckpoint(
            true,
            true,
            Normalize(ReadString(checkpointElement, "text", "value", "body", "checkpoint")),
            Normalize(ReadString(checkpointElement, "origin", "log", "log_id", "logId")),
            ReadLong(checkpointElement, "tree_size", "treeSize", "size"),
            Normalize(ReadString(checkpointElement, "root_hash", "rootHash", "hash")),
            Normalize(ReadString(checkpointElement, "signature", "signed_checkpoint", "signedCheckpoint", "signed")));
    }

    private static IReadOnlyList<MarketplacePluginTransparencyProofNode> ReadTransparencyInclusionProof(JsonElement proofElement)
    {
        if (!TryGetProperty(proofElement, out var inclusionProofElement, "inclusion_proof", "inclusionProof")
            || inclusionProofElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MarketplacePluginTransparencyProofNode>();
        }

        var nodes = new List<MarketplacePluginTransparencyProofNode>();
        foreach (var item in inclusionProofElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                nodes.Add(new MarketplacePluginTransparencyProofNode(false, Normalize(item.GetString()), null, null));
                continue;
            }

            if (item.ValueKind == JsonValueKind.Object)
            {
                nodes.Add(new MarketplacePluginTransparencyProofNode(
                    true,
                    Normalize(ReadString(item, "text", "value", "body", "node")),
                    Normalize(ReadString(item, "hash", "sha256", "root_hash", "rootHash")),
                    Normalize(ReadString(item, "position", "side", "direction"))));
                continue;
            }

            nodes.Add(new MarketplacePluginTransparencyProofNode(false, null, null, null));
        }

        return nodes;
    }

    private static MarketplacePluginRevocationCheck ReadMarketplaceRevocationCheck(JsonElement integrity)
    {
        var ocspResponses = ReadRevocationMaterials(integrity, out var ocspDeclared, "ocsp_response", "ocspResponse", "ocsp");
        var crls = ReadRevocationMaterials(integrity, out var crlDeclared, "crl", "crl_set", "crlSet");

        var proofDeclared = TryGetProperty(integrity, out var proofElement, "revocation_proof", "revocationProof");
        var statusDeclared = TryGetProperty(integrity, out var statusElement, "revocation_status", "revocationStatus");
        var proofKind = ReadRevocationEnvelopeString(proofDeclared ? proofElement : default, "kind", "type");
        var proof = ReadRevocationEnvelopeString(proofDeclared ? proofElement : default, "proof", "value", "payload", "data");
        var status = ReadRevocationEnvelopeString(statusDeclared ? statusElement : default, "status", "value", "state");
        var checkedAt = ReadRevocationEnvelopeString(statusDeclared ? statusElement : default, "checked_at", "checkedAt", "produced_at", "producedAt");

        return new MarketplacePluginRevocationCheck(
            ocspDeclared || crlDeclared || proofDeclared || statusDeclared,
            ocspDeclared,
            ocspResponses,
            crlDeclared,
            crls,
            proofDeclared,
            proofKind,
            proof,
            statusDeclared,
            status,
            checkedAt);
    }

    private static IReadOnlyList<string> ReadRevocationMaterials(JsonElement integrity, out bool declared, params string[] names)
    {
        declared = false;
        if (!TryGetProperty(integrity, out var materialElement, names))
        {
            return Array.Empty<string>();
        }

        declared = true;
        if (materialElement.ValueKind == JsonValueKind.Array)
        {
            return materialElement.EnumerateArray()
                .Select(ExtractRevocationMaterial)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .ToList();
        }

        var material = ExtractRevocationMaterial(materialElement);
        return string.IsNullOrWhiteSpace(material) ? Array.Empty<string>() : [material!];
    }

    private static string? ExtractRevocationMaterial(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return Normalize(element.GetString());
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            return Normalize(ReadString(element, "value", "body", "response", "data", "payload", "sha256", "url", "uri"));
        }

        return element.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
            ? Normalize(element.GetRawText())
            : null;
    }

    private static string? ReadRevocationEnvelopeString(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return Normalize(element.GetString());
        }

        return element.ValueKind == JsonValueKind.Object
            ? Normalize(ReadString(element, names))
            : null;
    }


    private static IReadOnlyList<string> ReadMarketplaceCertificateChain(JsonElement integrity, out bool declared)
    {
        declared = false;
        if (!TryGetProperty(integrity, out var chainElement, "certificate_chain", "certificateChain"))
        {
            return Array.Empty<string>();
        }

        declared = true;
        if (chainElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in chainElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                values.Add(string.Empty);
                continue;
            }

            values.Add(Normalize(item.GetString()) ?? string.Empty);
        }

        return values;
    }

    private static KernelPluginSourceInfo BuildPluginSourceInfo(MarketplacePluginResolution resolution, PluginMarketplaceTrustPolicy trustPolicy)
    {
        var expectedSha256 = Normalize(resolution.ExpectedSha256);
        var signer = Normalize(resolution.Signer);
        var trustStatus = ResolveSignerTrustStatus(signer, trustPolicy);
        var signatureStatus = ResolveSignatureStatus(
            marketplaceName: resolution.MarketplaceName,
            pluginName: resolution.PluginName,
            expectedSha256: expectedSha256,
            signer: signer,
            signature: resolution.Signature,
            trustPolicy: trustPolicy);
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return new KernelPluginSourceInfo(resolution.SourceType, resolution.SourcePath, Signer: signer, TrustStatus: trustStatus, SignatureStatus: signatureStatus);
        }

        if (!MarketplaceSourceExists(resolution.SourceType, resolution.SourcePath))
        {
            return new KernelPluginSourceInfo(resolution.SourceType, resolution.SourcePath, expectedSha256, "source-missing", signer, trustStatus, signatureStatus);
        }

        if (string.Equals(resolution.SourceType, "archive", StringComparison.Ordinal)
            || string.Equals(resolution.SourceType, "remote_archive", StringComparison.Ordinal))
        {
            return new KernelPluginSourceInfo(resolution.SourceType, resolution.SourcePath, expectedSha256, "deferred", signer, trustStatus, signatureStatus);
        }

        var actualSha256 = ComputePluginDirectorySha256(resolution.SourcePath);
        var status = string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase)
            ? "verified"
            : "mismatch";
        return new KernelPluginSourceInfo(resolution.SourceType, resolution.SourcePath, expectedSha256, status, signer, trustStatus, signatureStatus);
    }

    private static bool MarketplaceSourceExists(string sourceType, string sourcePath)
    {
        if (string.Equals(sourceType, "archive", StringComparison.Ordinal))
        {
            return File.Exists(sourcePath);
        }

        if (string.Equals(sourceType, "remote_archive", StringComparison.Ordinal))
        {
            return IsValidRemoteArchiveUri(sourcePath);
        }

        return Directory.Exists(sourcePath);
    }

    private static string ResolveSignerTrustStatus(string? signer, PluginMarketplaceTrustPolicy trustPolicy)
    {
        var normalizedSigner = Normalize(signer);
        if (string.IsNullOrWhiteSpace(normalizedSigner))
        {
            return trustPolicy.RequireSigner ? "missing-required" : "not-declared";
        }

        return trustPolicy.TrustedSigners.Contains(normalizedSigner!)
            ? "trusted"
            : "untrusted";
    }

    private static string ResolveSignatureStatus(
        string marketplaceName,
        string pluginName,
        string? expectedSha256,
        string? signer,
        MarketplacePluginSignature? signature,
        PluginMarketplaceTrustPolicy trustPolicy)
    {
        if (signature is null || !signature.IsDeclared)
        {
            return "not-declared";
        }

        var normalizedSigner = Normalize(signer);
        if (string.IsNullOrWhiteSpace(normalizedSigner))
        {
            return "missing-signer";
        }

        if (!trustPolicy.TrustedSigners.Contains(normalizedSigner!))
        {
            return "untrusted-signer";
        }

        var normalizedSha = Normalize(expectedSha256);
        if (string.IsNullOrWhiteSpace(normalizedSha))
        {
            return "missing-sha256";
        }

        if (signature.TransparencyLog.Declared)
        {
            return ResolveTransparencyLogStatus(signature.TransparencyLog, trustPolicy);
        }

        if (signature.RevocationCheck.Declared)
        {
            return ResolveRevocationCheckStatus(signature.RevocationCheck, signature.CertificateChainDeclared);
        }

        var algorithm = Normalize(signature.Algorithm);
        var rawSignature = Normalize(signature.Signature);
        if (string.IsNullOrWhiteSpace(algorithm)
            || string.IsNullOrWhiteSpace(rawSignature))
        {
            return "incomplete";
        }

        if (!string.Equals(algorithm, "ecdsa-p256-sha256", StringComparison.OrdinalIgnoreCase))
        {
            return "unsupported-algorithm";
        }

        if (!trustPolicy.Signers.TryGetValue(normalizedSigner!, out var signerTrust)
            || string.IsNullOrWhiteSpace(signerTrust.PublicKeySha256))
        {
            return "missing-public-key-pin";
        }

        if (signature.CertificateChainDeclared)
        {
            return VerifyEcdsaP256CertificateChainSignature(
                marketplaceName,
                pluginName,
                normalizedSigner!,
                normalizedSha!,
                signature.CertificateChain,
                rawSignature!,
                signerTrust.PublicKeySha256!,
                trustPolicy.CertificateAuthorities);
        }

        var publicKey = Normalize(signature.PublicKey) ?? Normalize(signerTrust.PublicKey);
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return "missing-public-key";
        }

        return VerifyEcdsaP256Signature(
            marketplaceName,
            pluginName,
            normalizedSigner!,
            normalizedSha!,
            publicKey!,
            rawSignature!,
            signerTrust.PublicKeySha256!);
    }

    private static string ResolveTransparencyLogStatus(
        MarketplacePluginTransparencyLog transparencyLog,
        PluginMarketplaceTrustPolicy trustPolicy)
    {
        var logId = Normalize(transparencyLog.LogId);
        if (string.IsNullOrWhiteSpace(logId))
        {
            return "transparency-log-unsupported";
        }

        if (!trustPolicy.TransparencyLogs.TryGetValue(logId!, out var logTrust)
            || !logTrust.Enabled)
        {
            return "transparency-log-untrusted";
        }

        if (string.IsNullOrWhiteSpace(Normalize(logTrust.PublicKeySha256)))
        {
            return "transparency-log-missing-public-key-pin";
        }

        var publicKeyStatus = ResolveTransparencyLogPublicKeyStatus(logTrust);
        if (publicKeyStatus is not null)
        {
            return publicKeyStatus;
        }

        if (IsTransparencyLogCheckpointEnvelopeIncomplete(transparencyLog))
        {
            return "transparency-log-checkpoint-incomplete";
        }

        if (IsTransparencyLogInclusionProofEnvelopeIncomplete(transparencyLog))
        {
            return "transparency-log-inclusion-proof-incomplete";
        }

        if (IsTransparencyLogProofRangeInvalid(transparencyLog))
        {
            return "transparency-log-proof-range-invalid";
        }

        if (!IsTransparencyLogProofComplete(transparencyLog))
        {
            return "transparency-log-proof-incomplete";
        }

        return "transparency-log-unsupported";
    }

    private static string ResolveRevocationCheckStatus(
        MarketplacePluginRevocationCheck revocationCheck,
        bool certificateChainDeclared)
    {
        if (!certificateChainDeclared)
        {
            return "revocation-check-missing-certificate-chain";
        }

        return IsRevocationCheckEnvelopeComplete(revocationCheck)
            ? "revocation-check-unsupported"
            : "revocation-check-envelope-incomplete";
    }

    private static bool IsRevocationCheckEnvelopeComplete(MarketplacePluginRevocationCheck revocationCheck)
    {
        if (revocationCheck.OcspDeclared && revocationCheck.OcspResponses.Count == 0)
        {
            return false;
        }

        if (revocationCheck.CrlDeclared && revocationCheck.Crls.Count == 0)
        {
            return false;
        }

        if (revocationCheck.ProofDeclared
            && (string.IsNullOrWhiteSpace(Normalize(revocationCheck.ProofKind))
                || string.IsNullOrWhiteSpace(Normalize(revocationCheck.Proof))))
        {
            return false;
        }

        return !revocationCheck.StatusDeclared
            || (!string.IsNullOrWhiteSpace(Normalize(revocationCheck.Status))
                && !string.IsNullOrWhiteSpace(Normalize(revocationCheck.CheckedAt)));
    }

    private static string? ResolveTransparencyLogPublicKeyStatus(PluginMarketplaceTransparencyLogTrust logTrust)
    {
        var publicKey = Normalize(logTrust.PublicKey);
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return null;
        }

        byte[] publicKeyBytes;
        try
        {
            publicKeyBytes = Convert.FromBase64String(publicKey!);
        }
        catch (FormatException)
        {
            return "transparency-log-invalid-public-key";
        }

        var actualPublicKeySha256 = Convert.ToHexString(SHA256.HashData(publicKeyBytes)).ToLowerInvariant();
        return string.Equals(actualPublicKeySha256, Normalize(logTrust.PublicKeySha256), StringComparison.OrdinalIgnoreCase)
            ? null
            : "transparency-log-public-key-mismatch";
    }

    private static bool IsTransparencyLogProofComplete(MarketplacePluginTransparencyLog transparencyLog)
        => transparencyLog.LogIndex is >= 0
            && transparencyLog.TreeSize is > 0
            && !string.IsNullOrWhiteSpace(Normalize(transparencyLog.RootHash))
            && IsTransparencyLogCheckpointPresent(transparencyLog.Checkpoint)
            && transparencyLog.InclusionProof.Count > 0
            && transparencyLog.InclusionProof.All(IsTransparencyLogInclusionProofNodePresent);

    private static bool IsTransparencyLogProofRangeInvalid(MarketplacePluginTransparencyLog transparencyLog)
        => transparencyLog.LogIndex is < 0
            || transparencyLog.TreeSize is <= 0
            || (transparencyLog.LogIndex.HasValue
                && transparencyLog.TreeSize.HasValue
                && transparencyLog.LogIndex.Value >= transparencyLog.TreeSize.Value);

    private static bool IsTransparencyLogCheckpointPresent(MarketplacePluginTransparencyCheckpoint checkpoint)
        => !string.IsNullOrWhiteSpace(Normalize(checkpoint.Text))
            || checkpoint.EnvelopeDeclared;

    private static bool IsTransparencyLogInclusionProofNodePresent(MarketplacePluginTransparencyProofNode node)
        => !string.IsNullOrWhiteSpace(Normalize(node.Text))
            || node.EnvelopeDeclared;

    private static bool IsTransparencyLogCheckpointEnvelopeIncomplete(MarketplacePluginTransparencyLog transparencyLog)
    {
        var checkpoint = transparencyLog.Checkpoint;
        return checkpoint.EnvelopeDeclared
            && (string.IsNullOrWhiteSpace(Normalize(checkpoint.Origin))
                || checkpoint.TreeSize is not > 0
                || string.IsNullOrWhiteSpace(Normalize(checkpoint.RootHash))
                || string.IsNullOrWhiteSpace(Normalize(checkpoint.Signature)));
    }

    private static bool IsTransparencyLogInclusionProofEnvelopeIncomplete(MarketplacePluginTransparencyLog transparencyLog)
        => transparencyLog.InclusionProof.Any(static node =>
            node.EnvelopeDeclared
            && (string.IsNullOrWhiteSpace(Normalize(node.Hash))
                || string.IsNullOrWhiteSpace(Normalize(node.Position))));

    private string InstallResolvedMarketplacePlugin(
        MarketplacePluginResolution resolution,
        string? pluginVersion,
        PluginMarketplaceTrustPolicy trustPolicy,
        CancellationToken cancellationToken)
    {
        if (string.Equals(resolution.SourceType, "local", StringComparison.Ordinal))
        {
            return InstallToStore(
                resolution.SourcePath,
                resolution.PluginName,
                resolution.MarketplaceName,
                cancellationToken,
                pluginVersion,
                resolution.ExpectedSha256,
                resolution.Signer,
                resolution.Signature,
                trustPolicy);
        }

        if (string.Equals(resolution.SourceType, "remote_archive", StringComparison.Ordinal))
        {
            return InstallRemoteArchiveMarketplacePlugin(resolution, pluginVersion, trustPolicy, cancellationToken);
        }

        if (!string.Equals(resolution.SourceType, "archive", StringComparison.Ordinal))
        {
            throw new KernelPluginInstallException($"unsupported marketplace plugin source `{resolution.SourceType}`", invalidRequest: true);
        }

        var extractionRoot = CreateArchiveExtractionRoot(resolution.PluginName, resolution.MarketplaceName);
        try
        {
            ExtractMarketplaceArchive(resolution.SourcePath, extractionRoot, cancellationToken);
            var pluginRoot = ResolveExtractedPluginRoot(extractionRoot, resolution.PluginName);
            return InstallToStore(
                pluginRoot,
                resolution.PluginName,
                resolution.MarketplaceName,
                cancellationToken,
                pluginVersion,
                resolution.ExpectedSha256,
                resolution.Signer,
                resolution.Signature,
                trustPolicy);
        }
        finally
        {
            RemoveExistingTarget(extractionRoot);
        }
    }

    private string InstallRemoteArchiveMarketplacePlugin(
        MarketplacePluginResolution resolution,
        string? pluginVersion,
        PluginMarketplaceTrustPolicy trustPolicy,
        CancellationToken cancellationToken)
    {
        VerifyRemoteArchiveInstallPolicy(resolution, trustPolicy);
        var extractionRoot = CreateArchiveExtractionRoot(resolution.PluginName, resolution.MarketplaceName);
        var archivePath = Path.Combine(extractionRoot, "remote.zip");
        var extractedRoot = Path.Combine(extractionRoot, "extracted");
        try
        {
            DownloadRemoteArchive(resolution.SourcePath, archivePath, trustPolicy.RemoteArchiveMaxBytes, cancellationToken);
            ExtractMarketplaceArchive(archivePath, extractedRoot, cancellationToken);
            var pluginRoot = ResolveExtractedPluginRoot(extractedRoot, resolution.PluginName);
            return InstallToStore(
                pluginRoot,
                resolution.PluginName,
                resolution.MarketplaceName,
                cancellationToken,
                pluginVersion,
                resolution.ExpectedSha256,
                resolution.Signer,
                resolution.Signature,
                trustPolicy);
        }
        finally
        {
            RemoveExistingTarget(extractionRoot);
        }
    }

    private string InstallToStore(
        string sourcePath,
        string pluginName,
        string marketplaceName,
        CancellationToken cancellationToken,
        string? pluginVersion = null,
        string? expectedSha256 = null,
        string? signer = null,
        MarketplacePluginSignature? signature = null,
        PluginMarketplaceTrustPolicy? trustPolicy = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedSourcePath = NormalizePath(sourcePath);
        if (normalizedSourcePath is null || !Directory.Exists(normalizedSourcePath))
        {
            throw new KernelPluginInstallException($"plugin source path is not a directory: {sourcePath}", invalidRequest: true);
        }

        if (!TryReadManifestName(normalizedSourcePath, out var manifestName, out var manifestError))
        {
            throw new KernelPluginInstallException(manifestError ?? $"missing plugin manifest: {Path.Combine(normalizedSourcePath, PluginManifestRelativePath)}", invalidRequest: true);
        }

        if (!string.Equals(manifestName, pluginName, StringComparison.Ordinal))
        {
            throw new KernelPluginInstallException($"plugin manifest name `{manifestName}` does not match marketplace plugin name `{pluginName}`", invalidRequest: true);
        }

        VerifyPluginDirectoryIntegrity(normalizedSourcePath, expectedSha256);
        var effectiveTrustPolicy = trustPolicy ?? new PluginMarketplaceTrustPolicy(
            false,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, PluginMarketplaceSignerTrust>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, PluginMarketplaceCertificateAuthorityTrust>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, PluginMarketplaceTransparencyLogTrust>(StringComparer.OrdinalIgnoreCase),
            false,
            DefaultRemoteArchiveMaxBytes,
            false,
            DefaultRemoteMarketplaceMaxBytes);
        VerifyMarketplaceSignerTrust(signer, effectiveTrustPolicy);
        VerifyMarketplaceSignature(marketplaceName, pluginName, expectedSha256, signer, signature, effectiveTrustPolicy);

        var resolvedVersion = Normalize(pluginVersion) ?? DefaultPluginVersion;
        var installedPath = Path.Combine(GetPluginInstallRoot(pluginName, marketplaceName), resolvedVersion);
        try
        {
            var parent = Path.GetDirectoryName(installedPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent!);
            }

            RemoveExistingTarget(installedPath);
            CopyDirectoryRecursive(normalizedSourcePath, installedPath, cancellationToken);
            return installedPath;
        }
        catch (KernelPluginInstallException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KernelPluginInstallException($"failed to install plugin: {ex.Message}", invalidRequest: false, ex);
        }
    }

    private void PersistEnabledPluginConfig(string pluginKey, string? cwd)
    {
        var configTomlPath = TianShuConfigTomlPathResolver.ResolveWritableProjectConfigTomlPath(cwd);
        try
        {
            TomlTable root;
            if (File.Exists(configTomlPath))
            {
                root = Toml.ToModel(File.ReadAllText(configTomlPath)) as TomlTable
                    ?? throw new InvalidOperationException("tianshu.toml root must be a table");
            }
            else
            {
                root = new TomlTable();
            }

            var plugins = GetOrCreateTable(root, "plugins");
            var installed = GetOrCreateTable(plugins, "installed");
            var plugin = GetOrCreateTable(installed, pluginKey);
            plugin["enabled"] = true;

            Directory.CreateDirectory(Path.GetDirectoryName(configTomlPath)!);
            File.WriteAllText(configTomlPath, Toml.FromModel(root));
        }
        catch (KernelPluginInstallException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KernelPluginInstallException($"failed to persist installed plugin config: {ex.Message}", invalidRequest: false, ex);
        }
    }

    private Dictionary<string, bool> ReconcileCuratedRemotePluginStates(
        IReadOnlyDictionary<string, bool> configuredStates,
        IReadOnlyList<KernelRemotePluginState> remoteStates,
        PluginMarketplaceTrustPolicy trustPolicy,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, bool>(configuredStates, StringComparer.OrdinalIgnoreCase);
        var curatedMarketplace = LoadCuratedMarketplaceState();
        var curatedVersion = ReadCuratedPluginsSha();
        if (string.IsNullOrWhiteSpace(curatedVersion))
        {
            throw new InvalidOperationException("local curated marketplace sha is not available");
        }

        var localPluginNames = curatedMarketplace.Plugins
            .Select(static item => item.PluginName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remoteEnabledByName = BuildRemoteEnabledByName(
            remoteStates,
            curatedMarketplace.MarketplaceName,
            localPluginNames);

        var persistedStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var removedPluginKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in curatedMarketplace.Plugins)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (remoteEnabledByName.TryGetValue(plugin.PluginName, out var enabled))
            {
                if (!IsPluginInstalled(plugin.PluginKey))
                {
                    VerifyCuratedRemoteAutoInstallPolicy(plugin, curatedMarketplace.MarketplaceName, trustPolicy);
                    var resolution = new MarketplacePluginResolution(
                        plugin.PluginKey,
                        plugin.PluginName,
                        plugin.SourceType,
                        plugin.SourcePath,
                        curatedMarketplace.MarketplaceName,
                        ExpectedSha256: plugin.ExpectedSha256,
                        Signer: plugin.Signer,
                        Signature: plugin.Signature);
                    _ = InstallResolvedMarketplacePlugin(
                        resolution,
                        curatedVersion,
                        trustPolicy,
                        cancellationToken);
                }

                merged[plugin.PluginKey] = enabled;
                persistedStates[plugin.PluginKey] = enabled;
                continue;
            }

            RemoveInstalledPluginStore(plugin.PluginKey);
            _ = merged.Remove(plugin.PluginKey);
            removedPluginKeys.Add(plugin.PluginKey);
        }

        PersistHomePluginStates(persistedStates, removedPluginKeys);
        ClearCache();
        return merged;
    }

    private static void VerifyCuratedRemoteAutoInstallPolicy(
        CuratedMarketplacePluginEntry plugin,
        string marketplaceName,
        PluginMarketplaceTrustPolicy trustPolicy)
    {
        if (string.IsNullOrWhiteSpace(Normalize(plugin.ExpectedSha256)))
        {
            throw new KernelPluginInstallException(
                $"curated remote auto-install requires integrity.sha256 for plugin `{plugin.PluginName}`",
                invalidRequest: true);
        }

        if (string.IsNullOrWhiteSpace(Normalize(plugin.Signer)))
        {
            throw new KernelPluginInstallException(
                $"curated remote auto-install requires integrity.signer for plugin `{plugin.PluginName}`",
                invalidRequest: true);
        }

        if (plugin.Signature is null || !plugin.Signature.IsDeclared)
        {
            throw new KernelPluginInstallException(
                $"curated remote auto-install requires integrity.signature for plugin `{plugin.PluginName}`",
                invalidRequest: true);
        }

        VerifyMarketplaceSignerTrust(plugin.Signer, trustPolicy);
        VerifyMarketplaceSignature(
            marketplaceName,
            plugin.PluginName,
            plugin.ExpectedSha256,
            plugin.Signer,
            plugin.Signature,
            trustPolicy);
    }

    private void PersistHomePluginStates(
        IReadOnlyDictionary<string, bool> remoteStates,
        IReadOnlyCollection<string> removedPluginKeys)
    {
        if (remoteStates.Count == 0 && removedPluginKeys.Count == 0)
        {
            return;
        }

        var configTomlPath = GetConfigTomlPath();
        try
        {
            TomlTable root;
            if (File.Exists(configTomlPath))
            {
                root = Toml.ToModel(File.ReadAllText(configTomlPath)) as TomlTable
                    ?? throw new InvalidOperationException("tianshu.toml root must be a table");
            }
            else
            {
                root = new TomlTable();
            }

            var plugins = GetOrCreateTable(root, "plugins");
            var installed = GetOrCreateTable(plugins, "installed");
            foreach (var pluginKey in removedPluginKeys)
            {
                if (string.IsNullOrWhiteSpace(pluginKey))
                {
                    continue;
                }

                _ = installed.Remove(pluginKey);
                _ = plugins.Remove(pluginKey);
            }

            foreach (var state in remoteStates)
            {
                if (string.IsNullOrWhiteSpace(state.Key))
                {
                    continue;
                }

                var plugin = GetOrCreateTable(installed, state.Key);
                plugin["enabled"] = state.Value;
            }

            if (installed.Count == 0)
            {
                _ = plugins.Remove("installed");
            }

            if (plugins.Count == 0)
            {
                _ = root.Remove("plugins");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(configTomlPath)!);
            File.WriteAllText(configTomlPath, Toml.FromModel(root));
        }
        catch (KernelPluginInstallException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KernelPluginInstallException($"failed to persist remote plugin config: {ex.Message}", invalidRequest: false, ex);
        }
    }

    private void RemovePersistedPluginConfig(string pluginKey, string? cwd)
    {
        var configTomlPath = TianShuConfigTomlPathResolver.ResolveWritableProjectConfigTomlPath(cwd);
        if (!File.Exists(configTomlPath))
        {
            return;
        }

        try
        {
            var root = Toml.ToModel(File.ReadAllText(configTomlPath)) as TomlTable
                ?? throw new InvalidOperationException("tianshu.toml root must be a table");
            if (root.TryGetValue("plugins", out var pluginsValue) && pluginsValue is TomlTable pluginsTable)
            {
                if (pluginsTable.TryGetValue("installed", out var installedValue)
                    && installedValue is TomlTable installed)
                {
                    _ = installed.Remove(pluginKey);
                    if (installed.Count == 0)
                    {
                        _ = pluginsTable.Remove("installed");
                    }
                }

                _ = pluginsTable.Remove(pluginKey);
                if (pluginsTable.Count == 0)
                {
                    _ = root.Remove("plugins");
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(configTomlPath)!);
            File.WriteAllText(configTomlPath, Toml.FromModel(root));
        }
        catch (KernelPluginInstallException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KernelPluginInstallException($"failed to remove installed plugin config: {ex.Message}", invalidRequest: false, ex);
        }
    }

    private static TomlTable GetOrCreateTable(TomlTable parent, string key)
    {
        if (parent.TryGetValue(key, out var existing) && existing is TomlTable table)
        {
            return table;
        }

        var created = new TomlTable();
        parent[key] = created;
        return created;
    }

    private IEnumerable<string> DiscoverMarketplacePathsForList(IReadOnlyList<string>? cwds)
    {
        var paths = new List<string>();
        var curatedMarketplace = GetCuratedMarketplacePath();
        if (!string.IsNullOrWhiteSpace(curatedMarketplace) && File.Exists(curatedMarketplace))
        {
            paths.Add(curatedMarketplace!);
        }

        paths.AddRange(DiscoverRemoteMarketplaceCachePaths());

        var homeMarketplace = GetHomeMarketplacePath();
        if (!string.IsNullOrWhiteSpace(homeMarketplace) && File.Exists(homeMarketplace))
        {
            paths.Add(homeMarketplace!);
        }

        if (cwds is not null)
        {
            foreach (var cwd in cwds)
            {
                var gitRoot = FindGitRoot(cwd);
                if (string.IsNullOrWhiteSpace(gitRoot))
                {
                    continue;
                }

                var repoPath = Path.Combine(gitRoot!, MarketplaceRelativePath);
                if (File.Exists(repoPath))
                {
                    paths.Add(repoPath);
                }
            }
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private string? GetHomeMarketplacePath()
    {
        var normalizedHome = NormalizePath(homePath);
        return string.IsNullOrWhiteSpace(normalizedHome)
            ? null
            : Path.Combine(normalizedHome!, MarketplaceRelativePath);
    }

    private string? GetCuratedMarketplacePath()
    {
        var normalizedHome = NormalizePath(homePath);
        return string.IsNullOrWhiteSpace(normalizedHome)
            ? null
            : Path.Combine(normalizedHome!, CuratedMarketplaceRelativePath);
    }

    private IEnumerable<string> DiscoverRemoteMarketplaceCachePaths()
    {
        var cacheRoot = GetRemoteMarketplaceCacheRoot();
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(cacheRoot!, "marketplace.json", SearchOption.AllDirectories)
            .OrderBy(static path => path, PathComparer)
            .ToArray();
    }

    private string? GetRemoteMarketplaceCacheRoot()
    {
        var normalizedHome = NormalizePath(homePath);
        return string.IsNullOrWhiteSpace(normalizedHome)
            ? null
            : Path.Combine(normalizedHome!, RemoteMarketplacesCacheRelativePath);
    }

    private string GetRemoteMarketplaceCachePath(string remoteMarketplaceId)
    {
        var safeId = NormalizeRequiredSegment(remoteMarketplaceId, "remote marketplace id");
        var cacheRoot = GetRemoteMarketplaceCacheRoot()
            ?? throw new KernelPluginInstallException("TianShu home path is not available", invalidRequest: false);
        return Path.Combine(cacheRoot, safeId, "marketplace.json");
    }

    private IEnumerable<string> DiscoverMarketplacePaths(string cwd)
    {
        var paths = new List<string>();
        var gitRoot = FindGitRoot(cwd);
        if (!string.IsNullOrWhiteSpace(gitRoot))
        {
            var repoPath = Path.Combine(gitRoot!, MarketplaceRelativePath);
            if (File.Exists(repoPath))
            {
                paths.Add(repoPath);
            }
        }

        var curatedMarketplace = GetCuratedMarketplacePath();
        if (!string.IsNullOrWhiteSpace(curatedMarketplace) && File.Exists(curatedMarketplace))
        {
            paths.Add(curatedMarketplace!);
        }

        paths.AddRange(DiscoverRemoteMarketplaceCachePaths());

        var homeMarketplace = GetHomeMarketplacePath();
        if (!string.IsNullOrWhiteSpace(homeMarketplace) && File.Exists(homeMarketplace))
        {
            paths.Add(homeMarketplace!);
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindGitRoot(string? cwd)
    {
        var current = NormalizePath(cwd);
        while (!string.IsNullOrWhiteSpace(current))
        {
            var gitPath = Path.Combine(current!, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return current;
            }

            var parent = Directory.GetParent(current!);
            if (parent is null || string.Equals(parent.FullName, current, PathComparison))
            {
                return null;
            }

            current = parent.FullName;
        }

        return null;
    }

    private static void RemoveExistingTarget(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return;
        }

        File.Delete(path);
    }

    private static void CopyDirectoryRecursive(string source, string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(target, Path.GetFileName(directory));
            CopyDirectoryRecursive(directory, destination, cancellationToken);
        }
    }

    private string CreateArchiveExtractionRoot(string pluginName, string marketplaceName)
    {
        var safePluginName = NormalizeRequiredSegment(pluginName, "plugin name");
        var safeMarketplaceName = NormalizeRequiredSegment(marketplaceName, "marketplace name");
        var root = Path.Combine(homePath, "tmp", "plugins", "archives", $"{safeMarketplaceName}-{safePluginName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void ExtractMarketplaceArchive(string archivePath, string extractionRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(archivePath))
        {
            throw new KernelPluginInstallException($"plugin archive source path does not exist: {archivePath}", invalidRequest: true);
        }

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.FullName))
                {
                    continue;
                }

                var destinationPath = Path.GetFullPath(Path.Combine(extractionRoot, entry.FullName));
                if (!IsPathWithinRoot(destinationPath, extractionRoot))
                {
                    throw new KernelPluginInstallException($"plugin archive entry escapes extraction root: {entry.FullName}", invalidRequest: true);
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, overwrite: false);
            }
        }
        catch (KernelPluginInstallException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new KernelPluginInstallException($"invalid plugin archive `{archivePath}`: {ex.Message}", invalidRequest: true, ex);
        }
        catch (Exception ex)
        {
            throw new KernelPluginInstallException($"failed to extract plugin archive `{archivePath}`: {ex.Message}", invalidRequest: false, ex);
        }
    }

    private static void VerifyRemoteArchiveInstallPolicy(MarketplacePluginResolution resolution, PluginMarketplaceTrustPolicy trustPolicy)
    {
        if (!trustPolicy.AllowRemoteArchiveSources)
        {
            throw new KernelPluginInstallException(
                "remote archive plugin sources are disabled by marketplace trust policy",
                invalidRequest: true);
        }

        if (!IsValidRemoteArchiveUri(resolution.SourcePath))
        {
            throw new KernelPluginInstallException(
                $"remote archive plugin source must be an absolute HTTPS URL: {resolution.SourcePath}",
                invalidRequest: true);
        }

        if (string.IsNullOrWhiteSpace(Normalize(resolution.ExpectedSha256)))
        {
            throw new KernelPluginInstallException("remote archive plugin source requires integrity.sha256", invalidRequest: true);
        }

        if (string.IsNullOrWhiteSpace(Normalize(resolution.Signer)))
        {
            throw new KernelPluginInstallException("remote archive plugin source requires integrity.signer", invalidRequest: true);
        }

        if (resolution.Signature is null || !resolution.Signature.IsDeclared)
        {
            throw new KernelPluginInstallException("remote archive plugin source requires integrity.signature", invalidRequest: true);
        }
    }

    private void SyncRemoteMarketplaces(
        IReadOnlyList<PluginRemoteMarketplaceDefinition> remoteMarketplaces,
        PluginMarketplaceTrustPolicy trustPolicy,
        CancellationToken cancellationToken)
    {
        var enabled = remoteMarketplaces.Where(static item => item.Enabled).ToArray();
        if (enabled.Length == 0)
        {
            return;
        }

        if (!trustPolicy.AllowRemoteMarketplaceSources)
        {
            throw new KernelPluginInstallException(
                "remote marketplace sources are disabled by marketplace trust policy",
                invalidRequest: true);
        }

        foreach (var remoteMarketplace in enabled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyncRemoteMarketplace(remoteMarketplace, trustPolicy.RemoteMarketplaceMaxBytes, cancellationToken);
        }
    }

    private void SyncRemoteMarketplace(
        PluginRemoteMarketplaceDefinition remoteMarketplace,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var url = Normalize(remoteMarketplace.Url);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new KernelPluginInstallException(
                $"remote marketplace `{remoteMarketplace.Id}` requires url",
                invalidRequest: true);
        }

        if (!IsValidHttpsUri(url!))
        {
            throw new KernelPluginInstallException(
                $"remote marketplace `{remoteMarketplace.Id}` url must be an absolute HTTPS URL",
                invalidRequest: true);
        }

        var expectedSha256 = Normalize(remoteMarketplace.Sha256);
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            throw new KernelPluginInstallException(
                $"remote marketplace `{remoteMarketplace.Id}` requires sha256",
                invalidRequest: true);
        }

        var bytes = DownloadRemoteMarketplace(remoteMarketplace.Id, url!, maxBytes, cancellationToken);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new KernelPluginInstallException(
                $"remote marketplace `{remoteMarketplace.Id}` integrity check failed: expected sha256 {expectedSha256}, actual {actualSha256}",
                invalidRequest: true);
        }

        ValidateMarketplaceDocument(bytes, $"remote marketplace `{remoteMarketplace.Id}`");
        var destinationPath = GetRemoteMarketplaceCachePath(remoteMarketplace.Id);
        var tempPath = Path.Combine(Path.GetDirectoryName(destinationPath)!, $"marketplace.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private byte[] DownloadRemoteMarketplace(string id, string url, long maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .GetAwaiter()
            .GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new KernelPluginInstallException(
                $"failed to download remote marketplace `{id}`: HTTP {(int)response.StatusCode}",
                invalidRequest: false);
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > 0 && contentLength > maxBytes)
        {
            throw new KernelPluginInstallException(
                $"remote marketplace `{id}` download exceeds configured max bytes: {contentLength} > {maxBytes}",
                invalidRequest: true);
        }

        using var source = response.Content.ReadAsStream(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (total > maxBytes)
            {
                throw new KernelPluginInstallException(
                    $"remote marketplace `{id}` download exceeds configured max bytes: {total} > {maxBytes}",
                    invalidRequest: true);
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static void ValidateMarketplaceDocument(byte[] bytes, string sourceDescription)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (!TryReadObject(document.RootElement, out var marketplaceObject))
            {
                throw new KernelPluginInstallException($"invalid {sourceDescription}: root must be an object", invalidRequest: true);
            }

            var marketplaceName = ReadString(marketplaceObject, "name");
            if (string.IsNullOrWhiteSpace(marketplaceName))
            {
                throw new KernelPluginInstallException($"invalid {sourceDescription}: missing marketplace name", invalidRequest: true);
            }

            if (!TryGetProperty(marketplaceObject, out var pluginsElement, "plugins") || pluginsElement.ValueKind != JsonValueKind.Array)
            {
                throw new KernelPluginInstallException($"invalid {sourceDescription}: missing plugins array", invalidRequest: true);
            }
        }
        catch (JsonException ex)
        {
            throw new KernelPluginInstallException($"invalid {sourceDescription}: {ex.Message}", invalidRequest: true, ex);
        }
    }

    private void DownloadRemoteArchive(string url, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .GetAwaiter()
            .GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new KernelPluginInstallException(
                $"failed to download plugin archive `{url}`: HTTP {(int)response.StatusCode}",
                invalidRequest: false);
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > 0 && contentLength > maxBytes)
        {
            throw new KernelPluginInstallException(
                $"plugin archive download exceeds configured max bytes: {contentLength} > {maxBytes}",
                invalidRequest: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var source = response.Content.ReadAsStream(cancellationToken);
        using var destination = File.Create(destinationPath);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (total > maxBytes)
            {
                throw new KernelPluginInstallException(
                    $"plugin archive download exceeds configured max bytes: {total} > {maxBytes}",
                    invalidRequest: true);
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static bool IsValidRemoteArchiveUri(string value)
        => IsValidHttpsUri(value);

    private static bool IsValidHttpsUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(uri.Host);

    private static string ResolveExtractedPluginRoot(string extractionRoot, string pluginName)
    {
        if (TryReadManifestName(extractionRoot, out _, out _))
        {
            return extractionRoot;
        }

        var candidates = Directory.EnumerateDirectories(extractionRoot)
            .Where(path => TryReadManifestName(path, out _, out _))
            .ToArray();
        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        throw new KernelPluginInstallException(
            $"plugin archive must contain plugin `{pluginName}` at archive root or in a single top-level directory",
            invalidRequest: true);
    }

    private static void VerifyPluginDirectoryIntegrity(string sourcePath, string? expectedSha256)
    {
        var normalizedExpected = Normalize(expectedSha256);
        if (string.IsNullOrWhiteSpace(normalizedExpected))
        {
            return;
        }

        var actual = ComputePluginDirectorySha256(sourcePath);
        if (!string.Equals(actual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new KernelPluginInstallException(
                $"plugin integrity check failed: expected sha256 {normalizedExpected}, actual {actual}",
                invalidRequest: true);
        }
    }

    private static void VerifyMarketplaceSignerTrust(string? signer, PluginMarketplaceTrustPolicy trustPolicy)
    {
        var status = ResolveSignerTrustStatus(signer, trustPolicy);
        if (string.Equals(status, "missing-required", StringComparison.Ordinal))
        {
            throw new KernelPluginInstallException(
                "plugin signer is required by marketplace trust policy",
                invalidRequest: true);
        }

        if (string.Equals(status, "untrusted", StringComparison.Ordinal))
        {
            throw new KernelPluginInstallException(
                $"plugin signer `{Normalize(signer)}` is not trusted by marketplace trust policy",
                invalidRequest: true);
        }
    }

    private static void VerifyMarketplaceSignature(
        string marketplaceName,
        string pluginName,
        string? expectedSha256,
        string? signer,
        MarketplacePluginSignature? signature,
        PluginMarketplaceTrustPolicy trustPolicy)
    {
        var status = ResolveSignatureStatus(marketplaceName, pluginName, expectedSha256, signer, signature, trustPolicy);
        if (string.Equals(status, "not-declared", StringComparison.Ordinal)
            || string.Equals(status, "verified", StringComparison.Ordinal))
        {
            return;
        }

        throw new KernelPluginInstallException(
            $"plugin signature check failed: {status}",
            invalidRequest: true);
    }

    private static string VerifyEcdsaP256Signature(
        string marketplaceName,
        string pluginName,
        string signer,
        string expectedSha256,
        string publicKeyBase64,
        string signatureBase64,
        string expectedPublicKeySha256)
    {
        byte[] publicKeyBytes;
        try
        {
            publicKeyBytes = Convert.FromBase64String(publicKeyBase64);
        }
        catch
        {
            return "invalid-public-key";
        }

        var actualPublicKeySha256 = Convert.ToHexString(SHA256.HashData(publicKeyBytes)).ToLowerInvariant();
        if (!string.Equals(actualPublicKeySha256, Normalize(expectedPublicKeySha256), StringComparison.OrdinalIgnoreCase))
        {
            return "public-key-mismatch";
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signatureBase64);
        }
        catch
        {
            return "invalid-signature";
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out var bytesRead);
            if (bytesRead != publicKeyBytes.Length)
            {
                return "invalid-public-key";
            }

            var payload = BuildMarketplaceSignaturePayload(marketplaceName, pluginName, signer, expectedSha256);
            return ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(payload),
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence)
                ? "verified"
                : "invalid";
        }
        catch
        {
            return "invalid";
        }
    }

    private static string VerifyEcdsaP256CertificateChainSignature(
        string marketplaceName,
        string pluginName,
        string signer,
        string expectedSha256,
        IReadOnlyList<string> certificateChainBase64,
        string signatureBase64,
        string expectedPublicKeySha256,
        IReadOnlyDictionary<string, PluginMarketplaceCertificateAuthorityTrust> certificateAuthorities)
    {
        if (certificateChainBase64.Count == 0)
        {
            return "invalid-certificate-chain";
        }

        var certificates = new List<X509Certificate2>(certificateChainBase64.Count);
        IReadOnlyList<X509Certificate2> authorityCandidates = Array.Empty<X509Certificate2>();
        try
        {
            foreach (var certificateBase64 in certificateChainBase64)
            {
                if (string.IsNullOrWhiteSpace(certificateBase64))
                {
                    return "invalid-certificate-chain";
                }

                byte[] certificateBytes;
                try
                {
                    certificateBytes = Convert.FromBase64String(certificateBase64);
                }
                catch
                {
                    return "invalid-certificate-chain";
                }

                certificates.Add(X509CertificateLoader.LoadCertificate(certificateBytes));
            }

            if (certificateAuthorities.Count == 0)
            {
                return "missing-certificate-authority";
            }

            authorityCandidates = ResolveCertificateAuthorityCandidates(certificates, certificateAuthorities);
            if (authorityCandidates.Count == 0)
            {
                return "certificate-authority-mismatch";
            }

            var leaf = certificates[0];
            if (!TryVerifyCertificateChain(leaf, certificates.Skip(1), authorityCandidates))
            {
                return "certificate-chain-invalid";
            }

            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signatureBase64);
            }
            catch
            {
                return "invalid-signature";
            }

            using var ecdsa = leaf.GetECDsaPublicKey();
            if (ecdsa is null)
            {
                return "invalid-public-key";
            }

            var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
            var actualPublicKeySha256 = Convert.ToHexString(SHA256.HashData(publicKeyBytes)).ToLowerInvariant();
            if (!string.Equals(actualPublicKeySha256, Normalize(expectedPublicKeySha256), StringComparison.OrdinalIgnoreCase))
            {
                return "certificate-leaf-public-key-mismatch";
            }

            var payload = BuildMarketplaceSignaturePayload(marketplaceName, pluginName, signer, expectedSha256);
            return ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(payload),
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence)
                ? "verified"
                : "invalid";
        }
        catch
        {
            return "invalid-certificate-chain";
        }
        finally
        {
            foreach (var certificate in certificates)
            {
                certificate.Dispose();
            }

            foreach (var certificate in authorityCandidates)
            {
                certificate.Dispose();
            }
        }
    }

    private static IReadOnlyList<X509Certificate2> ResolveCertificateAuthorityCandidates(
        IReadOnlyList<X509Certificate2> certificateChain,
        IReadOnlyDictionary<string, PluginMarketplaceCertificateAuthorityTrust> certificateAuthorities)
    {
        var candidates = new List<X509Certificate2>();
        foreach (var authority in certificateAuthorities.Values)
        {
            if (!authority.Enabled)
            {
                continue;
            }

            var expectedSha256 = Normalize(authority.CertificateSha256);
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                continue;
            }

            var configuredCertificate = Normalize(authority.Certificate);
            if (!string.IsNullOrWhiteSpace(configuredCertificate))
            {
                try
                {
                    var certificateBytes = Convert.FromBase64String(configuredCertificate!);
                    var actualSha256 = Convert.ToHexString(SHA256.HashData(certificateBytes)).ToLowerInvariant();
                    if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    candidates.Add(X509CertificateLoader.LoadCertificate(certificateBytes));
                    continue;
                }
                catch
                {
                    continue;
                }
            }

            foreach (var certificate in certificateChain.Skip(1))
            {
                var actualSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
                if (string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(X509CertificateLoader.LoadCertificate(certificate.RawData));
                    break;
                }
            }
        }

        return candidates;
    }

    private static bool TryVerifyCertificateChain(
        X509Certificate2 leaf,
        IEnumerable<X509Certificate2> suppliedChain,
        IReadOnlyList<X509Certificate2> trustedRoots)
    {
        foreach (var trustedRoot in trustedRoots)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);

            foreach (var certificate in suppliedChain)
            {
                chain.ChainPolicy.ExtraStore.Add(certificate);
            }

            if (chain.Build(leaf))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildMarketplaceSignaturePayload(string marketplaceName, string pluginName, string signer, string expectedSha256)
        => string.Join(
            "\n",
            "TianShu plugin marketplace signature v1",
            $"marketplace:{Normalize(marketplaceName)}",
            $"plugin:{Normalize(pluginName)}",
            $"signer:{Normalize(signer)}",
            $"sha256:{Normalize(expectedSha256)?.ToLowerInvariant()}",
            string.Empty);

    private static string ComputePluginDirectorySha256(string sourcePath)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                     .OrderBy(path => NormalizeDigestPath(Path.GetRelativePath(sourcePath, path)), StringComparer.Ordinal))
        {
            var relativePath = NormalizeDigestPath(Path.GetRelativePath(sourcePath, file));
            AppendDigestText(hash, relativePath);
            AppendDigestText(hash, new FileInfo(file).Length.ToString(CultureInfo.InvariantCulture));
            using var stream = File.OpenRead(file);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }

            hash.AppendData(new byte[] { 0 });
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendDigestText(IncrementalHash hash, string text)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(text));
        hash.AppendData(new byte[] { 0 });
    }

    private static string NormalizeDigestPath(string path)
        => path.Replace('\\', '/');

    private static bool TryReadManifestName(string pluginRoot, out string? manifestName, out string? error)
    {
        manifestName = null;
        error = null;
        var manifestPath = Path.Combine(pluginRoot, PluginManifestRelativePath);
        if (!File.Exists(manifestPath))
        {
            error = $"missing plugin manifest: {manifestPath}";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var rawName = document.RootElement.ValueKind == JsonValueKind.Object
                ? ReadString(document.RootElement, "name")
                : null;
            manifestName = string.IsNullOrWhiteSpace(rawName)
                ? Path.GetFileName(pluginRoot)
                : rawName;
            if (string.IsNullOrWhiteSpace(manifestName))
            {
                error = $"missing or invalid plugin manifest: {manifestPath}";
                return false;
            }

            return true;
        }
        catch
        {
            error = $"missing or invalid plugin manifest: {manifestPath}";
            return false;
        }
    }

    private string GetConfigTomlPath()
        => Path.Combine(homePath, "tianshu.toml");

    private string GetPluginsCacheRoot()
        => Path.Combine(homePath, PluginsCacheRelativePath);

    private static bool IsPathWithinRoot(string path, string root)
    {
        var normalizedPath = AppendDirectorySeparator(Path.GetFullPath(path));
        var normalizedRoot = AppendDirectorySeparator(Path.GetFullPath(root));
        return normalizedPath.StartsWith(normalizedRoot, PathComparison);
    }

    private static string AppendDirectorySeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static (string PluginName, string MarketplaceName)? ParsePluginKey(string pluginKey, bool throwOnInvalid)
    {
        var normalized = Normalize(pluginKey);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            if (throwOnInvalid)
            {
                throw new KernelPluginInstallException($"invalid plugin key `{pluginKey}`; expected <plugin>@<marketplace>", invalidRequest: true);
            }

            return null;
        }

        var separatorIndex = normalized!.LastIndexOf('@');
        if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1)
        {
            if (throwOnInvalid)
            {
                throw new KernelPluginInstallException($"invalid plugin key `{pluginKey}`; expected <plugin>@<marketplace>", invalidRequest: true);
            }

            return null;
        }

        var pluginName = NormalizeRequiredSegment(normalized[..separatorIndex], "plugin name");
        var marketplaceName = NormalizeRequiredSegment(normalized[(separatorIndex + 1)..], "marketplace name");
        return (pluginName, marketplaceName);
    }

    private static string NormalizeRequiredSegment(string value, string label)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new KernelPluginInstallException($"invalid {label}: must not be empty", invalidRequest: true);
        }

        if (!normalized!.All(static ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
        {
            throw new KernelPluginInstallException($"invalid {label}: only ASCII letters, digits, `_`, and `-` are allowed", invalidRequest: true);
        }

        return normalized;
    }

    private static bool TryReadObject(JsonElement element, out JsonElement objectElement)
    {
        objectElement = element;
        return element.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? Normalize(value.GetString()) : null;
    }

    private static bool? ReadBool(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    }

    private static double? ReadDouble(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return value.TryGetDouble(out var result) ? result : null;
    }

    private static long? ReadLong(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return value.TryGetInt64(out var result) ? result : null;
    }

    private static List<string> ReadStringArray(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(static item => item.ValueKind == JsonValueKind.String ? Normalize(item.GetString()) : null)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToList();
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names) || value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            string? text = property.Value.ValueKind switch
            {
                JsonValueKind.String => Normalize(property.Value.GetString()),
                JsonValueKind.Number => Normalize(property.Value.GetRawText()),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                result[property.Name] = text!;
            }
        }

        return result;
    }

    private static bool? ReadTomlBool(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is bool flag ? flag : null;

    private static long? ReadTomlLong(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            long number when number > 0 => number,
            int number when number > 0 => number,
            string text when long.TryParse(Normalize(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 => parsed,
            _ => null,
        };
    }

    private static string? ReadTomlString(TomlTable table, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (table.TryGetValue(key, out var value) && value is string text)
            {
                return Normalize(text);
            }
        }

        return null;
    }

    private static TomlTable? ReadTomlTable(TomlTable table, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (table.TryGetValue(key, out var value) && value is TomlTable nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool ReadConfigBool(IReadOnlyDictionary<string, string> values, bool defaultValue, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var rawValue)
                && bool.TryParse(NormalizeRawConfigValue(rawValue), out var parsed))
            {
                return parsed;
            }
        }

        return defaultValue;
    }

    private static long ReadConfigLong(IReadOnlyDictionary<string, string> values, long defaultValue, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var rawValue)
                && long.TryParse(NormalizeRawConfigValue(rawValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0)
            {
                return parsed;
            }
        }

        return defaultValue;
    }

    private static IEnumerable<string> ReadConfigStringSet(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var rawValue))
            {
                foreach (var item in ParseStringSet(rawValue))
                {
                    yield return item;
                }
            }
        }
    }

    private static IReadOnlyList<PluginRemoteMarketplaceDefinition> ReadConfigRemoteMarketplaceDefinitions(IReadOnlyDictionary<string, string> values)
    {
        var builders = new Dictionary<string, PluginRemoteMarketplaceBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            foreach (var prefix in new[]
            {
                "plugins.remote_marketplaces.",
            })
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? id = null;
                if (pair.Key.EndsWith(".enabled", StringComparison.OrdinalIgnoreCase))
                {
                    id = pair.Key[prefix.Length..^".enabled".Length];
                    if (bool.TryParse(NormalizeRawConfigValue(pair.Value), out var enabled))
                    {
                        GetRemoteMarketplaceBuilder(builders, id).Enabled = enabled;
                    }
                }
                else if (pair.Key.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                {
                    id = pair.Key[prefix.Length..^".url".Length];
                    GetRemoteMarketplaceBuilder(builders, id).Url = NormalizeRawConfigValue(pair.Value);
                }
                else if (pair.Key.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                {
                    id = pair.Key[prefix.Length..^".sha256".Length];
                    GetRemoteMarketplaceBuilder(builders, id).Sha256 = NormalizeRawConfigValue(pair.Value);
                }
            }
        }

        return BuildRemoteMarketplaceDefinitions(builders);
    }

    private static IReadOnlyList<PluginRemoteMarketplaceDefinition> ReadTomlRemoteMarketplaceDefinitions(TomlTable plugins)
    {
        var result = new List<PluginRemoteMarketplaceDefinition>();
        var remoteMarketplaces = ReadTomlTable(plugins, "remote_marketplaces");
        if (remoteMarketplaces is null)
        {
            return result;
        }

        foreach (var pair in remoteMarketplaces)
        {
            if (pair.Value is not TomlTable table)
            {
                continue;
            }

            var id = Normalize(pair.Key);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            result.Add(new PluginRemoteMarketplaceDefinition(
                id!,
                ReadTomlBool(table, "enabled") ?? true,
                ReadTomlString(table, "url"),
                ReadTomlString(table, "sha256")));
        }

        return result;
    }

    private static PluginRemoteMarketplaceBuilder GetRemoteMarketplaceBuilder(
        IDictionary<string, PluginRemoteMarketplaceBuilder> builders,
        string? id)
    {
        var normalizedId = Normalize(id?.Trim('"', '\''));
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return new PluginRemoteMarketplaceBuilder();
        }

        if (!builders.TryGetValue(normalizedId!, out var builder))
        {
            builder = new PluginRemoteMarketplaceBuilder();
            builders[normalizedId!] = builder;
        }

        return builder;
    }

    private static IReadOnlyList<PluginRemoteMarketplaceDefinition> BuildRemoteMarketplaceDefinitions(
        IReadOnlyDictionary<string, PluginRemoteMarketplaceBuilder> builders)
        => builders
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new PluginRemoteMarketplaceDefinition(
                pair.Key,
                pair.Value.Enabled ?? true,
                Normalize(pair.Value.Url),
                Normalize(pair.Value.Sha256)))
            .ToArray();

    private static IReadOnlyDictionary<string, PluginMarketplaceSignerTrust> ReadConfigSignerTrusts(IReadOnlyDictionary<string, string> values)
    {
        var result = new Dictionary<string, PluginMarketplaceSignerTrust>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            foreach (var prefix in new[]
            {
                "plugins.marketplace_trust.signers.",
            })
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? signer = null;
                string? fingerprint = null;
                string? publicKey = null;
                if (pair.Key.EndsWith(".public_key_sha256", StringComparison.OrdinalIgnoreCase))
                {
                    signer = pair.Key[prefix.Length..^".public_key_sha256".Length];
                    fingerprint = NormalizeRawConfigValue(pair.Value);
                }
                else if (pair.Key.EndsWith(".public_key", StringComparison.OrdinalIgnoreCase))
                {
                    signer = pair.Key[prefix.Length..^".public_key".Length];
                    publicKey = NormalizeRawConfigValue(pair.Value);
                }

                MergeSignerTrust(result, signer, fingerprint, publicKey);
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, PluginMarketplaceCertificateAuthorityTrust> ReadConfigCertificateAuthorityTrusts(IReadOnlyDictionary<string, string> values)
    {
        var result = new Dictionary<string, PluginMarketplaceCertificateAuthorityTrust>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            foreach (var prefix in new[]
            {
                "plugins.marketplace_trust.certificate_authorities.",
            })
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? authority = null;
                bool? enabled = null;
                string? certificateSha256 = null;
                string? certificate = null;
                if (pair.Key.EndsWith(".enabled", StringComparison.OrdinalIgnoreCase))
                {
                    authority = pair.Key[prefix.Length..^".enabled".Length];
                    enabled = bool.TryParse(NormalizeRawConfigValue(pair.Value), out var parsedEnabled) ? parsedEnabled : null;
                }
                else if (pair.Key.EndsWith(".certificate_sha256", StringComparison.OrdinalIgnoreCase))
                {
                    authority = pair.Key[prefix.Length..^".certificate_sha256".Length];
                    certificateSha256 = NormalizeRawConfigValue(pair.Value);
                }
                else if (pair.Key.EndsWith(".certificate", StringComparison.OrdinalIgnoreCase))
                {
                    authority = pair.Key[prefix.Length..^".certificate".Length];
                    certificate = NormalizeRawConfigValue(pair.Value);
                }

                MergeCertificateAuthorityTrust(result, authority, enabled, certificateSha256, certificate);
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, PluginMarketplaceTransparencyLogTrust> ReadConfigTransparencyLogTrusts(IReadOnlyDictionary<string, string> values)
    {
        var result = new Dictionary<string, PluginMarketplaceTransparencyLogTrust>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            foreach (var prefix in new[]
            {
                "plugins.marketplace_trust.transparency_logs.",
            })
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? log = null;
                bool? enabled = null;
                string? publicKeySha256 = null;
                string? publicKey = null;
                if (pair.Key.EndsWith(".enabled", StringComparison.OrdinalIgnoreCase))
                {
                    log = pair.Key[prefix.Length..^".enabled".Length];
                    enabled = bool.TryParse(NormalizeRawConfigValue(pair.Value), out var parsedEnabled) ? parsedEnabled : null;
                }
                else if (pair.Key.EndsWith(".public_key_sha256", StringComparison.OrdinalIgnoreCase))
                {
                    log = pair.Key[prefix.Length..^".public_key_sha256".Length];
                    publicKeySha256 = NormalizeRawConfigValue(pair.Value);
                }
                else if (pair.Key.EndsWith(".public_key", StringComparison.OrdinalIgnoreCase))
                {
                    log = pair.Key[prefix.Length..^".public_key".Length];
                    publicKey = NormalizeRawConfigValue(pair.Value);
                }

                MergeTransparencyLogTrust(result, log, enabled, publicKeySha256, publicKey);
            }
        }

        return result;
    }

    private static void MergeSignerTrust(
        IDictionary<string, PluginMarketplaceSignerTrust> result,
        string? signer,
        string? publicKeySha256,
        string? publicKey)
    {
        var normalizedSigner = Normalize(signer?.Trim('"', '\''));
        var normalizedPublicKeySha256 = Normalize(publicKeySha256);
        var normalizedPublicKey = Normalize(publicKey);
        if (string.IsNullOrWhiteSpace(normalizedSigner)
            || (string.IsNullOrWhiteSpace(normalizedPublicKeySha256) && string.IsNullOrWhiteSpace(normalizedPublicKey)))
        {
            return;
        }

        if (result.TryGetValue(normalizedSigner!, out var existing))
        {
            result[normalizedSigner!] = new PluginMarketplaceSignerTrust(
                normalizedPublicKeySha256 ?? existing.PublicKeySha256,
                normalizedPublicKey ?? existing.PublicKey);
        }
        else
        {
            result[normalizedSigner!] = new PluginMarketplaceSignerTrust(normalizedPublicKeySha256, normalizedPublicKey);
        }
    }

    private static IReadOnlyDictionary<string, PluginMarketplaceSignerTrust> ReadTomlSignerTrusts(TomlTable trustTable)
    {
        var result = new Dictionary<string, PluginMarketplaceSignerTrust>(StringComparer.OrdinalIgnoreCase);
        var signersTable = ReadTomlTable(trustTable, "signers");
        if (signersTable is null)
        {
            return result;
        }

        foreach (var pair in signersTable)
        {
            if (pair.Value is not TomlTable signerTable)
            {
                continue;
            }

            MergeSignerTrust(
                result,
                pair.Key,
                ReadTomlString(signerTable, "public_key_sha256"),
                ReadTomlString(signerTable, "public_key"));
        }

        return result;
    }

    private static void MergeCertificateAuthorityTrust(
        IDictionary<string, PluginMarketplaceCertificateAuthorityTrust> result,
        string? authority,
        bool? enabled,
        string? certificateSha256,
        string? certificate)
    {
        var normalizedAuthority = Normalize(authority?.Trim('"', '\''));
        var normalizedCertificateSha256 = Normalize(certificateSha256);
        var normalizedCertificate = Normalize(certificate);
        if (string.IsNullOrWhiteSpace(normalizedAuthority)
            || (enabled is null && string.IsNullOrWhiteSpace(normalizedCertificateSha256) && string.IsNullOrWhiteSpace(normalizedCertificate)))
        {
            return;
        }

        if (result.TryGetValue(normalizedAuthority!, out var existing))
        {
            result[normalizedAuthority!] = new PluginMarketplaceCertificateAuthorityTrust(
                enabled ?? existing.Enabled,
                normalizedCertificateSha256 ?? existing.CertificateSha256,
                normalizedCertificate ?? existing.Certificate);
        }
        else
        {
            result[normalizedAuthority!] = new PluginMarketplaceCertificateAuthorityTrust(enabled ?? true, normalizedCertificateSha256, normalizedCertificate);
        }
    }

    private static IReadOnlyDictionary<string, PluginMarketplaceCertificateAuthorityTrust> ReadTomlCertificateAuthorityTrusts(TomlTable trustTable)
    {
        var result = new Dictionary<string, PluginMarketplaceCertificateAuthorityTrust>(StringComparer.OrdinalIgnoreCase);
        var authoritiesTable = ReadTomlTable(trustTable, "certificate_authorities");
        if (authoritiesTable is null)
        {
            return result;
        }

        foreach (var pair in authoritiesTable)
        {
            if (pair.Value is not TomlTable authorityTable)
            {
                continue;
            }

            MergeCertificateAuthorityTrust(
                result,
                pair.Key,
                ReadTomlBool(authorityTable, "enabled"),
                ReadTomlString(authorityTable, "certificate_sha256"),
                ReadTomlString(authorityTable, "certificate"));
        }

        return result;
    }

    private static void MergeTransparencyLogTrust(
        IDictionary<string, PluginMarketplaceTransparencyLogTrust> result,
        string? log,
        bool? enabled,
        string? publicKeySha256,
        string? publicKey)
    {
        var normalizedLog = Normalize(log?.Trim('"', '\''));
        var normalizedPublicKeySha256 = Normalize(publicKeySha256);
        var normalizedPublicKey = Normalize(publicKey);
        if (string.IsNullOrWhiteSpace(normalizedLog)
            || (enabled is null && string.IsNullOrWhiteSpace(normalizedPublicKeySha256) && string.IsNullOrWhiteSpace(normalizedPublicKey)))
        {
            return;
        }

        if (result.TryGetValue(normalizedLog!, out var existing))
        {
            result[normalizedLog!] = new PluginMarketplaceTransparencyLogTrust(
                enabled ?? existing.Enabled,
                normalizedPublicKeySha256 ?? existing.PublicKeySha256,
                normalizedPublicKey ?? existing.PublicKey);
        }
        else
        {
            result[normalizedLog!] = new PluginMarketplaceTransparencyLogTrust(enabled ?? true, normalizedPublicKeySha256, normalizedPublicKey);
        }
    }

    private static IReadOnlyDictionary<string, PluginMarketplaceTransparencyLogTrust> ReadTomlTransparencyLogTrusts(TomlTable trustTable)
    {
        var result = new Dictionary<string, PluginMarketplaceTransparencyLogTrust>(StringComparer.OrdinalIgnoreCase);
        var logsTable = ReadTomlTable(trustTable, "transparency_logs");
        if (logsTable is null)
        {
            return result;
        }

        foreach (var pair in logsTable)
        {
            if (pair.Value is not TomlTable logTable)
            {
                continue;
            }

            MergeTransparencyLogTrust(
                result,
                pair.Key,
                ReadTomlBool(logTable, "enabled"),
                ReadTomlString(logTable, "public_key_sha256"),
                ReadTomlString(logTable, "public_key"));
        }

        return result;
    }

    private static IEnumerable<string> ReadTomlStringSet(TomlTable table, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!table.TryGetValue(key, out var value))
            {
                continue;
            }

            if (value is TomlArray array)
            {
                foreach (var item in array.OfType<string>())
                {
                    var normalized = Normalize(item);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        yield return normalized!;
                    }
                }

                continue;
            }

            if (value is string text)
            {
                foreach (var item in ParseStringSet(text))
                {
                    yield return item;
                }
            }
        }
    }

    private static IEnumerable<string> ParseStringSet(string rawValue)
    {
        var parsed = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(rawValue);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var normalized = item.ValueKind == JsonValueKind.String
                        ? Normalize(item.GetString())
                        : null;
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        parsed.Add(normalized!);
                    }
                }

                return parsed;
            }

            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                rawValue = document.RootElement.GetString() ?? string.Empty;
            }
        }
        catch
        {
        }

        foreach (var item in rawValue.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = Normalize(item.Trim('"', '\''));
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                parsed.Add(normalized!);
            }
        }

        return parsed;
    }

    private static string? NormalizeRawConfigValue(string rawValue)
    {
        try
        {
            using var json = JsonDocument.Parse(rawValue);
            var root = json.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.String => Normalize(root.GetString()),
                JsonValueKind.Number => Normalize(root.GetRawText()),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => null,
            };
        }
        catch
        {
            return Normalize(rawValue.Trim().Trim('"', '\''));
        }
    }

    private static string? NormalizePath(string? value)
    {
        var normalized = Normalize(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : Path.GetFullPath(normalized!);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private string GetPluginInstallRoot(string pluginName, string marketplaceName)
        => Path.Combine(GetPluginsCacheRoot(), marketplaceName, pluginName);

    private string? ResolveInstalledPluginRoot(string pluginName, string marketplaceName)
    {
        var installRoot = GetPluginInstallRoot(pluginName, marketplaceName);
        if (!Directory.Exists(installRoot))
        {
            return null;
        }

        var preferredLocalPath = Path.Combine(installRoot, DefaultPluginVersion);
        if (TryReadManifestName(preferredLocalPath, out _, out _))
        {
            return preferredLocalPath;
        }

        foreach (var versionPath in Directory.EnumerateDirectories(installRoot)
                     .OrderBy(static path => path, PathComparer))
        {
            if (TryReadManifestName(versionPath, out _, out _))
            {
                return versionPath;
            }
        }

        return null;
    }

    private void RemoveInstalledPluginStore(string pluginKey)
    {
        var plugin = ParsePluginKey(pluginKey, throwOnInvalid: true)!.Value;
        RemoveExistingTarget(GetPluginInstallRoot(plugin.PluginName, plugin.MarketplaceName));
    }

    private CuratedMarketplaceState LoadCuratedMarketplaceState()
    {
        var marketplacePath = GetCuratedMarketplacePath();
        if (string.IsNullOrWhiteSpace(marketplacePath) || !File.Exists(marketplacePath))
        {
            throw new InvalidOperationException("local curated marketplace not found");
        }

        using var document = LoadMarketplaceDocument(marketplacePath!);
        if (!TryReadObject(document.RootElement, out var marketplaceObject))
        {
            throw new KernelPluginInstallException($"invalid marketplace file {marketplacePath}: root must be an object", invalidRequest: true);
        }

        var marketplaceName = ReadString(marketplaceObject, "name");
        if (string.IsNullOrWhiteSpace(marketplaceName))
        {
            throw new KernelPluginInstallException($"invalid marketplace file {marketplacePath}: missing marketplace name", invalidRequest: true);
        }

        if (!TryGetProperty(marketplaceObject, out var pluginsElement, "plugins") || pluginsElement.ValueKind != JsonValueKind.Array)
        {
            throw new KernelPluginInstallException($"invalid marketplace file {marketplacePath}: missing plugins array", invalidRequest: true);
        }

        var seenPluginNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plugins = new List<CuratedMarketplacePluginEntry>();
        foreach (var pluginElement in pluginsElement.EnumerateArray())
        {
            var pluginName = ReadString(pluginElement, "name");
            if (string.IsNullOrWhiteSpace(pluginName) || !seenPluginNames.Add(pluginName!))
            {
                continue;
            }

            var resolution = CreateMarketplacePluginResolution(marketplacePath!, marketplaceName!, pluginElement);
            plugins.Add(new CuratedMarketplacePluginEntry(
                pluginName!,
                resolution.PluginKey,
                resolution.SourceType,
                resolution.SourcePath,
                resolution.ExpectedSha256,
                resolution.Signer,
                resolution.Signature));
        }

        return new CuratedMarketplaceState(marketplaceName!, plugins);
    }

    private string? ReadCuratedPluginsSha()
    {
        var normalizedHome = NormalizePath(homePath);
        if (string.IsNullOrWhiteSpace(normalizedHome))
        {
            return null;
        }

        var shaPath = Path.Combine(normalizedHome!, CuratedPluginsShaRelativePath);
        return File.Exists(shaPath)
            ? Normalize(File.ReadAllText(shaPath))
            : null;
    }

    private static Dictionary<string, bool> BuildRemoteEnabledByName(
        IReadOnlyList<KernelRemotePluginState> remoteStates,
        string curatedMarketplaceName,
        ISet<string> localPluginNames)
    {
        var remoteEnabledByName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in remoteStates)
        {
            if (string.IsNullOrWhiteSpace(state.PluginId))
            {
                continue;
            }

            var parsed = ParsePluginKey(state.PluginId, throwOnInvalid: false);
            if (parsed is null)
            {
                throw new InvalidOperationException($"invalid remote plugin key `{state.PluginId}`");
            }

            if (!string.Equals(parsed.Value.MarketplaceName, curatedMarketplaceName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"unknown remote marketplace `{parsed.Value.MarketplaceName}`");
            }

            if (!localPluginNames.Contains(parsed.Value.PluginName))
            {
                continue;
            }

            if (!remoteEnabledByName.TryAdd(parsed.Value.PluginName, state.Enabled))
            {
                throw new InvalidOperationException($"duplicate remote plugin `{parsed.Value.PluginName}`");
            }
        }

        return remoteEnabledByName;
    }

    private static string? ResolvePluginDisplayName(KernelLoadedPlugin plugin)
    {
        var manifestName = Normalize(plugin.ManifestName);
        if (!string.IsNullOrWhiteSpace(manifestName))
        {
            return manifestName;
        }

        var configKey = Normalize(plugin.ConfigKey);
        if (string.IsNullOrWhiteSpace(configKey))
        {
            return null;
        }

        var atIndex = configKey!.IndexOf('@', StringComparison.Ordinal);
        return atIndex > 0 ? configKey[..atIndex] : configKey;
    }

    private sealed record MarketplacePluginResolution(
        string PluginKey,
        string PluginName,
        string SourceType,
        string SourcePath,
        string MarketplaceName,
        string InstallPolicy = "AVAILABLE",
        string AuthPolicy = "ON_INSTALL",
        string? Category = null,
        KernelPluginInterfaceInfo? Interface = null,
        string? ExpectedSha256 = null,
        string? Signer = null,
        MarketplacePluginSignature? Signature = null);

    private sealed record KernelLoadedPlugin(
        string ConfigKey,
        string? ManifestName,
        string RootPath,
        bool Enabled,
        IReadOnlyList<KernelPluginSkillRoot> SkillRoots,
        IReadOnlyDictionary<string, KernelPluginMcpServerDefinition> McpServers,
        IReadOnlyList<string> AppIds,
        string? Error)
    {
        public bool IsActive => Enabled && string.IsNullOrWhiteSpace(Error);
    }
}
