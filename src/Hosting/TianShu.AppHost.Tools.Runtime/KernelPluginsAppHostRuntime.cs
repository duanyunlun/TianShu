using System.Net.Http.Headers;
using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Catalog;
using TianShu.Provider.Abstractions;
using static TianShu.AppHost.Tools.KernelAppCatalogUtilities;
using static TianShu.AppHost.Configuration.TianShuHomePathUtilities;
using static TianShu.AppHost.Configuration.KernelTomlTextParsingUtilities;
using static TianShu.AppHost.Tools.KernelToolJsonHelpers;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelChatGptAuthContext(string AccessToken, string AccountId);

internal sealed record KernelAccessibleConnectorsResult(bool IsReady, IReadOnlyList<KernelPluginConnectorInfo> Connectors);

internal sealed record KernelAccessibleToolsSnapshot(
    bool IsReady,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools,
    IReadOnlyList<KernelPluginConnectorInfo> AccessibleConnectors);

internal sealed class KernelPluginsAppHostRuntime
{
    private readonly object appListStateGate = new();
    private readonly HttpClient providerHttpClient;
    private readonly Func<string, CancellationToken, Task<(bool Found, string? Cwd)>> loadThreadCwdAsync;
    private readonly Func<string?, CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync;
    private readonly Func<string?, CancellationToken, Task<string?>> loadMergedPersistedConfigTextAsync;
    private readonly KernelPluginsManager pluginsManager;
    private readonly KernelSkillsManager skillsManager;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private IReadOnlyList<KernelPluginConnectorInfo>? cachedAppDirectoryConnectors;
    private IReadOnlyList<KernelPluginConnectorInfo>? cachedAccessibleAppConnectors;

    public KernelPluginsAppHostRuntime(
        HttpClient providerHttpClient,
        Func<string, CancellationToken, Task<(bool Found, string? Cwd)>> loadThreadCwdAsync,
        Func<string?, CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync,
        Func<string?, CancellationToken, Task<string?>> loadMergedPersistedConfigTextAsync,
        KernelPluginsManager pluginsManager,
        KernelSkillsManager skillsManager,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync)
    {
        this.providerHttpClient = providerHttpClient;
        this.loadThreadCwdAsync = loadThreadCwdAsync;
        this.loadEffectiveConfigValuesAsync = loadEffectiveConfigValuesAsync;
        this.loadMergedPersistedConfigTextAsync = loadMergedPersistedConfigTextAsync;
        this.pluginsManager = pluginsManager;
        this.skillsManager = skillsManager;
        this.writeNotificationAsync = writeNotificationAsync;
        this.writeErrorAsync = writeErrorAsync;
        this.writeResultAsync = writeResultAsync;
    }

    public Task<string?> ReadTianShuConfigTextAsync(CancellationToken cancellationToken)
        => loadMergedPersistedConfigTextAsync(Environment.CurrentDirectory, cancellationToken);

    public void ClearAppListCache()
    {
        lock (appListStateGate)
        {
            cachedAppDirectoryConnectors = null;
            cachedAccessibleAppConnectors = null;
        }
    }

    public async Task HandlePluginListAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        List<string>? cwds = null;
        var forceRemoteSync = ReadBool(@params, "forceRemoteSync") ?? false;
        if (@params.ValueKind == JsonValueKind.Object && @params.TryGetProperty("cwds", out var cwdsElement) && cwdsElement.ValueKind != JsonValueKind.Null)
        {
            if (cwdsElement.ValueKind != JsonValueKind.Array)
            {
                await writeErrorAsync(id, -32600, "cwds 必须是字符串数组。", cancellationToken).ConfigureAwait(false);
                return;
            }

            cwds = new List<string>();
            foreach (var item in cwdsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    await writeErrorAsync(id, -32600, "cwds 必须是字符串数组。", cancellationToken).ConfigureAwait(false);
                    return;
                }

                var rawPath = item.GetString();
                if (string.IsNullOrWhiteSpace(rawPath) || !Path.IsPathFullyQualified(rawPath))
                {
                    await writeErrorAsync(id, -32600, $"Invalid request: cwd `{rawPath}` 必须是绝对路径。", cancellationToken).ConfigureAwait(false);
                    return;
                }

                cwds.Add(Path.GetFullPath(rawPath));
            }
        }

        try
        {
            var result = await pluginsManager.ListAsync(new KernelPluginListRequest(cwds, forceRemoteSync), cancellationToken).ConfigureAwait(false);
            await writeResultAsync(
                    id,
                    new
                    {
                        marketplaces = result.Marketplaces.Select(static marketplace => new
                        {
                            name = marketplace.Name,
                            path = marketplace.MarketplacePath,
                            plugins = marketplace.Plugins,
                        }),
                        remoteSyncError = result.RemoteSyncError,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (KernelPluginInstallException ex) when (ex.InvalidRequest)
        {
            await writeErrorAsync(id, -32600, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (KernelPluginInstallException ex)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandlePluginInstallAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var marketplacePath = ReadString(@params, "marketplacePath") ?? ReadString(@params, "marketplace_path");
        var pluginName = ReadString(@params, "pluginName") ?? ReadString(@params, "plugin_name");
        var cwd = ReadString(@params, "cwd");
        if (string.IsNullOrWhiteSpace(marketplacePath) || string.IsNullOrWhiteSpace(pluginName))
        {
            await writeErrorAsync(id, -32602, "marketplacePath/pluginName 必填。", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var installResult = await pluginsManager.InstallAsync(
                new KernelPluginInstallRequest(marketplacePath!, pluginName!, cwd),
                cancellationToken).ConfigureAwait(false);
            var appsNeedingAuth = await LoadPluginAppsNeedingAuthAsync(installResult.InstalledPath, cancellationToken).ConfigureAwait(false);
            pluginsManager.ClearCache();
            skillsManager.ClearCache();
            ClearAppListCache();
            await writeResultAsync(id, new { authPolicy = installResult.AuthPolicy, appsNeedingAuth }, cancellationToken).ConfigureAwait(false);
        }
        catch (KernelPluginInstallException ex) when (ex.InvalidRequest)
        {
            await writeErrorAsync(id, -32600, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (KernelPluginInstallException ex)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandlePluginReadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var marketplacePath = ReadString(@params, "marketplacePath") ?? ReadString(@params, "marketplace_path");
        var pluginName = ReadString(@params, "pluginName") ?? ReadString(@params, "plugin_name");
        if (string.IsNullOrWhiteSpace(marketplacePath) || string.IsNullOrWhiteSpace(pluginName))
        {
            await writeErrorAsync(id, -32602, "marketplacePath/pluginName 必填。", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var plugin = await pluginsManager.ReadAsync(
                new KernelPluginReadRequest(marketplacePath!, pluginName!),
                cancellationToken).ConfigureAwait(false);
            await writeResultAsync(id, new { plugin }, cancellationToken).ConfigureAwait(false);
        }
        catch (KernelPluginInstallException ex) when (ex.InvalidRequest)
        {
            await writeErrorAsync(id, -32600, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (KernelPluginInstallException ex)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandlePluginUninstallAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var pluginId = ReadString(@params, "pluginId") ?? ReadString(@params, "plugin_id");
        var cwd = ReadString(@params, "cwd");
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            await writeErrorAsync(id, -32602, "pluginId 必填。", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await pluginsManager.UninstallAsync(new KernelPluginUninstallRequest(pluginId!, cwd), cancellationToken).ConfigureAwait(false);
            pluginsManager.ClearCache();
            skillsManager.ClearCache();
            ClearAppListCache();
            await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        }
        catch (KernelPluginInstallException ex) when (ex.InvalidRequest)
        {
            await writeErrorAsync(id, -32600, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (KernelPluginInstallException ex)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleAppListAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var cursor = ReadString(@params, "cursor");
        var limit = Math.Max(1, ReadInt(@params, "limit") ?? int.MaxValue);
        var threadId = Normalize(ReadString(@params, "threadId"));
        var forceRefetch = ReadBool(@params, "forceRefetch") ?? false;
        string? threadCwd = null;
        if (!string.IsNullOrWhiteSpace(threadId))
        {
            var threadLookup = await loadThreadCwdAsync(threadId!, cancellationToken).ConfigureAwait(false);
            if (!threadLookup.Found)
            {
                await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
                return;
            }

            threadCwd = threadLookup.Cwd;
        }

        var start = 0;
        if (!string.IsNullOrWhiteSpace(cursor) && !int.TryParse(cursor, out start))
        {
            await writeErrorAsync(id, -32600, $"invalid cursor: {cursor}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (start < 0)
        {
            await writeErrorAsync(id, -32600, $"invalid cursor: {cursor}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var appInfo = await LoadAppsAsync(forceRefetch, threadCwd, cancellationToken).ConfigureAwait(false);
        if (start > appInfo.Count)
        {
            await writeErrorAsync(id, -32600, $"cursor {start} exceeds total apps {appInfo.Count}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var end = Math.Min(start + limit, appInfo.Count);
        var data = appInfo.Skip(start).Take(end - start).ToArray();
        var nextCursor = end < appInfo.Count ? end.ToString() : null;
        await writeResultAsync(id, new
        {
            data,
            nextCursor,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleSkillsListAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var cwds = KernelToolJsonHelpers.ReadStringArray(@params, "cwds");
        if (cwds.Count == 0)
        {
            cwds.Add(Environment.CurrentDirectory);
        }

        var extraRootsByCwd = TryReadExtraSkillRoots(@params, cwds, out var skillRootError);
        if (!string.IsNullOrWhiteSpace(skillRootError))
        {
            await writeErrorAsync(id, -32600, skillRootError!, cancellationToken).ConfigureAwait(false);
            return;
        }

        var forceReload = ReadBool(@params, "forceReload") ?? false;
        var data = new List<object>(cwds.Count);
        foreach (var cwd in cwds)
        {
            var normalizedCwd = string.IsNullOrWhiteSpace(cwd) ? Environment.CurrentDirectory : Path.GetFullPath(cwd);
            var extraRoots = extraRootsByCwd.TryGetValue(normalizedCwd, out var roots)
                ? roots
                : Array.Empty<string>();
            var scan = await skillsManager.ScanAsync(normalizedCwd, extraRoots, forceReload, cancellationToken).ConfigureAwait(false);
            data.Add(new
            {
                cwd = normalizedCwd,
                skills = scan.Skills,
                errors = scan.Errors,
            });
        }

        await writeResultAsync(id, new { data }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleSkillsRemoteListAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        _ = ReadString(@params, "hazelnutScope");
        _ = ReadString(@params, "productSurface");
        _ = ReadBool(@params, "enabled");
        await writeErrorAsync(
            id,
            -32600,
            "skills/remote/list 依赖账号能力，当前内核未启用账号功能。",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleSkillsRemoteExportAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var hazelnutId = Normalize(ReadString(@params, "hazelnutId"));
        if (string.IsNullOrWhiteSpace(hazelnutId))
        {
            await writeErrorAsync(id, -32602, "hazelnutId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        await writeErrorAsync(
            id,
            -32600,
            "skills/remote/export 依赖账号能力，当前内核未启用账号功能。",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleSkillsConfigWriteAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var path = ReadString(@params, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            await writeErrorAsync(id, -32602, "path 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var enabled = ReadBool(@params, "enabled");
        var cwd = ReadString(@params, "cwd");
        if (enabled is null)
        {
            await writeErrorAsync(id, -32602, "enabled 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var effectiveEnabled = await skillsManager.WriteEnabledAsync(path!, enabled.Value, cwd, cancellationToken).ConfigureAwait(false);
        await writeResultAsync(id, new
        {
            effectiveEnabled,
        }, cancellationToken).ConfigureAwait(false);
        await writeNotificationAsync("skills/changed", new { }, cancellationToken).ConfigureAwait(false);
    }

    public static bool AreConnectorsEnabled(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return false;
        }

        var apps = ParseTomlBooleanSection(configText, "apps");
        return apps.TryGetValue("enabled", out var appsEnabled) && appsEnabled;
    }

    public static string? ReadConfiguredChatGptBaseUrl(string? configText)
        => OpenAiAppCatalogCompatibilityAdapter.TryReadConfiguredBaseUrl(configText);

    public async Task<KernelChatGptAuthContext?> ReadChatGptAuthContextAsync(CancellationToken cancellationToken)
    {
        var auth = await OpenAiAppCatalogCompatibilityAdapter
            .TryReadAuthContextAsync(ResolveTianShuHomePath(), cancellationToken)
            .ConfigureAwait(false);
        return auth.HasValue
            ? new KernelChatGptAuthContext(auth.Value.AccessToken, auth.Value.AccountId)
            : null;
    }

    public async Task<object[]> LoadPluginAppsNeedingAuthAsync(string installedPath, CancellationToken cancellationToken)
    {
        var pluginAppIds = ReadPluginAppIds(installedPath);
        if (pluginAppIds.Count == 0)
        {
            return [];
        }

        var configText = await ReadTianShuConfigTextAsync(cancellationToken).ConfigureAwait(false);
        if (!AreConnectorsEnabled(configText))
        {
            return [];
        }

        var auth = await ReadChatGptAuthContextAsync(cancellationToken).ConfigureAwait(false);
        if (auth is null)
        {
            return [];
        }

        var baseUrl = ReadConfiguredChatGptBaseUrl(configText) ?? string.Empty;
        var allConnectors = await FetchAllConnectorsAsync(baseUrl, auth, pluginAppIds, cancellationToken).ConfigureAwait(false);
        var accessibleResult = await TryFetchAccessibleConnectorsAsync(baseUrl, auth, cancellationToken).ConfigureAwait(false);
        if (!accessibleResult.IsReady)
        {
            return [];
        }

        var pluginIdSet = new HashSet<string>(pluginAppIds, StringComparer.OrdinalIgnoreCase);
        var accessibleIds = accessibleResult.Connectors
            .Select(static connector => connector.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return allConnectors
            .Where(connector => pluginIdSet.Contains(connector.Id) && !accessibleIds.Contains(connector.Id))
            .Select(connector => (object)new
            {
                id = connector.Id,
                name = connector.Name,
                description = connector.Description,
                installUrl = connector.InstallUrl,
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<KernelRemotePluginState>> SyncRemotePluginStatesAsync(CancellationToken cancellationToken)
    {
        var configText = await ReadTianShuConfigTextAsync(cancellationToken).ConfigureAwait(false);
        if (!ArePluginsEnabled(configText))
        {
            return Array.Empty<KernelRemotePluginState>();
        }

        var auth = await ReadChatGptAuthContextAsync(cancellationToken).ConfigureAwait(false);
        if (auth is null)
        {
            throw new InvalidOperationException("chatgpt authentication required");
        }

        var baseUrl = ReadConfiguredChatGptBaseUrl(configText) ?? string.Empty;
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildChatGptUri(baseUrl, "backend-api/plugins/list"));
        ApplyChatGptAuthHeaders(request.Headers, auth);
        using var response = await providerHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"remote plugin sync failed with HTTP {(int)response.StatusCode}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("remote plugin sync response must be an array");
        }

        var states = new List<KernelRemotePluginState>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var pluginName = Normalize(ReadString(item, "name"));
            var marketplaceName = Normalize(ReadString(item, "marketplace_name") ?? ReadString(item, "marketplaceName"));
            if (string.IsNullOrWhiteSpace(pluginName) || string.IsNullOrWhiteSpace(marketplaceName))
            {
                continue;
            }

            states.Add(new KernelRemotePluginState(
                $"{pluginName}@{marketplaceName}",
                ReadBool(item, "enabled") ?? false));
        }

        return states;
    }

    public async Task<IReadOnlyList<KernelToolSuggestConnectorInfo>> LoadToolSuggestDiscoverableConnectorsAsync(CancellationToken cancellationToken)
    {
        var configText = await ReadTianShuConfigTextAsync(cancellationToken).ConfigureAwait(false);
        if (!AreConnectorsEnabled(configText))
        {
            return Array.Empty<KernelToolSuggestConnectorInfo>();
        }

        var auth = await ReadChatGptAuthContextAsync(cancellationToken).ConfigureAwait(false);
        if (auth is null)
        {
            return Array.Empty<KernelToolSuggestConnectorInfo>();
        }

        var baseUrl = ReadConfiguredChatGptBaseUrl(configText) ?? string.Empty;
        var allConnectors = await FetchAllConnectorsAsync(baseUrl, auth, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
        var accessibleSnapshot = await TryFetchAccessibleToolsSnapshotAsync(baseUrl, auth, cancellationToken).ConfigureAwait(false);
        var accessibleIds = accessibleSnapshot.AccessibleConnectors
            .Select(static connector => connector.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allConnectors
            .Where(static connector => !string.IsNullOrWhiteSpace(connector.Id))
            .Where(connector => OpenAiAppCatalogCompatibilityAdapter.IsToolSuggestDiscoverableConnector(connector.Id))
            .Where(connector => !accessibleIds.Contains(connector.Id))
            .Where(static connector => !string.IsNullOrWhiteSpace(connector.InstallUrl))
            .Select(static connector => new KernelToolSuggestConnectorInfo(
                connector.Id,
                connector.Name,
                connector.Description,
                connector.InstallUrl))
            .OrderBy(static connector => connector.Name, StringComparer.Ordinal)
            .ThenBy(static connector => connector.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<KernelOpenAiAppsToolSnapshot> RefreshOpenAiAppsToolSnapshotAsync(CancellationToken cancellationToken)
    {
        var configText = await ReadTianShuConfigTextAsync(cancellationToken).ConfigureAwait(false);
        if (!AreConnectorsEnabled(configText))
        {
            return new KernelOpenAiAppsToolSnapshot(null, Array.Empty<KernelToolSuggestConnectorInfo>());
        }

        var auth = await ReadChatGptAuthContextAsync(cancellationToken).ConfigureAwait(false);
        if (auth is null)
        {
            return new KernelOpenAiAppsToolSnapshot(null, Array.Empty<KernelToolSuggestConnectorInfo>());
        }

        var baseUrl = ReadConfiguredChatGptBaseUrl(configText) ?? string.Empty;
        var snapshot = await TryFetchAccessibleToolsSnapshotAsync(baseUrl, auth, cancellationToken).ConfigureAwait(false);
        return new KernelOpenAiAppsToolSnapshot(
            KernelDynamicToolResolver.Clone(snapshot.DynamicTools),
            snapshot.AccessibleConnectors
                .Select(static connector => new KernelToolSuggestConnectorInfo(
                    connector.Id,
                    connector.Name,
                    connector.Description,
                    connector.InstallUrl))
                .OrderBy(static connector => connector.Name, StringComparer.Ordinal)
                .ThenBy(static connector => connector.Id, StringComparer.Ordinal)
                .ToArray());
    }

    public async Task<List<ControlPlaneAppDescriptor>> LoadAppsAsync(bool forceRefetch, string? cwd, CancellationToken cancellationToken)
    {
        var configState = await LoadAppConfigStateAsync(cancellationToken, cwd).ConfigureAwait(false);
        var pluginDisplayNames = await pluginsManager.GetEffectiveAppPluginDisplayNamesAsync(cancellationToken).ConfigureAwait(false);
        var pluginAppIds = await pluginsManager.GetEffectiveAppIdsAsync(cancellationToken).ConfigureAwait(false);
        var configText = await loadMergedPersistedConfigTextAsync(cwd ?? Environment.CurrentDirectory, cancellationToken).ConfigureAwait(false);
        if (!AreConnectorsEnabled(configText))
        {
            return BuildAppsFromConfig(configState, pluginDisplayNames, pluginAppIds);
        }

        var auth = await ReadChatGptAuthContextAsync(cancellationToken).ConfigureAwait(false);
        if (auth is null)
        {
            return BuildAppsFromConfig(configState, pluginDisplayNames, pluginAppIds);
        }

        var baseUrl = ReadConfiguredChatGptBaseUrl(configText) ?? string.Empty;
        IReadOnlyList<KernelPluginConnectorInfo>? cachedDirectory;
        IReadOnlyList<KernelPluginConnectorInfo>? cachedAccessible;
        lock (appListStateGate)
        {
            cachedDirectory = cachedAppDirectoryConnectors;
            cachedAccessible = cachedAccessibleAppConnectors;
        }

        var directoryConnectors = cachedDirectory;
        var accessibleConnectors = cachedAccessible;
        var accessibleLoaded = false;
        var directoryLoaded = false;
        List<ControlPlaneAppDescriptor>? lastNotifiedApps = null;

        async Task EmitIfNeededAsync(IReadOnlyList<KernelPluginConnectorInfo>? directorySnapshot, IReadOnlyList<KernelPluginConnectorInfo>? accessibleSnapshot)
        {
            var merged = BuildAppsFromConnectors(directorySnapshot, accessibleSnapshot, configState, pluginDisplayNames, pluginAppIds);
            if (!ShouldSendAppListUpdatedNotification(merged, accessibleLoaded, directoryLoaded)
                || AppListsEqual(lastNotifiedApps, merged))
            {
                return;
            }

            await writeNotificationAsync("app/list/updated", new { data = merged }, cancellationToken).ConfigureAwait(false);
            lastNotifiedApps = merged;
        }

        if (directoryConnectors is not null || accessibleConnectors is not null)
        {
            await EmitIfNeededAsync(directoryConnectors, accessibleConnectors).ConfigureAwait(false);
        }

        var directoryTask = FetchAllConnectorsAsync(baseUrl, auth, pluginAppIds, cancellationToken);
        var accessibleTask = TryFetchAccessibleConnectorsAsync(baseUrl, auth, cancellationToken);
        var pendingTasks = new HashSet<Task> { directoryTask, accessibleTask };

        while (pendingTasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(pendingTasks).ConfigureAwait(false);
            pendingTasks.Remove(completedTask);

            if (completedTask == directoryTask)
            {
                directoryConnectors = await directoryTask.ConfigureAwait(false);
                directoryLoaded = true;
            }
            else
            {
                var accessibleResult = await accessibleTask.ConfigureAwait(false);
                if (!accessibleResult.IsReady)
                {
                    throw new InvalidOperationException("failed to load accessible apps.");
                }

                accessibleConnectors = accessibleResult.Connectors;
                accessibleLoaded = true;
            }

            var showingInterimForceRefetch = forceRefetch && !(accessibleLoaded && directoryLoaded);
            var directoryForUpdate = showingInterimForceRefetch && cachedDirectory is not null
                ? cachedDirectory
                : directoryConnectors;
            var accessibleForUpdate = showingInterimForceRefetch && !accessibleLoaded
                ? null
                : accessibleConnectors;
            await EmitIfNeededAsync(directoryForUpdate, accessibleForUpdate).ConfigureAwait(false);

            if (accessibleLoaded && directoryLoaded)
            {
                lock (appListStateGate)
                {
                    cachedAppDirectoryConnectors = directoryConnectors;
                    cachedAccessibleAppConnectors = accessibleConnectors;
                }

                return BuildAppsFromConnectors(directoryConnectors, accessibleConnectors, configState, pluginDisplayNames, pluginAppIds);
            }
        }

        return BuildAppsFromConnectors(directoryConnectors, accessibleConnectors, configState, pluginDisplayNames, pluginAppIds);
    }

    public async Task<List<KernelPluginConnectorInfo>> FetchAllConnectorsAsync(
        string baseUrl,
        KernelChatGptAuthContext auth,
        IReadOnlyCollection<string> pluginAppIds,
        CancellationToken cancellationToken)
    {
        var connectors = new List<KernelPluginConnectorInfo>();
        try
        {
            connectors.AddRange(await FetchDirectoryConnectorsAsync(baseUrl, auth, includeWorkspace: false, cancellationToken).ConfigureAwait(false));
            connectors.AddRange(await FetchDirectoryConnectorsAsync(baseUrl, auth, includeWorkspace: true, cancellationToken).ConfigureAwait(false));
        }
        catch
        {
        }

        return MergePluginApps(connectors, pluginAppIds)
            .Where(static connector => !OpenAiAppCatalogCompatibilityAdapter.IsDisallowedConnector(connector.Id))
            .OrderBy(static connector => connector.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static connector => connector.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<KernelAccessibleConnectorsResult> TryFetchAccessibleConnectorsAsync(
        string baseUrl,
        KernelChatGptAuthContext auth,
        CancellationToken cancellationToken)
    {
        var snapshot = await TryFetchAccessibleToolsSnapshotAsync(baseUrl, auth, cancellationToken).ConfigureAwait(false);
        return new KernelAccessibleConnectorsResult(snapshot.IsReady, snapshot.AccessibleConnectors);
    }

    private static bool ArePluginsEnabled(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return false;
        }

        var plugins = ParseTomlBooleanSection(configText, "plugins");
        return plugins.TryGetValue("enabled", out var pluginsEnabled) && pluginsEnabled;
    }

    private async Task<KernelAppConfigState> LoadAppConfigStateAsync(CancellationToken cancellationToken, string? cwd)
    {
        var values = await loadEffectiveConfigValuesAsync(cwd, cancellationToken).ConfigureAwait(false);
        return BuildAppConfigState(values);
    }

    private static IReadOnlyList<string> ReadPluginAppIds(string pluginRoot)
    {
        var configPath = Path.Combine(pluginRoot, ".app.json");
        if (!File.Exists(configPath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("apps", out var appsElement)
                || appsElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var ids = new List<string>();
            foreach (var property in appsElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var appId = ReadString(property.Value, "id");
                if (!string.IsNullOrWhiteSpace(appId))
                {
                    ids.Add(appId!);
                }
            }

            return ids;
        }
        catch
        {
            return [];
        }
    }

    private async Task<List<KernelPluginConnectorInfo>> FetchDirectoryConnectorsAsync(
        string baseUrl,
        KernelChatGptAuthContext auth,
        bool includeWorkspace,
        CancellationToken cancellationToken)
    {
        var connectors = new List<KernelPluginConnectorInfo>();
        string? nextToken = null;
        do
        {
            var relativePath = includeWorkspace
                ? "connectors/directory/list_workspace?external_logos=true"
                : nextToken is null
                    ? "connectors/directory/list?tier=categorized&external_logos=true"
                    : $"connectors/directory/list?tier=categorized&token={Uri.EscapeDataString(nextToken)}&external_logos=true";
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildChatGptUri(baseUrl, relativePath));
            ApplyChatGptAuthHeaders(request.Headers, auth);
            using var response = await providerHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (includeWorkspace)
                {
                    return [];
                }

                throw new InvalidOperationException($"directory connectors request failed with HTTP {(int)response.StatusCode}");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("directory connectors response must be an object");
            }

            if (document.RootElement.TryGetProperty("apps", out var appsElement) && appsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var appElement in appsElement.EnumerateArray())
                {
                    if (IsHiddenDirectoryApp(appElement))
                    {
                        continue;
                    }

                    var connector = ParseConnectorInfo(appElement);
                    if (connector is not null)
                    {
                        connectors.Add(connector);
                    }
                }
            }

            nextToken = includeWorkspace
                ? null
                : Normalize(ReadString(document.RootElement, "nextToken") ?? ReadString(document.RootElement, "next_token"));
        }
        while (!includeWorkspace && !string.IsNullOrWhiteSpace(nextToken));

        return connectors;
    }

    private async Task<KernelAccessibleToolsSnapshot> TryFetchAccessibleToolsSnapshotAsync(
        string baseUrl,
        KernelChatGptAuthContext auth,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildChatGptUri(baseUrl, OpenAiAppCatalogCompatibilityAdapter.CodexAppsMcpPath);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = $"Bearer {auth.AccessToken}",
            [OpenAiAppCatalogCompatibilityKeys.ChatGptAccountIdHeaderName] = auth.AccountId,
        };

        await using var client = new KernelMcpStreamableHttpClient(
            endpoint,
            providerHttpClient,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            headers);
        try
        {
            var result = await client.SendRequestAsync("tools/list", null, cancellationToken).ConfigureAwait(false);
            if (result.ValueKind != JsonValueKind.Object
                || !result.TryGetProperty("tools", out var toolsElement)
                || toolsElement.ValueKind != JsonValueKind.Array)
            {
                return new KernelAccessibleToolsSnapshot(true, null, []);
            }

            var dynamicTools = KernelDynamicToolResolver.Parse(toolsElement);
            var connectors = new Dictionary<string, KernelPluginConnectorInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var toolElement in toolsElement.EnumerateArray())
            {
                if (toolElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var meta = TryGetObjectProperty(toolElement, "_meta") ?? TryGetObjectProperty(toolElement, "meta");
                if (meta is null)
                {
                    continue;
                }

                var connectorId = ReadString(meta.Value, "connector_id") ?? ReadString(meta.Value, "connectorId");
                if (string.IsNullOrWhiteSpace(connectorId) || OpenAiAppCatalogCompatibilityAdapter.IsDisallowedConnector(connectorId))
                {
                    continue;
                }

                var connectorName = ReadString(meta.Value, "connector_name")
                    ?? ReadString(meta.Value, "connectorName")
                    ?? connectorId;
                var pluginDisplayNames = ReadStringArray(meta.Value, "plugin_display_names", "pluginDisplayNames");
                var labels = ReadStringMap(meta.Value, "labels");
                var connector = new KernelPluginConnectorInfo(
                    connectorId!,
                    connectorName!,
                    null,
                    ReadString(meta.Value, "install_url") ?? ReadString(meta.Value, "installUrl"),
                    pluginDisplayNames,
                    ReadString(meta.Value, "connector_logo") ?? ReadString(meta.Value, "logo_url"),
                    ReadString(meta.Value, "connector_logo_dark") ?? ReadString(meta.Value, "logo_url_dark"),
                    ReadString(meta.Value, "distribution_channel"),
                    TryReadObject(meta.Value, "branding"),
                    TryReadObject(meta.Value, "app_metadata"),
                    labels);
                if (connectors.TryGetValue(connector.Id, out var existing))
                {
                    connectors[connector.Id] = MergeConnector(existing, connector);
                }
                else
                {
                    connectors[connector.Id] = connector;
                }
            }

            return new KernelAccessibleToolsSnapshot(true, dynamicTools, connectors.Values.ToArray());
        }
        catch
        {
            return new KernelAccessibleToolsSnapshot(false, null, []);
        }
    }

    private static IEnumerable<KernelPluginConnectorInfo> MergePluginApps(
        IEnumerable<KernelPluginConnectorInfo> connectors,
        IReadOnlyCollection<string> pluginAppIds)
    {
        var merged = new Dictionary<string, KernelPluginConnectorInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var connector in connectors)
        {
            if (merged.TryGetValue(connector.Id, out var existing))
            {
                merged[connector.Id] = MergeConnector(existing, connector);
            }
            else
            {
                merged[connector.Id] = connector;
            }
        }

        foreach (var pluginAppId in pluginAppIds)
        {
            if (string.IsNullOrWhiteSpace(pluginAppId) || merged.ContainsKey(pluginAppId) || OpenAiAppCatalogCompatibilityAdapter.IsDisallowedConnector(pluginAppId))
            {
                continue;
            }

            merged[pluginAppId] = new KernelPluginConnectorInfo(
                pluginAppId,
                pluginAppId,
                null,
                null,
                [],
                null,
                null,
                null,
                null,
                null,
                null);
        }

        return merged.Values;
    }

    private static KernelPluginConnectorInfo? ParseConnectorInfo(JsonElement appElement)
    {
        if (appElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = ReadString(appElement, "id");
        if (string.IsNullOrWhiteSpace(id) || OpenAiAppCatalogCompatibilityAdapter.IsDisallowedConnector(id))
        {
            return null;
        }

        var name = ReadString(appElement, "name") ?? id;
        var installUrl = ReadString(appElement, "installUrl")
            ?? ReadString(appElement, "install_url");
        return new KernelPluginConnectorInfo(
            id!,
            name!,
            ReadString(appElement, "description"),
            installUrl,
            ReadStringArray(appElement, "pluginDisplayNames", "plugin_display_names"),
            ReadString(appElement, "logoUrl") ?? ReadString(appElement, "logo_url"),
            ReadString(appElement, "logoUrlDark") ?? ReadString(appElement, "logo_url_dark"),
            ReadString(appElement, "distributionChannel") ?? ReadString(appElement, "distribution_channel"),
            TryReadObject(appElement, "branding"),
            TryReadObject(appElement, "appMetadata") ?? TryReadObject(appElement, "app_metadata"),
            ReadStringMap(appElement, "labels"));
    }

    private static bool IsHiddenDirectoryApp(JsonElement appElement)
        => string.Equals(ReadString(appElement, "visibility"), "HIDDEN", StringComparison.OrdinalIgnoreCase);

    private static Uri BuildChatGptUri(string baseUrl, string relativePath)
        => OpenAiAppCatalogCompatibilityAdapter.BuildCatalogUri(baseUrl, relativePath);

    private static void ApplyChatGptAuthHeaders(HttpRequestHeaders headers, KernelChatGptAuthContext auth)
        => OpenAiAppCatalogCompatibilityAdapter.ApplyAuthHeaders(headers, auth.AccessToken, auth.AccountId);

    private static JsonElement? TryGetObjectProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Object)
        {
            return property;
        }

        return null;
    }

    private static object? TryReadObject(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Object)
        {
            return property.Clone();
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string>? ReadStringMap(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in property.EnumerateObject())
        {
            var value = entry.Value.ValueKind == JsonValueKind.String
                ? entry.Value.GetString()
                : Normalize(entry.Value.ToString());
            if (!string.IsNullOrWhiteSpace(value))
            {
                map[entry.Name] = value!;
            }
        }

        return map.Count == 0 ? null : map;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName, string alias)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        JsonElement arrayElement;
        if (element.TryGetProperty(propertyName, out var first))
        {
            arrayElement = first;
        }
        else if (element.TryGetProperty(alias, out var second))
        {
            arrayElement = second;
        }
        else
        {
            return [];
        }

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return arrayElement.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

}
