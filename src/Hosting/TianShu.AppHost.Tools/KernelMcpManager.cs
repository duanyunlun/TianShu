using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.AppHost.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost.Tools;

internal sealed record KernelMcpServerStatus(
    string Name,
    IReadOnlyDictionary<string, object?> Tools,
    IReadOnlyList<object> Resources,
    IReadOnlyList<object> ResourceTemplates,
    string AuthStatus);

internal sealed record KernelMcpReloadResult(
    bool Reloaded,
    int ServerCount,
    IReadOnlyList<KernelMcpServerStatus> Data);

internal sealed record KernelMcpResourceEntry(string Server, JsonElement Resource);

internal sealed record KernelMcpResourceTemplateEntry(string Server, JsonElement Template);

internal sealed record KernelMcpListResourcesResult(
    string? Server,
    IReadOnlyList<KernelMcpResourceEntry> Resources,
    string? NextCursor);

internal sealed record KernelMcpListResourceTemplatesResult(
    string? Server,
    IReadOnlyList<KernelMcpResourceTemplateEntry> ResourceTemplates,
    string? NextCursor);

internal sealed record KernelMcpReadResourceResult(string Server, string Uri, JsonElement Result);

internal sealed record KernelMcpSandboxState(
    JsonElement SandboxPolicy,
    string SandboxCwd,
    string? LinuxSandboxExe = null,
    bool UseLegacyLandlock = false)
{
    public KernelMcpSandboxState DeepClone()
        => new(SandboxPolicy.Clone(), SandboxCwd, LinuxSandboxExe, UseLegacyLandlock);

    public object ToPayload()
    {
        return new
        {
            sandboxPolicy = JsonSerializer.Deserialize<object?>(SandboxPolicy.GetRawText()),
            linuxSandboxExe = Normalize(LinuxSandboxExe),
            sandboxCwd = SandboxCwd,
            useLegacyLandlock = UseLegacyLandlock,
        };
    }

    public string ToJson()
        => JsonSerializer.Serialize(ToPayload());

    public static KernelMcpSandboxState Create(JsonElement? sandboxPolicy, string? sandboxCwd)
    {
        var effectivePolicy = sandboxPolicy is { ValueKind: JsonValueKind.Object } policy
            ? policy.Clone()
            : JsonSerializer.SerializeToElement(new
            {
                type = "workspaceWrite",
            });
        var effectiveCwd = Path.GetFullPath(
            string.IsNullOrWhiteSpace(Normalize(sandboxCwd))
                ? Environment.CurrentDirectory
                : sandboxCwd!);
        return new KernelMcpSandboxState(effectivePolicy, effectiveCwd);
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
}

internal sealed class KernelMcpManager
{
    private static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultToolTimeout = TimeSpan.FromSeconds(120);
    internal const string McpSandboxStateCapability = "tianshu/sandbox-state";
    internal const string McpSandboxStateMethod = "tianshu/sandbox-state/update";

    private readonly string homePath;
    private readonly Func<CancellationToken, Task<Dictionary<string, string>>> loadConfigOverridesAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyDictionary<string, KernelPluginMcpServerDefinition>>>? loadPluginMcpServersAsync;
    private readonly Func<string, string?> readEnvironmentVariable;
    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<string, Lazy<Task<IKernelMcpClient>>> clientCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sandboxStateGate = new();
    private KernelMcpSandboxState? currentSandboxState;

    public KernelMcpManager(
        Func<CancellationToken, Task<Dictionary<string, string>>> loadConfigOverridesAsync,
        string? tianShuHome = null,
        HttpClient? httpClient = null,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, KernelPluginMcpServerDefinition>>>? loadPluginMcpServersAsync = null,
        Func<string, string?>? readEnvironmentVariable = null)
    {
        this.loadConfigOverridesAsync = loadConfigOverridesAsync;
        homePath = NormalizePath(tianShuHome) ?? TianShuHomePathUtilities.ResolveTianShuHomePath();
        this.loadPluginMcpServersAsync = loadPluginMcpServersAsync;
        this.readEnvironmentVariable = readEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        this.httpClient = httpClient ?? KernelCustomCaSupport.CreateHttpClient(Timeout.InfiniteTimeSpan);
    }

    public async Task<IReadOnlyList<string>> ListServerNamesAsync(CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in KernelMcpServerPackageManifestLoader.ListServerNames(homePath))
        {
            names.Add(name);
        }

        var configToml = Path.Combine(homePath, "tianshu.toml");
        foreach (var name in ReadTomlSectionNames(configToml, "mcp_servers"))
        {
            names.Add(name);
        }

        var values = await loadConfigOverridesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var key in values.Keys)
        {
            if (TryParseScopedConfigKey(key, "mcp_servers", out var name, out _))
            {
                names.Add(name);
            }
        }

        if (loadPluginMcpServersAsync is not null)
        {
            foreach (var name in (await loadPluginMcpServersAsync(cancellationToken).ConfigureAwait(false)).Keys)
            {
                names.Add(name);
            }
        }

        return names.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<KernelMcpServerStatus>> BuildStatusDataAsync(
        IEnumerable<string> names,
        CancellationToken cancellationToken)
    {
        var values = await loadConfigOverridesAsync(cancellationToken).ConfigureAwait(false);
        var resolvedConfigs = await LoadResolvedServerConfigsAsync(cancellationToken).ConfigureAwait(false);
        var statuses = new List<KernelMcpServerStatus>();
        foreach (var name in names)
        {
            var tools = resolvedConfigs.TryGetValue(name, out var resolved)
                ? await TryReadStatusToolsAsync(resolved, cancellationToken).ConfigureAwait(false)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            var resources = resolved is not null
                ? await TryReadStatusResourcesAsync(resolved, cancellationToken).ConfigureAwait(false)
                : Array.Empty<object>();
            var resourceTemplates = resolved is not null
                ? await TryReadStatusResourceTemplatesAsync(resolved, cancellationToken).ConfigureAwait(false)
                : Array.Empty<object>();

            statuses.Add(new KernelMcpServerStatus(
                Name: name,
                Tools: tools,
                Resources: resources,
                ResourceTemplates: resourceTemplates,
                AuthStatus: ResolveMcpServerAuthStatus(name, resolved, values)));
        }

        return statuses;
    }

    public async Task<KernelMcpReloadResult> ReloadAsync(CancellationToken cancellationToken)
    {
        await ResetClientsAsync().ConfigureAwait(false);
        var names = await ListServerNamesAsync(cancellationToken).ConfigureAwait(false);
        var data = await BuildStatusDataAsync(names, cancellationToken).ConfigureAwait(false);
        return new KernelMcpReloadResult(true, names.Count, data);
    }

    public async Task UpdateSandboxStateAsync(KernelMcpSandboxState sandboxState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sandboxState);

        lock (sandboxStateGate)
        {
            currentSandboxState = sandboxState.DeepClone();
        }

        foreach (var (serverName, lazy) in clientCache.ToArray())
        {
            if (!lazy.IsValueCreated)
            {
                continue;
            }

            try
            {
                var client = await lazy.Value.ConfigureAwait(false);
                await client.SendSandboxStateAsync(sandboxState, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await InvalidateClientAsync(serverName).ConfigureAwait(false);
            }
        }
    }

    public async Task EnsureRequiredServersInitializedAsync(
        KernelMcpSandboxState sandboxState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sandboxState);

        var requiredServers = (await LoadResolvedServerConfigsAsync(cancellationToken).ConfigureAwait(false)).Values
            .Where(static config => config.Required)
            .OrderBy(static config => config.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requiredServers.Length == 0)
        {
            return;
        }

        var failures = new List<string>();
        foreach (var requiredServer in requiredServers)
        {
            try
            {
                var client = await GetClientAsync(requiredServer, cancellationToken).ConfigureAwait(false);
                await client.SendSandboxStateAsync(sandboxState, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await InvalidateClientAsync(requiredServer.Name).ConfigureAwait(false);
                var detail = Normalize(ex.Message);
                failures.Add(detail is null ? requiredServer.Name : $"{requiredServer.Name} ({detail})");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"required MCP servers failed to initialize: {string.Join(", ", failures)}");
        }
    }

    public async Task<KernelMcpListResourcesResult> ListResourcesAsync(
        string? server,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var serverName = Normalize(server);
        var nextCursor = Normalize(cursor);
        if (serverName is null)
        {
            if (nextCursor is not null)
            {
                throw new InvalidOperationException("cursor can only be used when a server is specified");
            }

            var configs = await LoadResolvedServerConfigsAsync(cancellationToken).ConfigureAwait(false);
            var resources = new List<KernelMcpResourceEntry>();
            foreach (var config in configs.Values.OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var client = await GetClientAsync(config, cancellationToken).ConfigureAwait(false);
                    resources.AddRange(await ReadAllResourcePagesAsync(config.Name, client, cancellationToken).ConfigureAwait(false));
                }
                catch
                {
                    await InvalidateClientAsync(config.Name).ConfigureAwait(false);
                }
            }

            return new KernelMcpListResourcesResult(null, resources, null);
        }

        var resolved = await GetRequiredServerConfigAsync(serverName, cancellationToken).ConfigureAwait(false);
        try
        {
            var client = await GetClientAsync(resolved, cancellationToken).ConfigureAwait(false);
            var result = await client.SendRequestAsync(
                "resources/list",
                nextCursor is null ? null : new { cursor = nextCursor },
                cancellationToken).ConfigureAwait(false);
            return new KernelMcpListResourcesResult(serverName, ReadResourceEntries(serverName, result), ReadOptionalCursor(result));
        }
        catch
        {
            await InvalidateClientAsync(serverName).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<KernelMcpListResourceTemplatesResult> ListResourceTemplatesAsync(
        string? server,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var serverName = Normalize(server);
        var nextCursor = Normalize(cursor);
        if (serverName is null)
        {
            if (nextCursor is not null)
            {
                throw new InvalidOperationException("cursor can only be used when a server is specified");
            }

            var configs = await LoadResolvedServerConfigsAsync(cancellationToken).ConfigureAwait(false);
            var templates = new List<KernelMcpResourceTemplateEntry>();
            foreach (var config in configs.Values.OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var client = await GetClientAsync(config, cancellationToken).ConfigureAwait(false);
                    templates.AddRange(await ReadAllTemplatePagesAsync(config.Name, client, cancellationToken).ConfigureAwait(false));
                }
                catch
                {
                    await InvalidateClientAsync(config.Name).ConfigureAwait(false);
                }
            }

            return new KernelMcpListResourceTemplatesResult(null, templates, null);
        }

        var resolved = await GetRequiredServerConfigAsync(serverName, cancellationToken).ConfigureAwait(false);
        try
        {
            var client = await GetClientAsync(resolved, cancellationToken).ConfigureAwait(false);
            var result = await client.SendRequestAsync(
                "resources/templates/list",
                nextCursor is null ? null : new { cursor = nextCursor },
                cancellationToken).ConfigureAwait(false);
            return new KernelMcpListResourceTemplatesResult(serverName, ReadTemplateEntries(serverName, result), ReadOptionalCursor(result));
        }
        catch
        {
            await InvalidateClientAsync(serverName).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<KernelMcpReadResourceResult> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken)
    {
        var serverName = Normalize(server) ?? throw new InvalidOperationException("server is required");
        var normalizedUri = Normalize(uri) ?? throw new InvalidOperationException("uri is required");
        var resolved = await GetRequiredServerConfigAsync(serverName, cancellationToken).ConfigureAwait(false);
        try
        {
            var client = await GetClientAsync(resolved, cancellationToken).ConfigureAwait(false);
            var result = await client.SendRequestAsync("resources/read", new { uri = normalizedUri }, cancellationToken).ConfigureAwait(false);
            return new KernelMcpReadResourceResult(serverName, normalizedUri, result);
        }
        catch
        {
            await InvalidateClientAsync(serverName).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<KernelMcpResolvedServerConfig> GetRequiredServerConfigAsync(string serverName, CancellationToken cancellationToken)
    {
        var configs = await LoadResolvedServerConfigsAsync(cancellationToken).ConfigureAwait(false);
        if (!configs.TryGetValue(serverName, out var resolved))
        {
            throw new InvalidOperationException($"MCP server `{serverName}` is not configured.");
        }

        return resolved;
    }

    private async Task<IReadOnlyDictionary<string, KernelMcpResolvedServerConfig>> LoadResolvedServerConfigsAsync(CancellationToken cancellationToken)
    {
        var builders = LoadPackageServerConfigs(homePath);
        if (loadPluginMcpServersAsync is not null)
        {
            var pluginServers = await loadPluginMcpServersAsync(cancellationToken).ConfigureAwait(false);
            ApplyPluginDefinitions(builders, pluginServers);
        }

        ApplyTomlServerConfigs(builders, Path.Combine(homePath, "tianshu.toml"));
        var overrides = await loadConfigOverridesAsync(cancellationToken).ConfigureAwait(false);
        ApplyOverrides(builders, overrides);

        return builders.Values
            .Select(static builder => builder.Build())
            .Where(static config => config is not null && config.Enabled)
            .ToDictionary(static config => config!.Name, static config => config!, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IKernelMcpClient> GetClientAsync(KernelMcpResolvedServerConfig resolved, CancellationToken cancellationToken)
    {
        var lazy = clientCache.GetOrAdd(
            resolved.Name,
            _ => new Lazy<Task<IKernelMcpClient>>(() => CreateClientAsync(resolved), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var client = await lazy.Value.ConfigureAwait(false);
            var sandboxState = GetCurrentSandboxState();
            if (sandboxState is not null)
            {
                await client.SendSandboxStateAsync(sandboxState, cancellationToken).ConfigureAwait(false);
            }

            return client;
        }
        catch
        {
            clientCache.TryRemove(resolved.Name, out _);
            throw;
        }
    }

    private Task<IKernelMcpClient> CreateClientAsync(KernelMcpResolvedServerConfig resolved)
    {
        IKernelMcpClient client = resolved.Transport switch
        {
            KernelMcpStdioTransportConfig stdio => new KernelMcpStdioClient(
                resolved.Name,
                stdio.Command,
                stdio.Args,
                stdio.Cwd,
                stdio.Env,
                stdio.EnvVars,
                readEnvironmentVariable,
                resolved.StartupTimeout,
                resolved.ToolTimeout),
            KernelMcpStreamableHttpTransportConfig streamableHttp => new KernelMcpStreamableHttpClient(
                new Uri(streamableHttp.Url, UriKind.Absolute),
                httpClient,
                resolved.StartupTimeout,
                resolved.ToolTimeout,
                BuildHttpHeaders(streamableHttp)),
            _ => throw new InvalidOperationException($"Unsupported MCP transport for `{resolved.Name}`."),
        };

        return Task.FromResult(client);
    }

    private IReadOnlyDictionary<string, string> BuildHttpHeaders(KernelMcpStreamableHttpTransportConfig config)
    {
        var headers = new Dictionary<string, string>(config.HttpHeaders, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in config.EnvHttpHeaders)
        {
            var value = readEnvironmentVariable(pair.Value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                headers[pair.Key] = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(config.BearerTokenEnvVar))
        {
            var value = readEnvironmentVariable(config.BearerTokenEnvVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                headers["Authorization"] = $"Bearer {value}";
            }
        }

        return headers;
    }

    private async Task<List<KernelMcpResourceEntry>> ReadAllResourcePagesAsync(string serverName, IKernelMcpClient client, CancellationToken cancellationToken)
    {
        var results = new List<KernelMcpResourceEntry>();
        string? cursor = null;
        while (true)
        {
            var page = await client.SendRequestAsync(
                "resources/list",
                cursor is null ? null : new { cursor },
                cancellationToken).ConfigureAwait(false);
            results.AddRange(ReadResourceEntries(serverName, page));
            var nextCursor = ReadOptionalCursor(page);
            if (nextCursor is null)
            {
                return results;
            }

            if (string.Equals(cursor, nextCursor, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("resources/list returned duplicate cursor");
            }

            cursor = nextCursor;
        }
    }

    private async Task<List<KernelMcpResourceTemplateEntry>> ReadAllTemplatePagesAsync(string serverName, IKernelMcpClient client, CancellationToken cancellationToken)
    {
        var results = new List<KernelMcpResourceTemplateEntry>();
        string? cursor = null;
        while (true)
        {
            var page = await client.SendRequestAsync(
                "resources/templates/list",
                cursor is null ? null : new { cursor },
                cancellationToken).ConfigureAwait(false);
            results.AddRange(ReadTemplateEntries(serverName, page));
            var nextCursor = ReadOptionalCursor(page);
            if (nextCursor is null)
            {
                return results;
            }

            if (string.Equals(cursor, nextCursor, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("resources/templates/list returned duplicate cursor");
            }

            cursor = nextCursor;
        }
    }

    private async Task<IReadOnlyDictionary<string, object?>> TryReadStatusToolsAsync(
        KernelMcpResolvedServerConfig resolved,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await GetClientAsync(resolved, cancellationToken).ConfigureAwait(false);
            var result = await client.SendRequestAsync("tools/list", null, cancellationToken).ConfigureAwait(false);
            return ApplyToolFilters(ReadToolEntries(result), resolved);
        }
        catch
        {
            await InvalidateClientAsync(resolved.Name).ConfigureAwait(false);
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }
    }

    private async Task<IReadOnlyList<object>> TryReadStatusResourcesAsync(
        KernelMcpResolvedServerConfig resolved,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await GetClientAsync(resolved, cancellationToken).ConfigureAwait(false);
            return (await ReadAllResourcePagesAsync(resolved.Name, client, cancellationToken).ConfigureAwait(false))
                .Select(static entry => (object)entry.Resource.Clone())
                .ToArray();
        }
        catch
        {
            await InvalidateClientAsync(resolved.Name).ConfigureAwait(false);
            return Array.Empty<object>();
        }
    }

    private async Task<IReadOnlyList<object>> TryReadStatusResourceTemplatesAsync(
        KernelMcpResolvedServerConfig resolved,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await GetClientAsync(resolved, cancellationToken).ConfigureAwait(false);
            return (await ReadAllTemplatePagesAsync(resolved.Name, client, cancellationToken).ConfigureAwait(false))
                .Select(static entry => (object)entry.Template.Clone())
                .ToArray();
        }
        catch
        {
            await InvalidateClientAsync(resolved.Name).ConfigureAwait(false);
            return Array.Empty<object>();
        }
    }

    private async Task InvalidateClientAsync(string serverName)
    {
        if (!clientCache.TryRemove(serverName, out var lazy) || !lazy.IsValueCreated)
        {
            return;
        }

        try
        {
            var client = await lazy.Value.ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task ResetClientsAsync()
    {
        foreach (var key in clientCache.Keys.ToArray())
        {
            await InvalidateClientAsync(key).ConfigureAwait(false);
        }
    }

    private KernelMcpSandboxState? GetCurrentSandboxState()
    {
        lock (sandboxStateGate)
        {
            return currentSandboxState?.DeepClone();
        }
    }

    internal static Dictionary<string, object?> ToWireResource(KernelMcpResourceEntry entry)
        => MergeServer(entry.Server, entry.Resource);

    internal static Dictionary<string, object?> ToWireTemplate(KernelMcpResourceTemplateEntry entry)
        => MergeServer(entry.Server, entry.Template);

    internal static Dictionary<string, object?> ToWireReadResult(KernelMcpReadResourceResult result)
    {
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(result.Result.GetRawText())
            ?? new Dictionary<string, object?>();
        payload["server"] = result.Server;
        payload["uri"] = result.Uri;
        return payload;
    }

    private static Dictionary<string, object?> MergeServer(string server, JsonElement payload)
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(payload.GetRawText())
            ?? new Dictionary<string, object?>();
        result["server"] = server;
        return result;
    }

    private static IReadOnlyList<KernelMcpResourceEntry> ReadResourceEntries(string serverName, JsonElement result)
    {
        if (!TryGetArray(result, "resources", out var resources))
        {
            return Array.Empty<KernelMcpResourceEntry>();
        }

        return resources.EnumerateArray().Select(item => new KernelMcpResourceEntry(serverName, item.Clone())).ToArray();
    }

    private static IReadOnlyList<KernelMcpResourceTemplateEntry> ReadTemplateEntries(string serverName, JsonElement result)
    {
        if (!TryGetArray(result, "resourceTemplates", out var templates)
            && !TryGetArray(result, "resource_templates", out templates))
        {
            return Array.Empty<KernelMcpResourceTemplateEntry>();
        }

        return templates.EnumerateArray().Select(item => new KernelMcpResourceTemplateEntry(serverName, item.Clone())).ToArray();
    }

    private static IReadOnlyDictionary<string, object?> ReadToolEntries(JsonElement result)
    {
        if (!TryGetArray(result, "tools", out var tools))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var entries = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var item in tools.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("name", out var nameElement)
                || nameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = Normalize(nameElement.GetString());
            if (!string.IsNullOrWhiteSpace(name))
            {
                entries[name!] = item.Clone();
            }
        }

        return entries;
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement result)
    {
        result = default;
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out result)
               && result.ValueKind == JsonValueKind.Array;
    }

    private static string? ReadOptionalCursor(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (result.TryGetProperty("nextCursor", out var nextCursor) && nextCursor.ValueKind == JsonValueKind.String)
        {
            return Normalize(nextCursor.GetString());
        }

        if (result.TryGetProperty("next_cursor", out var nextCursorSnake) && nextCursorSnake.ValueKind == JsonValueKind.String)
        {
            return Normalize(nextCursorSnake.GetString());
        }

        return null;
    }

    private string ResolveMcpServerAuthStatus(
        string serverName,
        KernelMcpResolvedServerConfig? resolved,
        IReadOnlyDictionary<string, string> values)
    {
        if (HasConfiguredOauthToken(values, serverName))
        {
            return "oauth";
        }

        if (HasConfiguredBearerToken(values, serverName))
        {
            return "bearer_token";
        }

        if (resolved?.Transport is KernelMcpStreamableHttpTransportConfig streamableHttp)
        {
            if (HasBearerTokenEnvironmentValue(streamableHttp))
            {
                return "bearer_token";
            }

            return "not_logged_in";
        }

        return "unsupported";
    }

    private static bool HasConfiguredOauthToken(IReadOnlyDictionary<string, string> values, string serverName)
        => HasConfiguredValue(values, $"mcp_servers.{serverName}.oauth_access_token");

    private static bool HasConfiguredBearerToken(IReadOnlyDictionary<string, string> values, string serverName)
        => HasConfiguredValue(values, $"mcp_servers.{serverName}.access_token")
           || HasConfiguredValue(values, $"mcp_servers.{serverName}.api_key");

    private bool HasBearerTokenEnvironmentValue(KernelMcpStreamableHttpTransportConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BearerTokenEnvVar))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(readEnvironmentVariable(config.BearerTokenEnvVar));
    }

    private static bool HasConfiguredValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(NormalizeRawConfigValue(raw));

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

    private static Dictionary<string, KernelMcpServerConfigBuilder> LoadPackageServerConfigs(string tianShuHomePath)
    {
        var configs = new Dictionary<string, KernelMcpServerConfigBuilder>(StringComparer.OrdinalIgnoreCase);
        ApplyPackageDefinitions(configs, KernelMcpServerPackageManifestLoader.Load(tianShuHomePath));
        return configs;
    }

    private static void ApplyTomlServerConfigs(Dictionary<string, KernelMcpServerConfigBuilder> configs, string configTomlPath)
    {
        if (!File.Exists(configTomlPath))
        {
            return;
        }

        try
        {
            var model = Toml.ToModel(File.ReadAllText(configTomlPath));
            if (!model.TryGetValue("mcp_servers", out var serversValue) || serversValue is not TomlTable servers)
            {
                return;
            }

            foreach (var pair in servers)
            {
                if (pair.Value is not TomlTable server)
                {
                    continue;
                }

                var builder = GetOrCreateBuilder(configs, pair.Key);
                builder.Enabled = ReadTomlBool(server, "enabled") ?? true;
                builder.Required = ReadTomlBool(server, "required") ?? builder.Required;
                builder.Command = ReadTomlString(server, "command");
                builder.Args = ReadTomlStringArray(server, "args");
                builder.Env = ReadTomlStringMap(server, "env");
                builder.EnvVars = ReadTomlStringArray(server, "env_vars");
                builder.Cwd = NormalizePath(ReadTomlString(server, "cwd"));
                builder.Url = ReadTomlString(server, "url");
                builder.BearerTokenEnvVar = ReadTomlString(server, "bearer_token_env_var");
                builder.HttpHeaders = ReadTomlStringMap(server, "http_headers");
                builder.EnvHttpHeaders = ReadTomlStringMap(server, "env_http_headers");
                builder.StartupTimeout = ReadTomlTimeSpan(server, "startup_timeout_sec", "startup_timeout_ms");
                builder.ToolTimeout = ReadTomlTimeSpan(server, "tool_timeout_sec", "tool_timeout_ms");
                builder.EnabledTools = ReadTomlStringArray(server, "enabled_tools");
                builder.DisabledTools = ReadTomlStringArray(server, "disabled_tools");
            }
        }
        catch
        {
        }
    }

    private static void ApplyOverrides(Dictionary<string, KernelMcpServerConfigBuilder> configs, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, rawValue) in values)
        {
            if (!TryParseScopedConfigKey(key, "mcp_servers", out var name, out var leaf))
            {
                continue;
            }

            var builder = GetOrCreateBuilder(configs, name);
            switch (leaf)
            {
                case "enabled":
                    if (TryReadBoolean(rawValue, out var enabled))
                    {
                        builder.Enabled = enabled;
                    }
                    break;
                case "required":
                    if (TryReadBoolean(rawValue, out var required))
                    {
                        builder.Required = required;
                    }
                    break;
                case "command":
                    builder.Command = NormalizeRawConfigValue(rawValue);
                    break;
                case "url":
                    builder.Url = NormalizeRawConfigValue(rawValue);
                    break;
                case "cwd":
                    builder.Cwd = NormalizePath(NormalizeRawConfigValue(rawValue));
                    break;
                case "args":
                    builder.Args = ReadRawStringArray(rawValue);
                    break;
                case "env":
                    builder.Env = ReadRawStringMap(rawValue);
                    break;
                case "env_vars":
                    builder.EnvVars = ReadRawStringArray(rawValue);
                    break;
                case "bearer_token_env_var":
                    builder.BearerTokenEnvVar = NormalizeRawConfigValue(rawValue);
                    break;
                case "http_headers":
                    builder.HttpHeaders = ReadRawStringMap(rawValue);
                    break;
                case "env_http_headers":
                    builder.EnvHttpHeaders = ReadRawStringMap(rawValue);
                    break;
                case "startup_timeout_sec":
                    if (TryReadDouble(rawValue, out var startupTimeoutSec))
                    {
                        builder.StartupTimeout = TimeSpan.FromSeconds(startupTimeoutSec);
                    }
                    break;
                case "startup_timeout_ms":
                    if (TryReadDouble(rawValue, out var startupTimeoutMs))
                    {
                        builder.StartupTimeout = TimeSpan.FromMilliseconds(startupTimeoutMs);
                    }
                    break;
                case "tool_timeout_sec":
                    if (TryReadDouble(rawValue, out var timeoutSec))
                    {
                        builder.ToolTimeout = TimeSpan.FromSeconds(timeoutSec);
                    }
                    break;
                case "tool_timeout_ms":
                    if (TryReadDouble(rawValue, out var timeoutMs))
                    {
                        builder.ToolTimeout = TimeSpan.FromMilliseconds(timeoutMs);
                    }
                    break;
                case "enabled_tools":
                    builder.EnabledTools = ReadRawStringArray(rawValue);
                    break;
                case "disabled_tools":
                    builder.DisabledTools = ReadRawStringArray(rawValue);
                    break;
            }
        }
    }

    private static void ApplyPluginDefinitions(
        Dictionary<string, KernelMcpServerConfigBuilder> configs,
        IReadOnlyDictionary<string, KernelPluginMcpServerDefinition> pluginServers)
    {
        foreach (var (name, plugin) in pluginServers)
        {
            configs[name] = new KernelMcpServerConfigBuilder(name)
            {
                Enabled = plugin.Enabled,
                Command = Normalize(plugin.Command),
                Args = plugin.Args.ToList(),
                Env = new Dictionary<string, string>(plugin.Env, StringComparer.OrdinalIgnoreCase),
                EnvVars = plugin.EnvVars.ToList(),
                Cwd = NormalizePath(plugin.Cwd),
                Url = Normalize(plugin.Url),
                BearerTokenEnvVar = Normalize(plugin.BearerTokenEnvVar),
                HttpHeaders = new Dictionary<string, string>(plugin.HttpHeaders, StringComparer.OrdinalIgnoreCase),
                EnvHttpHeaders = new Dictionary<string, string>(plugin.EnvHttpHeaders, StringComparer.OrdinalIgnoreCase),
                StartupTimeout = plugin.StartupTimeout,
                ToolTimeout = plugin.ToolTimeout,
                EnabledTools = plugin.EnabledTools.ToList(),
                DisabledTools = plugin.DisabledTools.ToList(),
            };
        }
    }

    private static void ApplyPackageDefinitions(
        Dictionary<string, KernelMcpServerConfigBuilder> configs,
        IReadOnlyDictionary<string, KernelMcpServerPackageDefinition> packageServers)
    {
        foreach (var (name, packageServer) in packageServers)
        {
            configs[name] = new KernelMcpServerConfigBuilder(name)
            {
                Enabled = packageServer.Enabled,
                Required = packageServer.Required,
                Command = Normalize(packageServer.Command),
                Args = packageServer.Args.ToList(),
                Env = new Dictionary<string, string>(packageServer.Env, StringComparer.OrdinalIgnoreCase),
                EnvVars = packageServer.EnvVars.ToList(),
                Cwd = NormalizePath(packageServer.Cwd),
                Url = Normalize(packageServer.Url),
                BearerTokenEnvVar = Normalize(packageServer.BearerTokenEnvVar),
                HttpHeaders = new Dictionary<string, string>(packageServer.HttpHeaders, StringComparer.OrdinalIgnoreCase),
                EnvHttpHeaders = new Dictionary<string, string>(packageServer.EnvHttpHeaders, StringComparer.OrdinalIgnoreCase),
                StartupTimeout = packageServer.StartupTimeout,
                ToolTimeout = packageServer.ToolTimeout,
                EnabledTools = packageServer.EnabledTools.ToList(),
                DisabledTools = packageServer.DisabledTools.ToList(),
            };
        }
    }

    private static KernelMcpServerConfigBuilder GetOrCreateBuilder(Dictionary<string, KernelMcpServerConfigBuilder> configs, string name)
    {
        if (!configs.TryGetValue(name, out var builder))
        {
            builder = new KernelMcpServerConfigBuilder(name);
            configs[name] = builder;
        }

        return builder;
    }

    private static bool TryReadBoolean(string rawValue, out bool value)
    {
        value = false;
        try
        {
            using var json = JsonDocument.Parse(rawValue);
            var root = json.RootElement;
            if (root.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = root.GetBoolean();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryReadDouble(string rawValue, out double value)
    {
        value = 0;
        try
        {
            using var json = JsonDocument.Parse(rawValue);
            return json.RootElement.TryGetDouble(out value);
        }
        catch
        {
            return false;
        }
    }

    private static List<string> ReadRawStringArray(string rawValue)
    {
        try
        {
            using var json = JsonDocument.Parse(rawValue);
            return json.RootElement.EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? Normalize(item.GetString()) : null)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item!)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static Dictionary<string, string> ReadRawStringMap(string rawValue)
    {
        try
        {
            using var json = JsonDocument.Parse(rawValue);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in json.RootElement.EnumerateObject())
            {
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? Normalize(property.Value.GetString())
                    : Normalize(property.Value.GetRawText());
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result[property.Name] = value!;
                }
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ReadTomlString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is string text ? Normalize(text) : null;

    private static bool? ReadTomlBool(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is bool enabled ? enabled : null;

    private static List<string> ReadTomlStringArray(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is IEnumerable<object> items
            ? items.Select(static item => item as string).Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item!).ToList()
            : new List<string>();

    private static Dictionary<string, string> ReadTomlStringMap(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlTable map)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return map.Where(static pair => pair.Value is string)
            .ToDictionary(static pair => pair.Key, static pair => (string)pair.Value!, StringComparer.OrdinalIgnoreCase);
    }

    private static TimeSpan? ReadTomlTimeSpan(TomlTable table, string secondsKey, string millisecondsKey)
    {
        if (table.TryGetValue(secondsKey, out var seconds) && TryConvertToDouble(seconds, out var secValue))
        {
            return TimeSpan.FromSeconds(secValue);
        }

        if (table.TryGetValue(millisecondsKey, out var milliseconds) && TryConvertToDouble(milliseconds, out var msValue))
        {
            return TimeSpan.FromMilliseconds(msValue);
        }

        return null;
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        result = 0;
        return value switch
        {
            long number => (result = number) >= 0,
            int number => (result = number) >= 0,
            double number => (result = number) >= 0,
            _ => false,
        };
    }

    private static IEnumerable<string> ReadTomlSectionNames(string configTomlPath, string sectionPrefix)
    {
        if (!File.Exists(configTomlPath))
        {
            yield break;
        }

        foreach (var line in File.ReadLines(configTomlPath))
        {
            var text = line.Trim();
            if (!TryExtractTomlSectionName(text, sectionPrefix, out var name))
            {
                continue;
            }

            yield return name;
        }
    }

    private static bool TryExtractTomlSectionName(string text, string sectionPrefix, out string name)
    {
        name = string.Empty;
        if (!text.StartsWith('[') || !text.EndsWith(']'))
        {
            return false;
        }

        var section = text[1..^1].Trim();
        var prefix = $"{sectionPrefix}.";
        if (!section.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = section[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        if (remainder.StartsWith('"'))
        {
            var closing = remainder.IndexOf('"', 1);
            if (closing <= 1)
            {
                return false;
            }

            name = remainder[1..closing];
            return !string.IsNullOrWhiteSpace(name);
        }

        var dotIndex = remainder.IndexOf('.');
        var candidate = dotIndex >= 0 ? remainder[..dotIndex] : remainder;
        name = candidate.Trim().Trim('"', '\'');
        return !string.IsNullOrWhiteSpace(name);
    }

    private static bool TryParseScopedConfigKey(string key, string sectionPrefix, out string name, out string leaf)
    {
        name = string.Empty;
        leaf = string.Empty;
        var prefix = $"{sectionPrefix}.";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = key[prefix.Length..];
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        if (remainder.StartsWith('"'))
        {
            var closing = remainder.IndexOf('"', 1);
            if (closing <= 1 || closing + 2 >= remainder.Length || remainder[closing + 1] != '.')
            {
                return false;
            }

            name = remainder[1..closing];
            leaf = remainder[(closing + 2)..].Trim();
            return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(leaf);
        }

        var dotIndex = remainder.IndexOf('.');
        if (dotIndex <= 0 || dotIndex + 1 >= remainder.Length)
        {
            return false;
        }

        name = remainder[..dotIndex].Trim().Trim('"', '\'');
        leaf = remainder[(dotIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(leaf);
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

    private static string? NormalizePath(string? value)
    {
        var normalized = Normalize(value);
        return normalized is null ? null : Path.GetFullPath(normalized);
    }

    private sealed class KernelMcpServerConfigBuilder
    {
        public KernelMcpServerConfigBuilder(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public bool Enabled { get; set; } = true;
        public bool Required { get; set; }
        public string? Command { get; set; }
        public List<string> Args { get; set; } = new();
        public Dictionary<string, string> Env { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> EnvVars { get; set; } = new();
        public string? Cwd { get; set; }
        public string? Url { get; set; }
        public string? BearerTokenEnvVar { get; set; }
        public Dictionary<string, string> HttpHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> EnvHttpHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public TimeSpan? StartupTimeout { get; set; }
        public TimeSpan? ToolTimeout { get; set; }
        public List<string> EnabledTools { get; set; } = new();
        public List<string> DisabledTools { get; set; } = new();

        public KernelMcpResolvedServerConfig? Build()
        {
            KernelMcpTransportConfig? transport = null;
            if (!string.IsNullOrWhiteSpace(Command))
            {
                transport = new KernelMcpStdioTransportConfig(Command!, Args.ToArray(), Env, EnvVars.ToArray(), Cwd);
            }
            else if (!string.IsNullOrWhiteSpace(Url))
            {
                transport = new KernelMcpStreamableHttpTransportConfig(Url!, BearerTokenEnvVar, HttpHeaders, EnvHttpHeaders);
            }

            return transport is null
                ? null
                : new KernelMcpResolvedServerConfig(
                    Name,
                    transport,
                    Enabled,
                    Required,
                    StartupTimeout ?? DefaultStartupTimeout,
                    ToolTimeout ?? DefaultToolTimeout,
                    EnabledTools.ToArray(),
                    DisabledTools.ToArray());
        }
    }

    private static IReadOnlyDictionary<string, object?> ApplyToolFilters(
        IReadOnlyDictionary<string, object?> tools,
        KernelMcpResolvedServerConfig resolved)
    {
        if (tools.Count == 0)
        {
            return tools;
        }

        HashSet<string>? enabled = null;
        if (resolved.EnabledTools.Count > 0)
        {
            enabled = new HashSet<string>(
                resolved.EnabledTools.Where(static name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);
        }

        HashSet<string>? disabled = null;
        if (resolved.DisabledTools.Count > 0)
        {
            disabled = new HashSet<string>(
                resolved.DisabledTools.Where(static name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);
        }

        if (enabled is null && disabled is null)
        {
            return tools;
        }

        var filtered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, tool) in tools)
        {
            if (enabled is not null && !enabled.Contains(name))
            {
                continue;
            }

            if (disabled is not null && disabled.Contains(name))
            {
                continue;
            }

            filtered[name] = tool;
        }

        return filtered;
    }

    private sealed record KernelMcpResolvedServerConfig(
        string Name,
        KernelMcpTransportConfig Transport,
        bool Enabled,
        bool Required,
        TimeSpan StartupTimeout,
        TimeSpan ToolTimeout,
        IReadOnlyList<string> EnabledTools,
        IReadOnlyList<string> DisabledTools);
    private abstract record KernelMcpTransportConfig;
    private sealed record KernelMcpStdioTransportConfig(string Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env, IReadOnlyList<string> EnvVars, string? Cwd) : KernelMcpTransportConfig;
    private sealed record KernelMcpStreamableHttpTransportConfig(string Url, string? BearerTokenEnvVar, IReadOnlyDictionary<string, string> HttpHeaders, IReadOnlyDictionary<string, string> EnvHttpHeaders) : KernelMcpTransportConfig;
}
