using System.Net.Http.Headers;
using System.Text.Json;
using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.RuntimeComposition;

namespace TianShu.Cli;

internal static class CliOnboardingCommandRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static int RunInit(InitCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var bootstrap = CliFirstRunBootstrapper.EnsureDefaultConfiguration(
            options,
            options.Provider,
            options.Force);
        var config = LoadResolvedConfig(options);
        var result = new CliInitResult(
            Success: true,
            ConfigPath: bootstrap.ConfigPath,
            Provider: bootstrap.Provider,
            WrittenPaths: bootstrap.WrittenPaths,
            SkippedPaths: bootstrap.SkippedPaths,
            ApiKeyEnvironmentVariable: config.ProviderEnvKey,
            NextSteps: BuildNextSteps(config));

        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        }

        Console.WriteLine($"TianShu configuration initialized at: {result.ConfigPath}");
        Console.WriteLine($"Active provider: {result.Provider}");
        Console.WriteLine($"API key environment variable: {result.ApiKeyEnvironmentVariable ?? "<not resolved>"}");
        if (result.WrittenPaths.Count > 0)
        {
            Console.WriteLine("Written:");
            foreach (var path in result.WrittenPaths)
            {
                Console.WriteLine($"  {path}");
            }
        }

        if (result.SkippedPaths.Count > 0)
        {
            Console.WriteLine("Preserved existing files:");
            foreach (var path in result.SkippedPaths)
            {
                Console.WriteLine($"  {path}");
            }
        }

        Console.WriteLine("Next steps:");
        foreach (var step in result.NextSteps)
        {
            Console.WriteLine($"  {step}");
        }

        return 0;
    }

    public static async Task<int> RunDoctorAsync(DoctorCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var result = await BuildDoctorResultAsync(options, cancellationToken).ConfigureAwait(false);
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return result.Ready ? 0 : 1;
        }

        Console.WriteLine($"TianShu home: {result.TianShuHome}");
        Console.WriteLine($"Portable mode: {(result.PortableMode ? "yes" : "no")}");
        Console.WriteLine($"Package RID: {result.PackageRuntimeIdentifier ?? "<missing>"} (current={result.CurrentRuntimeIdentifier}, match={result.RuntimeIdentifierMatches?.ToString() ?? "unknown"})");
        Console.WriteLine($"Config: {result.ConfigPath} ({(result.ConfigExists ? "found" : "missing")})");
        Console.WriteLine($"Modules root: {result.ModulesRoot}");
        Console.WriteLine($"Runtime root: {result.RuntimeRoot} ({(result.RuntimeWritable ? "writable" : "not writable")})");
        Console.WriteLine($"AppHost: {result.AppHostPath} ({(result.AppHostExists ? "found" : "missing")})");
        Console.WriteLine($"Provider: {result.ModelProvider ?? "<missing>"}");
        Console.WriteLine($"Model: {result.Model ?? "<missing>"}");
        Console.WriteLine($"Wire API: {result.ProviderWireApi ?? "<missing>"}");
        Console.WriteLine($"Base URL: {result.ProviderBaseUrl ?? "<missing>"}");
        Console.WriteLine($"API key env: {result.ApiKeyEnvironmentVariable ?? "<missing>"} ({(result.ApiKeyEnvironmentVariablePresent ? "set" : "missing")})");
        Console.WriteLine($"Probe: {(result.ProbeRequested ? result.ProbeStatus ?? "not-run" : "not requested")}");
        Console.WriteLine($"Modules: discovered={result.Modules.DiscoveredCount}, selected={result.Modules.SelectedCount}, registered={result.Modules.RegisteredCount}, rejected={result.Modules.RejectedCount}, unavailable={result.Modules.UnavailableCount}, risks={result.Modules.GovernanceRiskCount}");
        if (result.Modules.Candidates.Count > 0)
        {
            Console.WriteLine("Module details:");
            foreach (var module in result.Modules.Candidates)
            {
                Console.WriteLine($"  {module.ModuleId} ({module.Kind}) discovery={module.DiscoveryStatus} load={module.LoadStatus ?? "n/a"} health={module.HealthStatus ?? "n/a"}");
                foreach (var diagnostic in module.Diagnostics)
                {
                    Console.WriteLine($"    [{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
                }

                foreach (var suggestion in module.RepairSuggestions)
                {
                    Console.WriteLine($"    fix: {suggestion}");
                }
            }
        }

        if (result.Issues.Count > 0)
        {
            Console.WriteLine("Issues:");
            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
            }
        }

        if (result.NextSteps.Count > 0)
        {
            Console.WriteLine("Next steps:");
            foreach (var step in result.NextSteps)
            {
                Console.WriteLine($"  {step}");
            }
        }

        return result.Ready ? 0 : 1;
    }

    public static async Task<CliDoctorResult> BuildDoctorResultAsync(DoctorCommandOptions options, CancellationToken cancellationToken)
    {
        var configPath = Path.GetFullPath(options.ConfigFilePath);
        var tianShuHome = Path.GetDirectoryName(configPath) ?? TianShu.Configuration.TianShuHomePathUtilities.ResolveTianShuHomePath();
        var portableLayout = TianShuRuntimeLayoutPaths.TryResolvePortableTianShuHomeLayout();
        var portableMode = portableLayout is not null
            && string.Equals(Path.GetFullPath(portableLayout.ConfigFilePath), configPath, PathComparison);
        var runtimeRoot = TianShuRuntimeLayoutPaths.ResolveRuntimePathFromHome(tianShuHome);
        var runtimeWriteCheck = CliRuntimeWriteGuard.CheckTianShuHomeRuntimePath(
            tianShuHome,
            options.WorkingDirectory,
            "kernel-runtime");
        var appHostPath = Path.Combine(
            runtimeRoot,
            "apphost",
            OperatingSystem.IsWindows() ? "TianShu.AppHost.exe" : "TianShu.AppHost");
        var appHostExists = File.Exists(appHostPath);
        var issues = new List<CliDoctorIssue>();
        var configExists = File.Exists(configPath);
        var versionPath = Path.Combine(tianShuHome, "VERSION.txt");
        var packageRuntimeIdentifier = ReadPackageRuntimeIdentifier(versionPath);
        var currentRuntimeIdentifier = ResolveCurrentRuntimeIdentifier();
        var runtimeIdentifierMatches = packageRuntimeIdentifier is null
            ? (bool?)null
            : string.Equals(packageRuntimeIdentifier, currentRuntimeIdentifier, StringComparison.OrdinalIgnoreCase);
        var providerInstancesPath = ResolveModulePath(configPath, "model", "provider-instances", "default.toml");
        var routeSetPath = ResolveModulePath(configPath, "model", "route-sets", "default.toml");
        var protocolRulesPath = ResolveModulePath(configPath, "model", "protocol-rules", "default.toml");

        AddFileIssue(issues, configPath, "config_missing", "Run `tianshu init` to create the default configuration.");
        AddFileIssue(issues, providerInstancesPath, "provider_instances_missing", "Run `tianshu init` to create provider templates.");
        AddFileIssue(issues, routeSetPath, "route_set_missing", "Run `tianshu init` to create model route templates.");
        AddFileIssue(issues, protocolRulesPath, "protocol_rules_missing", "Run `tianshu init` to create protocol rule templates.");
        if (!runtimeWriteCheck.Available)
        {
            issues.Add(new("error", runtimeWriteCheck.FailureCode ?? CliRuntimeWriteGuard.RuntimeNotWritableCode, runtimeWriteCheck.FailureMessage ?? "TianShuHome runtime root is not writable. 天枢运行目录不可写。"));
        }

        if (portableMode && packageRuntimeIdentifier is null)
        {
            issues.Add(new(
                "warning",
                "package_runtime_identifier_missing",
                $"Package VERSION.txt is missing runtimeIdentifier; platform/RID cannot be verified. 包内 VERSION.txt 缺少 runtimeIdentifier，无法验证当前平台是否匹配。Path: {versionPath}"));
        }

        if (runtimeIdentifierMatches == false)
        {
            issues.Add(new(
                "error",
                "package_runtime_identifier_mismatch",
                $"Package runtimeIdentifier `{packageRuntimeIdentifier}` does not match current platform `{currentRuntimeIdentifier}`. 当前便携包平台/RID 与本机不匹配，请下载匹配当前平台的 TianShu 包。Path: {versionPath}"));
        }

        if (!appHostExists)
        {
            issues.Add(new(
                portableMode ? "error" : "warning",
                "apphost_missing",
                $"Missing AppHost runtime entry: {appHostPath}"));
        }

        ResolvedTianShuConfig? config = null;
        if (configExists)
        {
            try
            {
                config = LoadResolvedConfig(options);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException)
            {
                issues.Add(new("error", "config_load_failed", ex.Message));
            }
        }

        var apiKeyEnv = config?.ProviderEnvKey;
        var apiKeyPresent = !string.IsNullOrWhiteSpace(apiKeyEnv)
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(apiKeyEnv!));
        if (config is not null)
        {
            if (string.IsNullOrWhiteSpace(config.ModelProvider))
            {
                issues.Add(new("error", "provider_missing", "No active provider could be resolved."));
            }

            if (string.IsNullOrWhiteSpace(config.Model))
            {
                issues.Add(new("error", "model_missing", "No active model could be resolved."));
            }

            if (string.IsNullOrWhiteSpace(config.ProviderBaseUrl))
            {
                issues.Add(new("error", "provider_base_url_missing", "The active provider has no base_url."));
            }

            if (string.IsNullOrWhiteSpace(apiKeyEnv))
            {
                issues.Add(new("error", "provider_api_key_env_missing", "The active provider has no api_key_env."));
            }
            else if (!apiKeyPresent)
            {
                issues.Add(new("error", "provider_api_key_missing", $"Environment variable `{apiKeyEnv}` is not set."));
            }
        }

        var assemblyIssues = CheckPackagedAssemblies();
        issues.AddRange(assemblyIssues);
        var moduleDiagnostics = await BuildModuleDoctorResultAsync(tianShuHome, cancellationToken: cancellationToken).ConfigureAwait(false);
        issues.AddRange(moduleDiagnostics.Issues);

        string? probeStatus = null;
        string? probeMessage = null;
        if (options.Probe)
        {
            var probe = await ProbeProviderAsync(config, apiKeyPresent, cancellationToken).ConfigureAwait(false);
            probeStatus = probe.Status;
            probeMessage = probe.Message;
            if (!probe.Success)
            {
                issues.Add(new("error", probe.Code, probe.Message));
            }
        }

        var ready = issues.All(static issue => !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
        return new CliDoctorResult(
            Ready: ready,
            ConfigPath: configPath,
            TianShuHome: tianShuHome,
            PortableMode: portableMode,
            PackageRoot: portableLayout?.PackageRoot,
            PackageRuntimeIdentifier: packageRuntimeIdentifier,
            CurrentRuntimeIdentifier: currentRuntimeIdentifier,
            RuntimeIdentifierMatches: runtimeIdentifierMatches,
            ModulesRoot: Path.Combine(tianShuHome, "modules"),
            RuntimeRoot: runtimeRoot,
            RuntimeWorkspaceRoot: runtimeWriteCheck.RuntimeWorkspaceRoot,
            RuntimeWritable: runtimeWriteCheck.Available,
            AppHostPath: appHostPath,
            AppHostExists: appHostExists,
            ConfigExists: configExists,
            ProviderInstancesPath: providerInstancesPath,
            ProviderInstancesExists: File.Exists(providerInstancesPath),
            RouteSetPath: routeSetPath,
            RouteSetExists: File.Exists(routeSetPath),
            ProtocolRulesPath: protocolRulesPath,
            ProtocolRulesExists: File.Exists(protocolRulesPath),
            ModelProvider: config?.ModelProvider,
            Model: config?.Model,
            ProviderWireApi: config?.ProviderWireApi,
            ProviderBaseUrl: config?.ProviderBaseUrl,
            ApiKeyEnvironmentVariable: apiKeyEnv,
            ApiKeyEnvironmentVariablePresent: apiKeyPresent,
            ProbeRequested: options.Probe,
            ProbeStatus: probeStatus,
            ProbeMessage: probeMessage,
            Modules: moduleDiagnostics,
            Issues: issues,
            NextSteps: BuildNextSteps(config));
    }

    internal static async ValueTask<CliModuleDoctorResult> BuildModuleDoctorResultAsync(
        string tianShuHome,
        IReadOnlyList<ModuleDescriptor>? builtInDescriptors = null,
        IReadOnlySet<string>? boundConfigurationKeys = null,
        IReadOnlyList<ModuleDiscoveryRoot>? additionalRoots = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuHome);
        cancellationToken.ThrowIfCancellationRequested();

        var descriptors = builtInDescriptors ?? BuildDefaultDoctorBuiltInModuleDescriptors();
        var discovery = new TianShuModuleManifestDiscovery().Load(
            tianShuHome,
            descriptors,
            additionalRoots);
        var policy = new ModuleLoadingPolicy(
            "0.6.0",
            explicitlyAllowedModuleIds: BuildDefaultAllowedModuleIds(descriptors),
            boundConfigurationKeys: boundConfigurationKeys ?? BuildDefaultBoundConfigurationKeys(descriptors));
        var plan = await new DefaultModuleCompositionRoot().ComposeAsync(
            new ModuleCompositionRootContext(discovery, policy),
            cancellationToken).ConfigureAwait(false);
        var recordsByModuleId = plan.Records
            .GroupBy(static record => record.Candidate.Manifest.ModuleId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var candidates = discovery.Candidates
            .Select(candidate => ToModuleCandidateDoctorResult(candidate, recordsByModuleId))
            .OrderBy(static candidate => candidate.ModuleId, StringComparer.Ordinal)
            .ToArray();
        var issues = discovery.Issues.Select(ToCliDoctorIssue)
            .Concat(plan.Diagnostics.Select(ToCliDoctorIssue))
            .ToArray();

        return new CliModuleDoctorResult(
            RootCount: discovery.Roots.Count,
            DiscoveredCount: discovery.Candidates.Count,
            SelectedCount: discovery.SelectedCandidates.Count,
            RegisteredCount: plan.RegisteredRecords.Count,
            RejectedCount: plan.Records.Count(static record => record.Status == ModuleLoadStatus.Rejected),
            UnavailableCount: plan.Records.Count(static record => record.Status == ModuleLoadStatus.Unavailable),
            SkippedCount: plan.Records.Count(static record => record.Status == ModuleLoadStatus.Skipped),
            MissingConfigurationCount: candidates.Sum(static candidate => candidate.MissingConfigurationKeys.Count),
            GovernanceRiskCount: candidates.Sum(static candidate => candidate.GovernanceRisks.Count),
            Candidates: candidates,
            Issues: issues);
    }

    private static ResolvedTianShuConfig LoadResolvedConfig(CliRuntimeCommandOptions options)
        => new RuntimeConfigurationComposition().Load(
            options.ConfigFilePath,
            options.ProfileName,
            options.ConfigOverrides,
            options.WorkingDirectory);

    private static IReadOnlyList<string> BuildNextSteps(ResolvedTianShuConfig? config)
    {
        if (config is null)
        {
            return ["tianshu init --provider openai"];
        }

        var steps = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.ProviderEnvKey)
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(config.ProviderEnvKey!)))
        {
            steps.Add($"Set {config.ProviderEnvKey}=<your key>");
        }

        steps.Add("tianshu doctor");
        steps.Add("tianshu doctor --probe");
        steps.Add("tianshu send --message \"hello\"");
        return steps;
    }

    private static IReadOnlyList<CliDoctorIssue> CheckPackagedAssemblies()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var issues = new List<CliDoctorIssue>();
        foreach (var assemblyName in new[]
                 {
                     "TianShu.Provider.OpenAI.dll",
                     "TianShu.Provider.Anthropic.dll",
                     "TianShu.Provider.OpenAICompatible.dll",
                     "TianShu.Tools.FileSystem.dll",
                     "TianShu.Tools.FileSystemMutating.dll",
                 })
        {
            if (!File.Exists(Path.Combine(baseDirectory, assemblyName)))
            {
                issues.Add(new("error", "packaged_assembly_missing", $"Missing packaged assembly: {assemblyName}"));
            }
        }

        return issues;
    }

    internal static string ResolveCurrentRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : OperatingSystem.IsLinux()
                    ? "linux"
                    : "unknown";
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            _ => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };

        return $"{os}-{architecture}";
    }

    private static string? ReadPackageRuntimeIdentifier(string versionPath)
    {
        if (!File.Exists(versionPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(versionPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();
            if (!string.Equals(key, "runtimeIdentifier", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = trimmed[(separator + 1)..].Trim().Trim('"', '\'');
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static IReadOnlyList<ModuleDescriptor> BuildDefaultDoctorBuiltInModuleDescriptors()
        =>
        [
            WithDoctorHealth(BuiltInModuleDescriptors.Diagnostics(), "TianShu.Diagnostics.dll"),
            WithDoctorHealth(BuiltInModuleDescriptors.WorkspaceEnvironment(), "TianShu.RuntimeComposition.dll"),
            WithDoctorHealth(BuiltInModuleDescriptors.Configuration(), "TianShu.Configuration.dll"),
            WithDoctorHealth(BuiltInModuleDescriptors.SubAgent(), "TianShu.SubAgent.dll"),
        ];

    private static ModuleDescriptor WithDoctorHealth(ModuleDescriptor descriptor, string assemblyName)
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, assemblyName);
        var exists = File.Exists(assemblyPath);
        return new ModuleDescriptor(
            descriptor.ModuleId,
            descriptor.Kind,
            descriptor.DisplayName,
            descriptor.Version,
            descriptor.Capabilities,
            descriptor.ConfigurationSchema,
            descriptor.Permission,
            descriptor.SideEffects,
            descriptor.Audit,
            descriptor.TrustLevel,
            descriptor.RequiredConfiguration,
            descriptor.RuntimeDependencies,
            descriptor.MinimumTianShuVersion,
            new ModuleHealthProbe(
                exists ? ModuleHealthStatus.Healthy : ModuleHealthStatus.Unavailable,
                exists ? "Packaged assembly found." : $"Missing packaged assembly: {assemblyName}",
                DateTimeOffset.UtcNow,
                [assemblyName]),
            descriptor.ImplementationBinding,
            descriptor.Metadata);
    }

    private static IReadOnlySet<string> BuildDefaultAllowedModuleIds(IReadOnlyList<ModuleDescriptor> descriptors)
        => descriptors
            .Where(static descriptor => descriptor.TrustLevel is ModuleTrustLevel.BuiltIn or ModuleTrustLevel.WorkspaceTrusted or ModuleTrustLevel.UserInstalled)
            .Select(static descriptor => descriptor.ModuleId)
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlySet<string> BuildDefaultBoundConfigurationKeys(IReadOnlyList<ModuleDescriptor> descriptors)
        => descriptors
            .SelectMany(static descriptor => descriptor.RequiredConfiguration)
            .Where(static requirement => requirement.Required)
            .Select(static requirement => requirement.Key)
            .ToHashSet(StringComparer.Ordinal);

    private static CliModuleCandidateDoctorResult ToModuleCandidateDoctorResult(
        ModuleDiscoveryCandidate candidate,
        IReadOnlyDictionary<string, ModuleLoadRecord[]> recordsByModuleId)
    {
        recordsByModuleId.TryGetValue(candidate.Manifest.ModuleId, out var matchingRecords);
        var record = matchingRecords?
            .FirstOrDefault(record => Equals(record.Candidate, candidate))
            ?? matchingRecords?.FirstOrDefault();
        var loadDiagnostics = record?.Diagnostics ?? Array.Empty<ModuleLoadDiagnostic>();
        var missingConfiguration = loadDiagnostics
            .Where(static diagnostic => string.Equals(diagnostic.Code, "module_load.required_configuration_missing", StringComparison.Ordinal))
            .Select(static diagnostic => ExtractConfigurationKey(diagnostic.Message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var governanceRisks = BuildGovernanceRisks(candidate, record).ToArray();
        var diagnostics = loadDiagnostics
            .Select(ToCliModuleDiagnostic)
            .ToArray();
        var suggestions = loadDiagnostics
            .SelectMany(static diagnostic => BuildRepairSuggestions(diagnostic.Code, diagnostic.Message))
            .Concat(candidate.StatusReason is null ? Array.Empty<string>() : BuildRepairSuggestions(candidate.StatusReason, candidate.StatusReason))
            .Concat(governanceRisks.Select(static risk => $"Review module governance risk `{risk}` before enabling the module."))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new CliModuleCandidateDoctorResult(
            ModuleId: candidate.Manifest.ModuleId,
            Kind: candidate.Manifest.Kind.ToString(),
            DisplayName: candidate.Manifest.DisplayName,
            Version: candidate.Manifest.Version,
            SourceKind: candidate.Manifest.Source.Root.SourceKind.ToString(),
            ManifestPath: candidate.Manifest.Source.ManifestPath,
            DiscoveryStatus: candidate.Status.ToString(),
            DiscoveryReason: candidate.StatusReason,
            LoadStatus: record?.Status.ToString(),
            HealthStatus: candidate.Descriptor?.Health.Status.ToString(),
            MissingConfigurationKeys: missingConfiguration,
            GovernanceRisks: governanceRisks,
            Diagnostics: diagnostics,
            RepairSuggestions: suggestions);
    }

    private static IEnumerable<string> BuildGovernanceRisks(ModuleDiscoveryCandidate candidate, ModuleLoadRecord? record)
    {
        if (candidate.Manifest.Source.Root.SourceKind is ModuleDiscoverySourceKind.ThirdPartyDirectory or ModuleDiscoverySourceKind.Package
            && record?.Diagnostics.Any(static diagnostic => diagnostic.Code == "module_load.third_party_not_allowed") == true)
        {
            yield return "third_party_requires_explicit_allow_list";
        }

        if (candidate.Descriptor is null)
        {
            yield return "descriptor_missing";
            yield break;
        }

        if (candidate.Descriptor.SideEffects.Level >= SideEffectLevel.HostMutation
            && !candidate.Descriptor.Permission.RequiresHumanGate)
        {
            yield return "high_side_effect_without_human_gate";
        }

        if (!candidate.Descriptor.Audit.Required)
        {
            yield return "audit_not_required";
        }
    }

    private static string ExtractConfigurationKey(string message)
    {
        const string marker = "Module required configuration is not bound:";
        var index = message.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? message : message[(index + marker.Length)..].Trim();
    }

    private static CliDoctorIssue ToCliDoctorIssue(ModuleDiscoveryIssue issue)
        => new(ToDoctorSeverity(issue.Severity), issue.Code, issue.Message);

    private static CliDoctorIssue ToCliDoctorIssue(ModuleLoadDiagnostic diagnostic)
        => new(ToDoctorSeverity(diagnostic.Severity), diagnostic.Code, diagnostic.Message);

    private static CliModuleDiagnostic ToCliModuleDiagnostic(ModuleLoadDiagnostic diagnostic)
        => new(ToDoctorSeverity(diagnostic.Severity), diagnostic.Code, diagnostic.Message);

    private static string ToDoctorSeverity(ModuleDiscoveryIssueSeverity severity)
        => severity switch
        {
            ModuleDiscoveryIssueSeverity.Info => "info",
            ModuleDiscoveryIssueSeverity.Warning => "warning",
            _ => "error",
        };

    private static string ToDoctorSeverity(ModuleLoadDiagnosticSeverity severity)
        => severity switch
        {
            ModuleLoadDiagnosticSeverity.Info => "info",
            ModuleLoadDiagnosticSeverity.Warning => "warning",
            _ => "error",
        };

    private static IEnumerable<string> BuildRepairSuggestions(string code, string message)
    {
        switch (code)
        {
            case "module_manifest.kind_unspecified":
            case "module_manifest.parse_failed":
                yield return "Fix module.toml fields: id, kind, version and implementation.";
                break;
            case "module_discovery.disabled":
                yield return "Enable the module manifest or remove the module id from the disabled list.";
                break;
            case "module_discovery.duplicate_rejected":
                yield return "Keep one module id per module family or rename the lower-priority module.";
                break;
            case "module_load.descriptor_missing":
                yield return "Provide a ModuleDescriptor projection for this module before loading it.";
                break;
            case "module_load.descriptor_manifest_mismatch":
                yield return "Make ModuleDescriptor.ModuleId/Kind match module.toml id/kind.";
                break;
            case "module_load.third_party_not_allowed":
                yield return "Add the third-party module id to the explicit allow-list after reviewing trust and permissions.";
                break;
            case "module_load.required_configuration_missing":
                yield return $"Bind required module configuration: {ExtractConfigurationKey(message)}.";
                break;
            case "module_load.health_not_healthy":
                yield return "Check module health details, required packaged assemblies, credentials and local dependencies.";
                break;
            case "module_load.version_incompatible":
                yield return "Upgrade TianShu or install a module version compatible with the current runtime.";
                break;
            case "module_load.implementation_binding_missing":
                yield return "Add implementation.project and implementation.type to the module descriptor or manifest.";
                break;
        }
    }

    private static async Task<CliProbeResult> ProbeProviderAsync(
        ResolvedTianShuConfig? config,
        bool apiKeyPresent,
        CancellationToken cancellationToken)
    {
        if (config is null)
        {
            return new(false, "skipped", "probe_config_missing", "No config is available for probe.");
        }

        if (!apiKeyPresent || string.IsNullOrWhiteSpace(config.ProviderEnvKey))
        {
            return new(false, "skipped", "probe_api_key_missing", "Probe requires the active provider API key environment variable.");
        }

        if (string.IsNullOrWhiteSpace(config.ProviderBaseUrl))
        {
            return new(false, "skipped", "probe_base_url_missing", "Probe requires provider base_url.");
        }

        var endpoint = ResolveProbeEndpoint(config);
        if (endpoint is null)
        {
            return new(false, "skipped", "probe_unsupported_wire_api", $"Probe is not implemented for wire API `{config.ProviderWireApi}`.");
        }

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            var apiKey = Environment.GetEnvironmentVariable(config.ProviderEnvKey!)!;
            if (string.Equals(config.ProviderWireApi, "anthropic_messages", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new(true, "ok", "probe_ok", $"Endpoint is reachable: {(int)response.StatusCode}");
            }

            return new(false, "failed", "probe_http_failed", $"Endpoint returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new(false, "failed", "probe_request_failed", ex.Message);
        }
    }

    private static string? ResolveProbeEndpoint(ResolvedTianShuConfig config)
    {
        var baseUrl = config.ProviderBaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return config.ProviderWireApi switch
        {
            "openai_responses" or "responses" or "openai_chat_completions" => ResolveVersionedEndpoint(baseUrl, "v1", "models"),
            "anthropic_messages" => ResolveVersionedEndpoint(baseUrl, "v1", "models"),
            _ => null,
        };
    }

    private static string ResolveVersionedEndpoint(string baseUrl, string versionSegment, string terminalPath)
    {
        var version = "/" + versionSegment.Trim('/');
        var terminal = "/" + terminalPath.Trim('/');
        if (baseUrl.EndsWith(terminal, StringComparison.OrdinalIgnoreCase))
        {
            return baseUrl;
        }

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            && uri.AbsolutePath.TrimEnd('/').EndsWith(version, StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseUrl}{terminal}";
        }

        return $"{baseUrl}{version}{terminal}";
    }

    private static void AddFileIssue(List<CliDoctorIssue> issues, string path, string code, string message)
    {
        if (!File.Exists(path))
        {
            issues.Add(new("error", code, $"{message} Missing path: {path}"));
        }
    }

    private static string ResolveModulePath(string configPath, params string[] segments)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Unable to resolve config directory: {configPath}");
        }

        return Path.Combine([directory, "modules", .. segments]);
    }
}

internal sealed record CliInitResult(
    bool Success,
    string ConfigPath,
    string Provider,
    IReadOnlyList<string> WrittenPaths,
    IReadOnlyList<string> SkippedPaths,
    string? ApiKeyEnvironmentVariable,
    IReadOnlyList<string> NextSteps);

internal sealed record CliDoctorResult(
    bool Ready,
    string ConfigPath,
    string TianShuHome,
    bool PortableMode,
    string? PackageRoot,
    string? PackageRuntimeIdentifier,
    string CurrentRuntimeIdentifier,
    bool? RuntimeIdentifierMatches,
    string ModulesRoot,
    string RuntimeRoot,
    string RuntimeWorkspaceRoot,
    bool RuntimeWritable,
    string AppHostPath,
    bool AppHostExists,
    bool ConfigExists,
    string ProviderInstancesPath,
    bool ProviderInstancesExists,
    string RouteSetPath,
    bool RouteSetExists,
    string ProtocolRulesPath,
    bool ProtocolRulesExists,
    string? ModelProvider,
    string? Model,
    string? ProviderWireApi,
    string? ProviderBaseUrl,
    string? ApiKeyEnvironmentVariable,
    bool ApiKeyEnvironmentVariablePresent,
    bool ProbeRequested,
    string? ProbeStatus,
    string? ProbeMessage,
    CliModuleDoctorResult Modules,
    IReadOnlyList<CliDoctorIssue> Issues,
    IReadOnlyList<string> NextSteps);

internal sealed record CliDoctorIssue(string Severity, string Code, string Message);

internal sealed record CliProbeResult(bool Success, string Status, string Code, string Message);

internal sealed record CliModuleDoctorResult(
    int RootCount,
    int DiscoveredCount,
    int SelectedCount,
    int RegisteredCount,
    int RejectedCount,
    int UnavailableCount,
    int SkippedCount,
    int MissingConfigurationCount,
    int GovernanceRiskCount,
    IReadOnlyList<CliModuleCandidateDoctorResult> Candidates,
    IReadOnlyList<CliDoctorIssue> Issues);

internal sealed record CliModuleCandidateDoctorResult(
    string ModuleId,
    string Kind,
    string DisplayName,
    string Version,
    string SourceKind,
    string ManifestPath,
    string DiscoveryStatus,
    string? DiscoveryReason,
    string? LoadStatus,
    string? HealthStatus,
    IReadOnlyList<string> MissingConfigurationKeys,
    IReadOnlyList<string> GovernanceRisks,
    IReadOnlyList<CliModuleDiagnostic> Diagnostics,
    IReadOnlyList<string> RepairSuggestions);

internal sealed record CliModuleDiagnostic(string Severity, string Code, string Message);
