using TianShu.RuntimeComposition;

namespace TianShu.Cli;

internal static class CliFirstRunBootstrapper
{
    internal const string ProviderOpenAi = "openai";
    internal const string ProviderAnthropic = "anthropic";
    internal const string ProviderOpenAiCompatible = "openai-compatible";

    private const string GeneratedMarker = "TianShu CLI generated first-run template";

    public static CliBootstrapResult EnsureDefaultConfiguration(
        CliRuntimeCommandOptions options,
        string? requestedProvider = null,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        return EnsureDefaultConfiguration(options.ConfigFilePath, requestedProvider, force);
    }

    public static CliBootstrapResult EnsureDefaultConfiguration(
        string? configFilePath,
        string? requestedProvider = null,
        bool force = false)
    {
        var configPath = ResolveConfigPath(configFilePath);
        var provider = ResolveProviderSelection(requestedProvider);
        var written = new List<string>();
        var skipped = new List<string>();

        EnsureFile(
            configPath,
            BuildMainConfig(),
            force,
            written,
            skipped);

        var providerInstancesPath = ResolveModulePath(configPath, "model", "provider-instances", "default.toml");
        var routeSetPath = ResolveModulePath(configPath, "model", "route-sets", "default.toml");
        var protocolRulesPath = ResolveModulePath(configPath, "model", "protocol-rules", "default.toml");

        EnsureFile(
            providerInstancesPath,
            BuildProviderInstancesConfig(),
            force,
            written,
            skipped);
        EnsureFile(
            routeSetPath,
            BuildRouteSetConfig(provider),
            force,
            written,
            skipped);
        EnsureFile(
            protocolRulesPath,
            BuildProtocolRulesConfig(),
            force,
            written,
            skipped);

        return new CliBootstrapResult(
            ConfigPath: configPath,
            Provider: provider.Id,
            WrittenPaths: written,
            SkippedPaths: skipped);
    }

    public static bool IsSupportedProvider(string? value)
        => TryNormalizeProvider(value, out _);

    public static IReadOnlyList<CliProviderTemplate> ProviderTemplates { get; } =
    [
        new(
            Id: ProviderOpenAi,
            DisplayName: "OpenAI Responses",
            Model: "gpt-5.5",
            RouteProtocol: "openai_responses",
            WireApi: "openai_responses",
            BaseUrl: "https://api.openai.com",
            ApiKeyEnvironmentVariable: "OPENAI_API_KEY"),
        new(
            Id: ProviderAnthropic,
            DisplayName: "Anthropic Messages",
            Model: "claude-opus-4.8",
            RouteProtocol: "anthropic_messages",
            WireApi: "anthropic_messages",
            BaseUrl: "https://api.anthropic.com",
            ApiKeyEnvironmentVariable: "ANTHROPIC_API_KEY"),
        new(
            Id: ProviderOpenAiCompatible,
            DisplayName: "OpenAI-compatible Chat Completions",
            Model: "openai-compatible-default",
            RouteProtocol: "openai_chat_completions",
            WireApi: "openai_chat_completions",
            BaseUrl: "https://api.openai.com",
            ApiKeyEnvironmentVariable: "OPENAI_COMPATIBLE_API_KEY"),
    ];

    private static string ResolveConfigPath(string? configuredPath)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
            ? RuntimeConfigurationComposition.ResolveDefaultPath()
            : configuredPath!);

    private static string ResolveModulePath(string configPath, params string[] segments)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"无法解析配置目录：{configPath}");
        }

        return Path.Combine([directory, "modules", .. segments]);
    }

    private static CliProviderTemplate ResolveProviderSelection(string? requestedProvider)
    {
        if (TryNormalizeProvider(requestedProvider, out var explicitProvider))
        {
            return GetTemplate(explicitProvider!);
        }

        var detected = ProviderTemplates
            .Where(static template => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(template.ApiKeyEnvironmentVariable)))
            .ToArray();
        return detected.Length == 1 ? detected[0] : GetTemplate(ProviderOpenAi);
    }

    private static CliProviderTemplate GetTemplate(string provider)
        => ProviderTemplates.First(template => string.Equals(template.Id, provider, StringComparison.OrdinalIgnoreCase));

    private static bool TryNormalizeProvider(string? value, out string? provider)
    {
        provider = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        provider = normalized switch
        {
            ProviderOpenAi or "openai-responses" or "responses" => ProviderOpenAi,
            ProviderAnthropic or "claude" or "anthropic-messages" => ProviderAnthropic,
            ProviderOpenAiCompatible or "compatible" or "openai_chat_completions" or "chat-completions" => ProviderOpenAiCompatible,
            _ => null,
        };
        return provider is not null;
    }

    private static void EnsureFile(
        string path,
        string content,
        bool force,
        List<string> written,
        List<string> skipped)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && !force && !IsGeneratedTemplate(path))
        {
            skipped.Add(path);
            return;
        }

        if (File.Exists(path) && !force)
        {
            skipped.Add(path);
            return;
        }

        File.WriteAllText(path, content);
        written.Add(path);
    }

    private static bool IsGeneratedTemplate(string path)
    {
        try
        {
            return File.ReadLines(path).Take(3).Any(line => line.Contains(GeneratedMarker, StringComparison.Ordinal));
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string BuildMainConfig()
        => $"""
           # {GeneratedMarker}
           # This file stores TianShu defaults and environment variable names only. Do not put secrets here.
           profile = "default"
           model_route_set = "default"
           model_protocol_rule_set = "default"
           provider_instances = "default"
           approval_policy = "never"
           sandbox_mode = "workspace-write"

           [profiles.default]
           model_route_set = "default"
           """;

    private static string BuildProviderInstancesConfig()
        => $"""
           # {GeneratedMarker}
           # Provider templates are public defaults. Replace base_url/model as needed; keep API keys in environment variables.

           [providers.openai]
           base_url = "https://api.openai.com"
           api_key_env = "OPENAI_API_KEY"
           default_protocol = "openai_responses"
           protocol_fallbacks = ["openai_responses"]
           request_max_retries = 1
           stream_max_retries = 1
           stream_idle_timeout_ms = 30000
           websocket_connect_timeout_ms = 15000
           supports_websockets = true

           [providers.anthropic]
           base_url = "https://api.anthropic.com"
           api_key_env = "ANTHROPIC_API_KEY"
           default_protocol = "anthropic_messages"
           protocol_fallbacks = ["anthropic_messages"]
           request_max_retries = 1
           stream_max_retries = 1
           stream_idle_timeout_ms = 30000
           websocket_connect_timeout_ms = 15000
           supports_websockets = false

           [providers.openai-compatible]
           base_url = "https://api.openai.com"
           api_key_env = "OPENAI_COMPATIBLE_API_KEY"
           default_protocol = "openai_chat_completions"
           protocol_fallbacks = ["openai_chat_completions"]
           request_max_retries = 1
           stream_max_retries = 1
           stream_idle_timeout_ms = 30000
           websocket_connect_timeout_ms = 15000
           supports_websockets = false
           """;

    private static string BuildRouteSetConfig(CliProviderTemplate provider)
        => $$"""
           # {{GeneratedMarker}}
           [model_route_sets.default]
           display_name = "TianShu first-run default route set"
           description = "Public first-run route set. Change provider/model after running tianshu init if needed."
           routes = [
             { kind = "default", candidates = [{ provider = "{{provider.Id}}", model = "{{provider.Model}}", protocol = "{{provider.RouteProtocol}}" }] }
           ]
           """;

    private static string BuildProtocolRulesConfig()
        => $$"""
           # {{GeneratedMarker}}
           [model_protocol_rule_sets.default]
           display_name = "TianShu first-run protocol rules"
           rules = [
             { match = "gpt-*", protocols = ["openai_responses"] },
             { match = "claude-*", protocols = ["anthropic_messages"] },
             { match = "*", protocols = ["openai_chat_completions"] }
           ]
           """;
}

internal sealed record CliBootstrapResult(
    string ConfigPath,
    string Provider,
    IReadOnlyList<string> WrittenPaths,
    IReadOnlyList<string> SkippedPaths);

internal sealed record CliProviderTemplate(
    string Id,
    string DisplayName,
    string Model,
    string RouteProtocol,
    string WireApi,
    string BaseUrl,
    string ApiKeyEnvironmentVariable);
