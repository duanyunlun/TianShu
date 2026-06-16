using System.Net.Http.Headers;
using System.Text.Json;
using TianShu.Configuration;
using TianShu.RuntimeComposition;

namespace TianShu.Cli;

internal static class CliOnboardingCommandRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

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
        Console.WriteLine($"Config: {result.ConfigPath} ({(result.ConfigExists ? "found" : "missing")})");
        Console.WriteLine($"Provider: {result.ModelProvider ?? "<missing>"}");
        Console.WriteLine($"Model: {result.Model ?? "<missing>"}");
        Console.WriteLine($"Wire API: {result.ProviderWireApi ?? "<missing>"}");
        Console.WriteLine($"Base URL: {result.ProviderBaseUrl ?? "<missing>"}");
        Console.WriteLine($"API key env: {result.ApiKeyEnvironmentVariable ?? "<missing>"} ({(result.ApiKeyEnvironmentVariablePresent ? "set" : "missing")})");
        Console.WriteLine($"Probe: {(result.ProbeRequested ? result.ProbeStatus ?? "not-run" : "not requested")}");

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
        var issues = new List<CliDoctorIssue>();
        var configExists = File.Exists(configPath);
        var providerInstancesPath = ResolveModulePath(configPath, "model", "provider-instances", "default.toml");
        var routeSetPath = ResolveModulePath(configPath, "model", "route-sets", "default.toml");
        var protocolRulesPath = ResolveModulePath(configPath, "model", "protocol-rules", "default.toml");

        AddFileIssue(issues, configPath, "config_missing", "Run `tianshu init` to create the default configuration.");
        AddFileIssue(issues, providerInstancesPath, "provider_instances_missing", "Run `tianshu init` to create provider templates.");
        AddFileIssue(issues, routeSetPath, "route_set_missing", "Run `tianshu init` to create model route templates.");
        AddFileIssue(issues, protocolRulesPath, "protocol_rules_missing", "Run `tianshu init` to create protocol rule templates.");

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
            Issues: issues,
            NextSteps: BuildNextSteps(config));
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
    IReadOnlyList<CliDoctorIssue> Issues,
    IReadOnlyList<string> NextSteps);

internal sealed record CliDoctorIssue(string Severity, string Code, string Message);

internal sealed record CliProbeResult(bool Success, string Status, string Code, string Message);
