using TianShu.Configuration;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Catalog;

/// <summary>
/// Provider 本地 smoke test 的安全执行计划。
/// Safe execution plan for local provider smoke tests.
/// </summary>
public sealed class ProviderSmokeTestPlan
{
    private const string DefaultModelProvider = "openai";
    private const string DefaultBaseUrl = "https://api.openai.com/v1";
    private const string DefaultWireApi = "responses";
    private const string DefaultApiKeyEnvironmentVariable = "OPENAI_API_KEY";

    private ProviderSmokeTestPlan(
        string configFilePath,
        string? model,
        string modelProvider,
        string providerBaseUrl,
        string providerApiKeyEnvironmentVariable,
        string providerWireApi,
        string protocolAdapter,
        bool bootstrapResolved,
        bool credentialValueAvailable,
        string? failureReason)
    {
        ConfigFilePath = configFilePath;
        Model = model;
        ModelProvider = modelProvider;
        ProviderBaseUrl = providerBaseUrl;
        ProviderApiKeyEnvironmentVariable = providerApiKeyEnvironmentVariable;
        ProviderWireApi = providerWireApi;
        ProtocolAdapter = protocolAdapter;
        BootstrapResolved = bootstrapResolved;
        CredentialValueAvailable = credentialValueAvailable;
        FailureReason = failureReason;
    }

    public string ConfigFilePath { get; }

    public string? Model { get; }

    public string ModelProvider { get; }

    public string ProviderBaseUrl { get; }

    public string ProviderApiKeyEnvironmentVariable { get; }

    public string ProviderWireApi { get; }

    public string ProtocolAdapter { get; }

    public bool BootstrapResolved { get; }

    public bool CredentialValueAvailable { get; }

    public bool Ready => BootstrapResolved && CredentialValueAvailable;

    public string? FailureReason { get; }

    /// <summary>
    /// 从已解析的 TianShu 配置创建 provider smoke test 计划。
    /// Creates a provider smoke-test plan from a resolved TianShu configuration.
    /// </summary>
    public static ProviderSmokeTestPlan Create(
        ResolvedTianShuConfig config,
        Func<string, string?>? readEnvironmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        readEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var modelProvider = Normalize(config.ModelProvider) ?? DefaultModelProvider;
        var baseUrl = Normalize(config.ProviderBaseUrl) ?? DefaultBaseUrl;
        var envKey = Normalize(config.ProviderEnvKey) ?? DefaultApiKeyEnvironmentVariable;
        var wireApi = Normalize(config.ProviderWireApi) ?? DefaultWireApi;
        var protocolAdapter = Normalize(config.ProtocolAdapter);

        var bootstrapResolved = false;
        string? failureReason = null;
        try
        {
            protocolAdapter = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(protocolAdapter).Bootstrap.ProtocolAdapterId;
            bootstrapResolved = true;
        }
        catch (InvalidOperationException exception)
        {
            failureReason = exception.Message;
        }

        var credentialValueAvailable = !string.IsNullOrWhiteSpace(readEnvironmentVariable(envKey));
        if (failureReason is null && !credentialValueAvailable)
        {
            failureReason = $"环境变量 `{envKey}` 未设置或为空。";
        }

        return new ProviderSmokeTestPlan(
            Normalize(config.ConfigFilePath) ?? string.Empty,
            Normalize(config.Model),
            modelProvider,
            baseUrl,
            envKey,
            wireApi,
            protocolAdapter ?? string.Empty,
            bootstrapResolved,
            credentialValueAvailable,
            failureReason);
    }

    /// <summary>
    /// 生成不包含 secret 值的诊断摘要。
    /// Builds a diagnostics summary that never includes secret values.
    /// </summary>
    public IReadOnlyList<string> ToRedactedDiagnostics()
        =>
        [
            $"config={ConfigFilePath}",
            $"provider={ModelProvider}",
            $"model={Model ?? string.Empty}",
            $"base_url={ProviderBaseUrl}",
            $"default_protocol={ProviderWireApi}",
            $"api_key_env={ProviderApiKeyEnvironmentVariable}",
            $"protocol_adapter={ProtocolAdapter}",
            $"bootstrap_resolved={BootstrapResolved}",
            $"credential_value_available={CredentialValueAvailable}",
            $"ready={Ready}",
        ];

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
