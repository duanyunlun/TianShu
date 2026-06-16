using TianShu.Provider.Anthropic;
using TianShu.Provider.Abstractions;
using TianShu.Provider.Google;
using TianShu.Provider.OpenAI;
using TianShu.Provider.OpenAICompatible;

namespace TianShu.Cli;

/// <summary>
/// CLI 发布形态下的随包 provider bootstrap 注册器。
/// Packaged provider bootstrap registrar for CLI publishing layouts.
/// </summary>
internal static class CliProviderAssemblyPreloader
{
    /// <summary>
    /// 注册随 CLI 分发的 provider bootstrap；用户级插件包生命周期由 AppHost 承载。
    /// Registers provider bootstraps distributed with the CLI; user-level plugin package lifecycle is owned by AppHost.
    /// </summary>
    public static void TryLoadPackagedProviders()
    {
        RegisterPackagedProviderBootstraps();
    }

    private static void RegisterPackagedProviderBootstraps()
    {
        var openAiBootstrap = new OpenAiProviderBootstrap();
        ProviderRuntimeBootstrapRegistry.Register(openAiBootstrap);
        ProviderResponsesComponentBootstraps.Register(openAiBootstrap);
        ProviderResponsesComponentBootstraps.Register(new OpenAiChatCompletionsProviderBootstrap());
        ProviderResponsesComponentBootstraps.Register(new AnthropicMessagesProviderBootstrap());
        ProviderResponsesComponentBootstraps.Register(new GoogleGenerativeProviderBootstrap());
    }
}
