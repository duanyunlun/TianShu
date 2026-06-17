using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Provider.Abstractions;
using TianShu.Contracts.Sessions;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;
using ConfigTomlPathResolver = TianShu.Configuration.TianShuConfigTomlPathResolver;

namespace TianShu.RuntimeComposition;

/// <summary>
/// TianShu TOML 配置加载器。
/// Loads TianShu TOML configuration layers.
/// </summary>
public sealed class TianShuTomlConfigurationLoader
{
    private const string SystemRootOverrideEnvironmentVariable = "TIANSHU_SYSTEM_CONFIG_ROOT";
    private const string LegacySystemRootOverrideEnvironmentVariable = "TIANSHU_SYSTEM_CONFIG_ROOT";
    private const string LegacyManagedConfigMigrationOnlyDisabledReason = "legacy managed_config.toml 只作为迁移诊断层展示，不再参与正式配置合并。";
    private static readonly string[] DefaultProjectRootMarkers = [".git", ".tianshu"];

    public ResolvedTianShuConfig Load(string? configFilePath, string? profileOverride, string? workingDirectory = null)
        => Load(configFilePath, profileOverride, configOverrides: null, workingDirectory);

    public ResolvedTianShuConfig Load(
        string? configFilePath,
        string? profileOverride,
        IReadOnlyDictionary<string, string>? configOverrides,
        string? workingDirectory = null,
        string? programDirectory = null)
    {
        var explicitPath = Normalize(configFilePath);
        var userPath = NormalizeFilePath(string.IsNullOrWhiteSpace(explicitPath) ? ResolveDefaultPath(programDirectory) : explicitPath!);
        var sessionFlagsLayer = CreateSessionFlagsLayer(
            profileOverride,
            configOverrides,
            ResolveSessionFlagsBaseDirectory(workingDirectory));
        var loadPlan = ResolveLoadLayers(userPath, sessionFlagsLayer, workingDirectory, programDirectory);
        var layers = loadPlan.Layers;
        var root = LoadMergedRoot(layers, includeDisabled: false);
        var normalizedWorkingDirectory = loadPlan.NormalizedWorkingDirectory;
        var trustContext = loadPlan.TrustContext;
        var workspaceResolverPolicy = loadPlan.WorkspaceResolverPolicy;

        var activeProfile = ReadString(root, "profile");
        var profile = ReadProfile(root, activeProfile);

        var nativeProfile = ResolveNativeProfile(root, profile);
        var model = ReadMergedString(root, profile, "model") ?? ReadString(nativeProfile.Agent, "model");
        var modelProvider = ReadMergedString(root, profile, "provider")
                            ?? ReadString(nativeProfile.Execution, "provider")
                            ?? ReadString(nativeProfile.Agent, "provider");
        var policyStrategyDefaults = PolicyStrategyRuntimeComposition.ResolveEffectiveDefaultsFromConfigPath(userPath);
        var approvalPolicy = ReadMergedString(root, profile, "approval_policy")
            ?? ResolveImplicitApprovalPolicy(trustContext, normalizedWorkingDirectory)
            ?? policyStrategyDefaults.ApprovalPolicy;
        var sandboxMode = ReadMergedString(root, profile, "sandbox_mode")
            ?? NormalizePolicyStrategySandboxMode(policyStrategyDefaults.SandboxMode);
        var webSearchMode = ReadMergedString(root, profile, "web_search");
        var serviceTier = ReadMergedString(root, profile, "service_tier");
        var rawConfig = TianShuConfigObjectUtilities.ConvertTomlTableToDictionary(root);
        TianShuPromptConfigUtilities.ApplyPromptConfigLayer(
            rawConfig,
            layers
                .Select(static layer => new TianShuPromptConfigLayer(
                    layer.Path,
                    layer.DirectoryPath,
                    TianShuConfigObjectUtilities.ConvertTomlTableToDictionary(layer.Root),
                    layer.IsDisabled,
                    layer.FileExists))
                .ToArray(),
            workingDirectory);
        var routeCandidate = ResolveActiveRouteCandidate(rawConfig);
        if (routeCandidate is not null && HasActiveRouteSetReference(rawConfig))
        {
            model = routeCandidate.Model;
            modelProvider = routeCandidate.Provider;
        }
        else
        {
            model ??= routeCandidate?.Model;
            modelProvider ??= routeCandidate?.Provider;
        }
        var provider = ReadProvider(root, modelProvider);
        var agentReasoning = ReadChildTable(nativeProfile.Agent, "reasoning");
        var providerReasoning = ReadChildTable(provider, "reasoning");
        var modelReasoningEnabled = ReadMergedBool(root, profile, "model_reasoning_enabled")
            ?? ReadBool(agentReasoning, "enabled")
            ?? ReadBool(providerReasoning, "enabled");
        var modelReasoningEffort = ReadMergedString(root, profile, "model_reasoning_effort")
            ?? ReadString(agentReasoning, "effort")
            ?? ReadString(providerReasoning, "effort");
        var modelReasoningSummary = ReadMergedString(root, profile, "model_reasoning_summary")
            ?? ReadString(agentReasoning, "summary")
            ?? ReadString(providerReasoning, "summary");
        var modelVerbosity = ReadMergedString(root, profile, "model_verbosity")
            ?? ReadString(agentReasoning, "verbosity")
            ?? ReadString(providerReasoning, "verbosity");
        var modelReasoningBudgetTokens = ReadMergedLong(root, profile, "model_reasoning_budget_tokens")
            ?? ReadLong(agentReasoning, "budget_tokens")
            ?? ReadLong(providerReasoning, "budget_tokens");

        var baseUrl = ReadString(provider, "base_url");
        var envKey = ReadString(provider, "api_key_env");
        var wireApi = KernelModelProtocolResolver.ResolveModelProtocol(rawConfig, modelProvider, model, routeCandidate?.Protocol);
        var requestMaxRetries = ReadLong(provider, "request_max_retries");
        var streamMaxRetries = ReadLong(provider, "stream_max_retries");
        var streamIdleTimeoutMs = ReadLong(provider, "stream_idle_timeout_ms");
        var websocketConnectTimeoutMs = ReadLong(provider, "websocket_connect_timeout_ms");
        var supportsWebsockets = ReadBool(provider, "supports_websockets");
        var adapter = ResolveAdapter();
        var effectiveConfigPath = layers
            .Where(static layer => !layer.IsDisabled && layer.FileExists)
            .Select(static layer => layer.Path)
            .LastOrDefault(static path => !string.IsNullOrWhiteSpace(path))
            ?? userPath;

        return new ResolvedTianShuConfig
        {
            ConfigFilePath = effectiveConfigPath,
            UserConfigPath = userPath,
            ActiveProfile = activeProfile,
            Model = model,
            ModelProvider = modelProvider,
            ApprovalPolicy = approvalPolicy,
            SandboxMode = sandboxMode,
            WebSearchMode = webSearchMode,
            ServiceTier = serviceTier,
            ModelReasoningEnabled = modelReasoningEnabled,
            ModelReasoningEffort = modelReasoningEffort,
            ModelReasoningSummary = modelReasoningSummary,
            ModelVerbosity = modelVerbosity,
            ModelReasoningBudgetTokens = modelReasoningBudgetTokens,
            ProviderBaseUrl = baseUrl,
            ProviderEnvKey = envKey,
            ProviderWireApi = wireApi,
            ProviderRequestMaxRetries = requestMaxRetries,
            ProviderStreamMaxRetries = streamMaxRetries,
            ProviderStreamIdleTimeoutMs = streamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs = websocketConnectTimeoutMs,
            ProviderSupportsWebsockets = supportsWebsockets,
            ProtocolAdapter = adapter,
            WorkspaceResolverPolicy = workspaceResolverPolicy,
            RawConfig = rawConfig,
            Layers = layers
                .Select(static layer => new ResolvedTianShuConfigLayer
                {
                    SourceKind = layer.SourceKind,
                    Path = layer.Path,
                    DirectoryPath = layer.DirectoryPath,
                    FileExists = layer.FileExists,
                    IsEmpty = layer.Root.Count == 0,
                    DisabledReason = layer.DisabledReason,
                })
                .ToArray(),
        };
    }

    public static void ApplyToOptions(ControlPlaneInitializeRuntimeCommand options, ResolvedTianShuConfig config)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);

        options.ConfigFilePath = string.IsNullOrWhiteSpace(options.ConfigFilePath) ? config.UserConfigPath : options.ConfigFilePath;
        if (string.IsNullOrWhiteSpace(options.ProfileName))
        {
            options.ProfileName = config.ActiveProfile;
            options.ProfileNameResolvedFromConfig = !string.IsNullOrWhiteSpace(config.ActiveProfile);
        }

        options.Model = Pick(options.Model, config.Model);
        options.ModelProvider = Pick(options.ModelProvider, config.ModelProvider);
        options.ApprovalPolicy = Pick(options.ApprovalPolicy, config.ApprovalPolicy);
        options.SandboxMode = Pick(options.SandboxMode, config.SandboxMode);
        options.WebSearchMode = Pick(options.WebSearchMode, config.WebSearchMode);
        options.ServiceTier = Pick(options.ServiceTier, config.ServiceTier);
        options.ModelReasoningSummary = Pick(options.ModelReasoningSummary, config.ModelReasoningSummary);
        options.ModelVerbosity = Pick(options.ModelVerbosity, config.ModelVerbosity);
        options.ProviderBaseUrl = Pick(options.ProviderBaseUrl, config.ProviderBaseUrl);
        options.ProviderApiKeyEnvironmentVariable = Pick(options.ProviderApiKeyEnvironmentVariable, config.ProviderEnvKey);
        options.ProviderWireApi = Pick(options.ProviderWireApi, config.ProviderWireApi);
        options.ProviderRequestMaxRetries ??= config.ProviderRequestMaxRetries;
        options.ProviderStreamMaxRetries ??= config.ProviderStreamMaxRetries;
        options.ProviderStreamIdleTimeoutMs ??= config.ProviderStreamIdleTimeoutMs;
        options.ProviderWebsocketConnectTimeoutMs ??= config.ProviderWebsocketConnectTimeoutMs;
        options.ProviderSupportsWebsockets ??= config.ProviderSupportsWebsockets;

        var adapter = NormalizeProtocolAdapter(options.ProtocolAdapter, nameof(ControlPlaneInitializeRuntimeCommand.ProtocolAdapter));
        options.ProtocolAdapter = adapter ?? config.ProtocolAdapter;
    }

    public static string ResolveDefaultPath()
        => ConfigTomlPathResolver.ResolveUserConfigTomlPath();

    internal static string ResolveDefaultPath(string? programDirectory)
    {
        if (string.IsNullOrWhiteSpace(programDirectory))
        {
            return ResolveDefaultPath();
        }

        var homePath = TianShuRuntimeLayoutPaths.ResolveTianShuHomePathFrom(
            programDirectory!,
            Environment.GetEnvironmentVariable("TIANSHU_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return TianShuRuntimeLayoutPaths.ResolveTianShuConfigFilePathFromHome(homePath);
    }

    private static string ResolveSystemConfigPath()
        => ConfigTomlPathResolver.ResolveSystemConfigTomlPath();

    private static string ResolveManagedConfigPath(string userPath)
    {
        var systemRootOverride = ResolveSystemRootOverride();
        if (!string.IsNullOrWhiteSpace(systemRootOverride))
        {
            return Path.Combine(systemRootOverride!, "managed_config.toml");
        }

        var userDirectory = Path.GetDirectoryName(userPath);
        if (string.IsNullOrWhiteSpace(userDirectory))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tianshu", "managed_config.toml");
        }

        return Path.Combine(userDirectory, "managed_config.toml");
    }

    private static string? ResolveSystemRootOverride()
        => Normalize(Environment.GetEnvironmentVariable(SystemRootOverrideEnvironmentVariable))
           ?? Normalize(Environment.GetEnvironmentVariable(LegacySystemRootOverrideEnvironmentVariable));

    private static ResolvedLoadLayers ResolveLoadLayers(
        string userPath,
        ConfigLayer? sessionFlagsLayer,
        string? workingDirectory,
        string? programDirectory)
    {
        var layers = new List<ConfigLayer>();
        var seenPaths = new HashSet<string>(PathComparer);
        var isPortableMode = IsPortableModeForConfigPath(userPath, programDirectory);

        if (!isPortableMode)
        {
            AddRequiredLayer(layers, seenPaths, ResolvedTianShuConfigLayerSourceKind.System, ResolveSystemConfigPath());
        }

        AddUserModuleDefaultLayers(layers, seenPaths, userPath);
        AddRequiredLayer(layers, seenPaths, ResolvedTianShuConfigLayerSourceKind.User, userPath);
        AddUserProviderInstanceLayers(layers, seenPaths, userPath);

        List<ConfigLayer> mergedLowerPriorityLayers;
        if (sessionFlagsLayer is null)
        {
            mergedLowerPriorityLayers = layers;
        }
        else
        {
            var mergedSessionFlagsLayer = sessionFlagsLayer.Value;
            mergedLowerPriorityLayers = new List<ConfigLayer>(layers);
            mergedLowerPriorityLayers.Add(mergedSessionFlagsLayer);
        }
        var mergedLowerPriorityConfig = LoadMergedRoot(mergedLowerPriorityLayers, includeDisabled: false);
        var workspaceResolverPolicy = WorkspaceResolverRuntimeComposition.ResolveEffectivePolicy(userPath, ResolveProjectRootMarkers(mergedLowerPriorityConfig));
        var projectRootMarkers = workspaceResolverPolicy.RootMarkers;
        var normalizedWorkingDirectory = NormalizeDirectory(workingDirectory);
        var trustContext = BuildProjectTrustContext(mergedLowerPriorityConfig, normalizedWorkingDirectory, projectRootMarkers, userPath);

        if (!string.IsNullOrWhiteSpace(normalizedWorkingDirectory))
        {
            foreach (var dotTianShuDirectory in EnumerateProjectLayerDirectories(normalizedWorkingDirectory!, projectRootMarkers))
            {
                var layerDirectory = ResolveProjectLayerDirectory(dotTianShuDirectory);
                var configPath = Path.Combine(dotTianShuDirectory, "tianshu.toml");
                var disabledReason = File.Exists(configPath) && trustContext is not null
                    ? ResolveDisabledReason(trustContext, layerDirectory)
                    : null;
                if (File.Exists(configPath))
                {
                    AddOptionalLayerIfExists(
                        layers,
                        seenPaths,
                        ResolvedTianShuConfigLayerSourceKind.Project,
                        configPath,
                        layerDirectory,
                        disabledReason,
                        suppressErrorsWhenDisabled: true);
                    continue;
                }

                AddVirtualLayer(
                    layers,
                    ResolvedTianShuConfigLayerSourceKind.Project,
                    configPath,
                    layerDirectory,
                    disabledReason: null,
                    fileExists: false);
            }
        }

        if (sessionFlagsLayer is { } sessionFlags)
        {
            layers.Add(sessionFlags);
        }

        AddOptionalLayerIfExists(
            layers,
            seenPaths,
            ResolvedTianShuConfigLayerSourceKind.LegacyManagedConfig,
            ResolveManagedConfigPath(userPath),
            directoryPath: null,
            LegacyManagedConfigMigrationOnlyDisabledReason);

        return new ResolvedLoadLayers(layers, normalizedWorkingDirectory, trustContext, workspaceResolverPolicy);
    }

    internal static bool IsPortableModeForConfigPath(string userPath, string? programDirectory = null)
    {
        var layout = string.IsNullOrWhiteSpace(programDirectory)
            ? TianShuRuntimeLayoutPaths.TryResolvePortableTianShuHomeLayout()
            : TianShuRuntimeLayoutPaths.TryResolvePortableTianShuHomeLayoutFrom(programDirectory!);
        if (layout is null)
        {
            return false;
        }

        var normalizedUserPath = NormalizeFilePath(userPath);
        return string.Equals(normalizedUserPath, Path.GetFullPath(layout.ConfigFilePath), PathComparison);
    }

    private static ConfigLayer? CreateSessionFlagsLayer(
        string? profileOverride,
        IReadOnlyDictionary<string, string>? configOverrides,
        string? baseDirectory)
    {
        TomlTable? root = null;
        var normalizedProfile = Normalize(profileOverride);

        if (configOverrides is not null)
        {
            foreach (var pair in configOverrides)
            {
                var canonicalKey = CanonicalizeConfigKeyPath(pair.Key);
                if (string.IsNullOrWhiteSpace(canonicalKey))
                {
                    continue;
                }

                root ??= new TomlTable();
                ApplyConfigOverride(
                    root,
                    canonicalKey,
                    RebaseConfigOverrideRawValue(canonicalKey, pair.Value, baseDirectory));
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedProfile))
        {
            root ??= new TomlTable();
            root["profile"] = normalizedProfile!;
        }

        if (root is null)
        {
            return null;
        }

        return new ConfigLayer(
            ResolvedTianShuConfigLayerSourceKind.SessionFlags,
            Path: null,
            DirectoryPath: null,
            root,
            DisabledReason: null,
            FileExists: false);
    }

    private static void ApplyConfigOverride(TomlTable root, string key, string rawValue)
    {
        var segments = SplitConfigKeyPath(CanonicalizeConfigKeyPath(key));
        if (segments.Count == 0)
        {
            return;
        }

        SetTomlPathValue(root, segments, ConvertRawOverrideToTomlValue(rawValue));
    }

    private static void SetTomlPathValue(TomlTable root, IReadOnlyList<string> segments, object value)
    {
        TomlTable current = root;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (current.TryGetValue(segments[index], out var existing) && existing is TomlTable table)
            {
                current = table;
                continue;
            }

            var created = new TomlTable();
            current[segments[index]] = created;
            current = created;
        }

        current[segments[^1]] = value;
    }

    private static object ConvertRawOverrideToTomlValue(string rawValue)
    {
        var normalized = Normalize(rawValue) ?? string.Empty;
        if (normalized.StartsWith("json:", StringComparison.OrdinalIgnoreCase))
        {
            var payload = normalized["json:".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(payload))
            {
                try
                {
                    using var document = JsonDocument.Parse(payload);
                    return ConvertJsonElementToTomlValue(document.RootElement);
                }
                catch
                {
                }
            }
        }

        if (bool.TryParse(normalized, out var boolValue))
        {
            return boolValue;
        }

        if (long.TryParse(normalized, out var longValue))
        {
            return longValue;
        }

        if (double.TryParse(normalized, out var doubleValue))
        {
            return doubleValue;
        }

        return rawValue;
    }

    private static string? ResolveSessionFlagsBaseDirectory(string? workingDirectory)
    {
        var normalizedWorkingDirectory = NormalizeDirectory(workingDirectory);
        if (!string.IsNullOrWhiteSpace(normalizedWorkingDirectory))
        {
            return normalizedWorkingDirectory;
        }

        var tianShuHome = Path.GetDirectoryName(ResolveDefaultPath());
        return NormalizeDirectory(tianShuHome);
    }

    private static string RebaseConfigOverrideRawValue(string canonicalKey, string rawValue, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return rawValue;
        }

        var normalized = Normalize(rawValue) ?? string.Empty;
        if (normalized.StartsWith("json:", StringComparison.OrdinalIgnoreCase))
        {
            var payload = normalized["json:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(payload))
            {
                return rawValue;
            }

            try
            {
                using var document = JsonDocument.Parse(payload);
                var rebased = RebaseConfigOverrideJsonElement(
                    document.RootElement,
                    SplitConfigKeyPath(canonicalKey),
                    baseDirectory!);
                return $"json:{JsonSerializer.Serialize(rebased)}";
            }
            catch
            {
                return rawValue;
            }
        }

        return ShouldRebaseConfigOverridePath(SplitConfigKeyPath(canonicalKey))
            ? RebaseRelativePath(rawValue, baseDirectory!)
            : rawValue;
    }

    private static object? RebaseConfigOverrideJsonElement(
        JsonElement element,
        IReadOnlyList<string> segments,
        string baseDirectory)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    static property => property.Name,
                    property => RebaseConfigOverrideJsonElement(
                        property.Value,
                        [.. segments, property.Name],
                        baseDirectory),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => RebaseConfigOverrideJsonElement(item, segments, baseDirectory))
                .ToList(),
            JsonValueKind.String when ShouldRebaseConfigOverridePath(segments) => RebaseRelativePath(element.GetString() ?? string.Empty, baseDirectory),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Null => null,
            _ => element.ToString(),
        };
    }

    private static bool ShouldRebaseConfigOverridePath(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            return false;
        }

        return segments switch
        {
            ["log_dir"] => true,
            ["model_instructions_file"] => true,
            ["experimental_instructions_file"] => true,
            ["experimental_compact_prompt_file"] => true,
            ["js_repl_node_path"] => true,
            ["js_repl_node_module_dirs"] => true,
            ["sqlite_home"] => true,
            ["zsh_path"] => true,
            ["model_route_set_json"] => true,
            ["sandbox_workspace_write", "writable_roots"] => true,
            ["skills", "config", "path"] => true,
            ["agents", "roles", _, "config_file"] => true,
            ["mcp_servers", _, "cwd"] => true,
            ["profiles", _, "model_instructions_file"] => true,
            ["profiles", _, "experimental_instructions_file"] => true,
            ["profiles", _, "experimental_compact_prompt_file"] => true,
            ["profiles", _, "js_repl_node_path"] => true,
            ["profiles", _, "js_repl_node_module_dirs"] => true,
            ["profiles", _, "zsh_path"] => true,
            ["profiles", _, "model_route_set_json"] => true,
            _ => false,
        };
    }

    private static string RebaseRelativePath(string rawValue, string baseDirectory)
    {
        var normalized = Normalize(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return rawValue;
        }

        if (Path.IsPathRooted(normalized))
        {
            return Path.GetFullPath(normalized);
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, normalized));
    }

    private static object ConvertJsonElementToTomlValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObjectToTomlTable(value),
            JsonValueKind.Array => ConvertJsonArrayToTomlArray(value),
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.Null => string.Empty,
            _ => value.ToString(),
        };
    }

    private static TomlTable ConvertJsonObjectToTomlTable(JsonElement value)
    {
        var table = new TomlTable();
        foreach (var property in value.EnumerateObject())
        {
            table[property.Name] = ConvertJsonElementToTomlValue(property.Value);
        }

        return table;
    }

    private static TomlArray ConvertJsonArrayToTomlArray(JsonElement value)
    {
        var array = new TomlArray();
        foreach (var item in value.EnumerateArray())
        {
            array.Add(ConvertJsonElementToTomlValue(item));
        }

        return array;
    }

    private static List<string> SplitConfigKeyPath(string keyPath)
    {
        return keyPath
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment.Trim().Trim('"', '\''))
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .ToList();
    }

    private static string CanonicalizeConfigKeyPath(string key)
    {
        var segments = SplitConfigKeyPath(key);
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(".", segments);
    }

    private static void AddRequiredLayer(
        List<ConfigLayer> layers,
        HashSet<string> seenPaths,
        ResolvedTianShuConfigLayerSourceKind sourceKind,
        string path)
    {
        var normalizedPath = NormalizeFilePath(path);
        if (!seenPaths.Add(normalizedPath))
        {
            return;
        }

        if (!File.Exists(normalizedPath))
        {
            layers.Add(new ConfigLayer(sourceKind, normalizedPath, null, new TomlTable(), null, false));
            return;
        }

        layers.Add(new ConfigLayer(sourceKind, normalizedPath, null, ReadTomlRoot(normalizedPath), null, true));
    }

    private static void AddUserModuleDefaultLayers(
        List<ConfigLayer> layers,
        HashSet<string> seenPaths,
        string userPath)
    {
        foreach (var modulePath in TianShuKnownModuleConfigurationPaths.EnumerateDefaultLayerModuleFiles(userPath))
        {
            var directory = Path.GetDirectoryName(modulePath);
            AddOptionalLayerIfExists(
                layers,
                seenPaths,
                ResolvedTianShuConfigLayerSourceKind.UserModule,
                modulePath,
                directory);
        }
    }

    private static void AddUserProviderInstanceLayers(
        List<ConfigLayer> layers,
        HashSet<string> seenPaths,
        string userPath)
    {
        var normalizedUserPath = NormalizeFilePath(userPath);
        var userDirectory = Path.GetDirectoryName(normalizedUserPath);
        if (string.IsNullOrWhiteSpace(userDirectory))
        {
            return;
        }

        var providerInstanceDirectory = TianShuHomePathUtilities.ResolveModulePathFromConfig(userPath, "model", "provider-instances");
        foreach (var providerInstancePath in TianShuKnownModuleConfigurationPaths.ResolveProviderInstanceModulePaths(normalizedUserPath))
        {
            AddOptionalLayerIfExists(
                layers,
                seenPaths,
                ResolvedTianShuConfigLayerSourceKind.UserProviderInstance,
                providerInstancePath,
                providerInstanceDirectory);
        }
    }

    private static void AddOptionalLayerIfExists(
        List<ConfigLayer> layers,
        HashSet<string> seenPaths,
        ResolvedTianShuConfigLayerSourceKind sourceKind,
        string path,
        string? directoryPath,
        string? disabledReason = null,
        bool suppressErrorsWhenDisabled = false)
    {
        var normalizedPath = NormalizeFilePath(path);
        if (!File.Exists(normalizedPath) || !seenPaths.Add(normalizedPath))
        {
            return;
        }

        TomlTable root;
        try
        {
            root = ReadTomlRoot(normalizedPath);
        }
        catch when (suppressErrorsWhenDisabled && !string.IsNullOrWhiteSpace(disabledReason))
        {
            root = new TomlTable();
        }

        layers.Add(new ConfigLayer(sourceKind, normalizedPath, directoryPath, root, disabledReason, true));
    }

    private static void AddVirtualLayer(
        List<ConfigLayer> layers,
        ResolvedTianShuConfigLayerSourceKind sourceKind,
        string path,
        string? directoryPath,
        string? disabledReason,
        bool fileExists)
    {
        layers.Add(new ConfigLayer(
            sourceKind,
            NormalizeFilePath(path),
            directoryPath,
            new TomlTable(),
            disabledReason,
            fileExists));
    }

    private static ProjectTrustContext? BuildProjectTrustContext(
        TomlTable mergedConfig,
        string? normalizedWorkingDirectory,
        IReadOnlyList<string> projectRootMarkers,
        string userConfigPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedWorkingDirectory))
        {
            return null;
        }

        var projectRoot = FindProjectRoot(normalizedWorkingDirectory!, projectRootMarkers);
        var gitRoot = FindGitRoot(normalizedWorkingDirectory!);
        var trustLevels = ResolveProjectTrustLevels(mergedConfig);
        return new ProjectTrustContext(projectRoot, gitRoot, trustLevels, userConfigPath);
    }

    private static Dictionary<string, ProjectTrustLevel> ResolveProjectTrustLevels(TomlTable mergedConfig)
    {
        var trustLevels = new Dictionary<string, ProjectTrustLevel>(PathComparer);
        if (!mergedConfig.TryGetValue("projects", out var rawProjects)
            || rawProjects is not TomlTable projects)
        {
            return trustLevels;
        }

        foreach (var project in projects)
        {
            if (project.Value is not TomlTable projectConfig)
            {
                continue;
            }

            var trustLevel = ParseTrustLevel(
                ReadString(projectConfig, "trust_level") ?? ReadString(projectConfig, "trustLevel"));
            if (trustLevel == ProjectTrustLevel.Unknown)
            {
                continue;
            }

            string? normalizedProjectPath;
            try
            {
                normalizedProjectPath = NormalizeDirectory(project.Key);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(normalizedProjectPath))
            {
                continue;
            }

            trustLevels[normalizedProjectPath] = trustLevel;
        }

        return trustLevels;
    }

    private static string? ResolveDisabledReason(ProjectTrustContext trustContext, string? directory)
        => trustContext.ResolveTrustLevel(directory) == ProjectTrustLevel.Untrusted
            ? trustContext.BuildDisabledReason(directory)
            : null;

    private static string? ResolveImplicitApprovalPolicy(ProjectTrustContext? trustContext, string? directory)
        => trustContext?.ResolveTrustLevel(directory) == ProjectTrustLevel.Untrusted
            ? "untrusted"
            : null;

    private static string? ResolveProjectLayerDirectory(string dotTianShuFolderPath)
    {
        if (string.IsNullOrWhiteSpace(dotTianShuFolderPath))
        {
            return null;
        }

        return NormalizeDirectory(Directory.GetParent(dotTianShuFolderPath)?.FullName);
    }

    private static TomlTable LoadMergedRoot(IReadOnlyList<ConfigLayer> layers, bool includeDisabled)
    {
        var merged = new TomlTable();
        foreach (var layer in layers)
        {
            if (!includeDisabled && layer.IsDisabled)
            {
                continue;
            }

            MergeTables(merged, layer.Root);
        }

        return merged;
    }

    private static TomlTable ReadTomlRoot(string path)
    {
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TomlTable();
        }

        var syntax = Toml.Parse(text);
        if (syntax.HasErrors)
        {
            var first = syntax.Diagnostics.FirstOrDefault()?.ToString() ?? "TOML 解析失败";
            throw new FormatException(first);
        }

        if (syntax.ToModel() is not TomlTable root)
        {
            throw new InvalidOperationException("tianshu.toml 结构无效。");
        }

        return root;
    }

    private static void MergeTables(TomlTable target, TomlTable source)
    {
        foreach (var pair in source)
        {
            if (pair.Value is TomlTable sourceTable
                && target.TryGetValue(pair.Key, out var existing)
                && existing is TomlTable targetTable)
            {
                MergeTables(targetTable, sourceTable);
                continue;
            }

            target[pair.Key] = pair.Value;
        }
    }

    private static IReadOnlyList<string> ResolveProjectRootMarkers(TomlTable root)
    {
        if (!TryGetTomlValue(root, "project_root_markers", out var rawMarkers)
            && !TryGetTomlValue(root, "projectRootMarkers", out rawMarkers))
        {
            return DefaultProjectRootMarkers;
        }

        if (rawMarkers is not TomlArray markers)
        {
            throw new FormatException("project_root_markers must be an array of strings");
        }

        if (markers.Count == 0)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>(markers.Count);
        foreach (var marker in markers)
        {
            if (marker is not string text)
            {
                throw new FormatException("project_root_markers must be an array of strings");
            }

            var normalized = Normalize(text);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                values.Add(normalized!);
            }
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizePolicyStrategySandboxMode(string? sandboxMode)
    {
        var normalized = Normalize(sandboxMode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized!.ToLowerInvariant() switch
        {
            "readonly" or "read-only" or "read_only" => "read-only",
            "workspacewrite" or "workspace-write" or "workspace_write" => "workspace-write",
            _ => null,
        };
    }

    private static bool TryGetTomlValue(TomlTable table, string key, out object? value)
    {
        foreach (var pair in table)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static IReadOnlyList<string> EnumerateProjectLayerDirectories(string workingDirectory, IReadOnlyList<string> projectRootMarkers)
    {
        var directories = new List<string>();
        foreach (var current in EnumerateDirectoriesBetweenProjectRootAndCwd(workingDirectory, projectRootMarkers))
        {
            var dotTianShuDirectory = Path.Combine(current, ".tianshu");
            if (Directory.Exists(dotTianShuDirectory))
            {
                directories.Add(NormalizeDirectory(dotTianShuDirectory) ?? dotTianShuDirectory);
            }
        }

        return directories;
    }

    private static string? NormalizeDirectory(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var fullPath = Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, normalized));

        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath);
        }

        return fullPath;
    }

    private static IReadOnlyList<string> EnumerateDirectoriesBetweenProjectRootAndCwd(string cwd, IReadOnlyList<string> projectRootMarkers)
    {
        var projectRoot = FindProjectRoot(cwd, projectRootMarkers);
        var stack = new Stack<string>();
        var current = cwd;
        while (!string.IsNullOrWhiteSpace(current))
        {
            stack.Push(current);
            if (string.Equals(current, projectRoot, PathComparison))
            {
                break;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, PathComparison))
            {
                break;
            }

            current = parent;
        }

        var directories = new List<string>(stack.Count);
        while (stack.TryPop(out var directory))
        {
            directories.Add(directory);
        }

        return directories;
    }

    private static string FindProjectRoot(string cwd, IReadOnlyList<string> projectRootMarkers)
    {
        if (projectRootMarkers.Count == 0)
        {
            return cwd;
        }

        var current = cwd;
        while (!string.IsNullOrWhiteSpace(current))
        {
            foreach (var marker in projectRootMarkers)
            {
                var markerPath = Path.Combine(current, marker);
                if (File.Exists(markerPath) || Directory.Exists(markerPath))
                {
                    return current;
                }
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, PathComparison))
            {
                break;
            }

            current = parent;
        }

        return cwd;
    }

    private static string? FindGitRoot(string cwd)
    {
        var current = cwd;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var markerPath = Path.Combine(current, ".git");
            if (File.Exists(markerPath) || Directory.Exists(markerPath))
            {
                return current;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, PathComparison))
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    private static ProjectTrustLevel ParseTrustLevel(string? value)
    {
        var normalized = Normalize(value);
        return normalized?.ToLowerInvariant() switch
        {
            "trusted" => ProjectTrustLevel.Trusted,
            "untrusted" => ProjectTrustLevel.Untrusted,
            _ => ProjectTrustLevel.Unknown,
        };
    }

    private static string ResolveAdapter()
        => ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId();

    private static string? NormalizeProviderWireApi(string? wireApi, string? modelProvider)
    {
        var normalized = Normalize(wireApi);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        try
        {
            return ProviderWireApi.NormalizeOrThrow(normalized, "provider protocol");
        }
        catch (InvalidOperationException ex)
        {
            var settingPath = string.IsNullOrWhiteSpace(modelProvider)
                ? "providers.<active>.default_protocol"
                : $"providers.{modelProvider}.default_protocol";

            throw new InvalidOperationException($"配置项 `{settingPath}` {ex.Message}", ex);
        }
    }

    private static string? NormalizeProtocolAdapter(string? adapterId, string source)
        => ProviderRuntimeBootstrapRegistry.NormalizeOptionalProtocolAdapterId(adapterId, source);

    private static TomlTable? ReadProfile(TomlTable root, string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return null;
        }

        if (!root.TryGetValue("profiles", out var profilesObj) || profilesObj is not TomlTable profiles)
        {
            throw new InvalidOperationException($"config profile `{profileName}` not found");
        }

        if (!profiles.TryGetValue(profileName, out var profileObj))
        {
            throw new InvalidOperationException($"config profile `{profileName}` not found");
        }

        return profileObj as TomlTable
            ?? throw new InvalidOperationException($"config profile `{profileName}` not found");
    }

    private static TomlTable? ReadProvider(TomlTable root, string? modelProvider)
    {
        if (string.IsNullOrWhiteSpace(modelProvider))
        {
            return null;
        }

        if (root.TryGetValue("providers", out var TianShuProvidersObj)
            && TianShuProvidersObj is TomlTable TianShuProviders
            && TianShuProviders.TryGetValue(modelProvider, out var TianShuProviderObj))
        {
            return TianShuProviderObj as TomlTable;
        }

        return null;
    }

    private static ActiveRouteCandidate? ResolveActiveRouteCandidate(Dictionary<string, object?> rawConfig)
    {
        var routeSet = TianShuModelRouteSetDefaults.ResolveRouteSet(rawConfig);
        var route = routeSet.Routes.FirstOrDefault(static item =>
                        string.Equals(item.Kind, TianShuModelRouteSetDefaults.DefaultRouteKind, StringComparison.OrdinalIgnoreCase))
                    ?? routeSet.Routes.FirstOrDefault();
        var candidate = route?.Candidates.FirstOrDefault(static item =>
            !string.IsNullOrWhiteSpace(item.Provider)
            && !string.IsNullOrWhiteSpace(item.Model)
            && string.IsNullOrWhiteSpace(item.UnavailableReason));
        return candidate is null
            ? null
            : new ActiveRouteCandidate(candidate.Provider, candidate.Model, candidate.Protocol);
    }

    private static bool HasActiveRouteSetReference(Dictionary<string, object?> rawConfig)
    {
        if (!string.IsNullOrWhiteSpace(ReadRawString(rawConfig, "model_route_set")))
        {
            return true;
        }

        var activeProfile = ReadRawString(rawConfig, "profile") ?? "default";
        return TryReadRawObject(rawConfig, "profiles", out var profiles)
               && TryReadRawObject(profiles, activeProfile, out var profile)
               && !string.IsNullOrWhiteSpace(ReadRawString(profile, "model_route_set"));
    }

    private static string? ReadRawString(Dictionary<string, object?> rawConfig, string key)
        => rawConfig.TryGetValue(key, out var value) ? Normalize(value?.ToString()) : null;

    private static bool TryReadRawObject(Dictionary<string, object?> rawConfig, string key, out Dictionary<string, object?> value)
    {
        value = null!;
        if (!rawConfig.TryGetValue(key, out var rawValue))
        {
            return false;
        }

        if (rawValue is Dictionary<string, object?> dictionary)
        {
            value = dictionary;
            return true;
        }

        if (rawValue is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            value = new Dictionary<string, object?>(readOnlyDictionary, StringComparer.Ordinal);
            return true;
        }

        return false;
    }

    private static string? ReadMergedString(TomlTable root, TomlTable? profile, string key)
        => ReadString(profile, key) ?? ReadString(root, key);

    private static long? ReadMergedLong(TomlTable root, TomlTable? profile, string key)
        => ReadLong(profile, key) ?? ReadLong(root, key);

    private static bool? ReadMergedBool(TomlTable root, TomlTable? profile, string key)
        => ReadBool(profile, key) ?? ReadBool(root, key);

    private static NativeProfileTables ResolveNativeProfile(TomlTable root, TomlTable? profile)
    {
        var executionId = ReadString(profile, "execution") ?? "default";
        var execution = ReadNamedTable(root, "execution_profiles", executionId);
        var agentId = ReadString(profile, "agent")
                      ?? ReadString(execution, "agent")
                      ?? "default";
        var agent = ReadNamedTable(root, "agents", agentId);
        return new NativeProfileTables(agent, execution);
    }

    private static TomlTable? ReadNamedTable(TomlTable root, string tableName, string? entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)
            || !root.TryGetValue(tableName, out var tableObj)
            || tableObj is not TomlTable table
            || !table.TryGetValue(entryName, out var entryObj))
        {
            return null;
        }

        return entryObj as TomlTable;
    }

    private static TomlTable? ReadChildTable(TomlTable? table, string key)
        => table is not null
           && table.TryGetValue(key, out var value)
           && value is TomlTable child
            ? child
            : null;

    private static string? ReadString(TomlTable? table, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
        {
            return null;
        }

        return Normalize(value?.ToString());
    }

    private static long? ReadLong(TomlTable? table, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            long typedLong => typedLong,
            int typedInt => typedInt,
            string text when long.TryParse(text, out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool? ReadBool(TomlTable? table, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            bool typedBool => typedBool,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? Pick(string? existing, string? incoming)
    {
        var current = Normalize(existing);
        return !string.IsNullOrWhiteSpace(current) ? current : Normalize(incoming);
    }

    private static string NormalizeFilePath(string path)
    {
        var normalized = Normalize(path)
            ?? throw new InvalidOperationException("tianshu.toml 路径无效。");
        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, normalized));
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

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly record struct ConfigLayer(
        ResolvedTianShuConfigLayerSourceKind SourceKind,
        string? Path,
        string? DirectoryPath,
        TomlTable Root,
        string? DisabledReason,
        bool FileExists)
    {
        public bool IsDisabled => !string.IsNullOrWhiteSpace(DisabledReason);
    }

    private sealed record ResolvedLoadLayers(
        IReadOnlyList<ConfigLayer> Layers,
        string? NormalizedWorkingDirectory,
        ProjectTrustContext? TrustContext,
        WorkspaceResolverEffectivePolicy WorkspaceResolverPolicy);

    private sealed record NativeProfileTables(TomlTable? Agent, TomlTable? Execution);

    private sealed record ActiveRouteCandidate(string Provider, string Model, string? Protocol);

    private sealed class ProjectTrustContext(
        string projectRoot,
        string? gitRoot,
        IReadOnlyDictionary<string, ProjectTrustLevel> trustLevels,
        string userConfigPath)
    {
        public ProjectTrustLevel ResolveTrustLevel(string? directory)
            => ResolveTrustDecision(directory).TrustLevel;

        public string BuildDisabledReason(string? directory)
        {
            var decision = ResolveTrustDecision(directory);
            return decision.TrustLevel == ProjectTrustLevel.Untrusted
                ? $"{decision.TrustKey} is marked as untrusted in {userConfigPath}. To load tianshu.toml, mark it trusted."
                : $"To load tianshu.toml, add {decision.TrustKey} as a trusted project in {userConfigPath}.";
        }

        private ProjectTrustDecision ResolveTrustDecision(string? directory)
        {
            var normalizedDirectory = NormalizeDirectory(directory);
            if (string.IsNullOrWhiteSpace(normalizedDirectory))
            {
                return new ProjectTrustDecision(ProjectTrustLevel.Unknown, gitRoot ?? projectRoot);
            }

            if (trustLevels.TryGetValue(normalizedDirectory, out var directTrustLevel))
            {
                return new ProjectTrustDecision(directTrustLevel, normalizedDirectory);
            }

            if (trustLevels.TryGetValue(projectRoot, out var projectRootTrustLevel))
            {
                return new ProjectTrustDecision(projectRootTrustLevel, projectRoot);
            }

            if (!string.IsNullOrWhiteSpace(gitRoot)
                && trustLevels.TryGetValue(gitRoot, out var gitRootTrustLevel))
            {
                return new ProjectTrustDecision(gitRootTrustLevel, gitRoot);
            }

            return new ProjectTrustDecision(ProjectTrustLevel.Unknown, gitRoot ?? projectRoot);
        }
    }

    private readonly record struct ProjectTrustDecision(ProjectTrustLevel TrustLevel, string TrustKey);

    private enum ProjectTrustLevel
    {
        Unknown = 0,
        Trusted = 1,
        Untrusted = 2,
    }
}
