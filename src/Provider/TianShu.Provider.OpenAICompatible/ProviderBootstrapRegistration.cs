using TianShu.Provider.Abstractions;
using TianShu.Provider.OpenAICompatible;

// 将 OpenAI-compatible Chat Completions adapter 显式注册到共享 bootstrap loader。
// Explicitly registers the OpenAI-compatible Chat Completions adapter into the shared bootstrap loader.
[assembly: ProviderBootstrapRegistration(typeof(OpenAiChatCompletionsProviderBootstrap))]
