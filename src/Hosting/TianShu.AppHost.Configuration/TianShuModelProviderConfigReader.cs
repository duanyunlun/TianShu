namespace TianShu.AppHost.Configuration;

/// <summary>
/// 模型 provider 配置公开读取入口，仅暴露 presentation 层需要的只读查询。
/// Public read-only model-provider config entry point for presentation consumers.
/// </summary>
public static class TianShuModelProviderConfigReader
{
    public static string? ReadProviderBaseUrl(
        IReadOnlyDictionary<string, string> config,
        string? modelProvider)
        => KernelModelProviderConfigUtilities.ReadConfiguredModelProviderSetting(
            config,
            modelProvider,
            "base_url");

    public static string? ReadProviderBaseUrl(
        Dictionary<string, object?> config,
        string? modelProvider)
        => KernelModelProviderConfigUtilities.ReadConfiguredModelProviderSetting(
            config,
            modelProvider,
            "base_url");

    public static string? ReadProviderApiKeyEnvironmentVariable(
        IReadOnlyDictionary<string, string> config,
        string? modelProvider)
        => KernelModelProviderConfigUtilities.ReadConfiguredModelProviderSetting(
            config,
            modelProvider,
            "api_key_env");

    public static string? ReadProviderApiKeyEnvironmentVariable(
        Dictionary<string, object?> config,
        string? modelProvider)
        => KernelModelProviderConfigUtilities.ReadConfiguredModelProviderSetting(
            config,
            modelProvider,
            "api_key_env");
}
