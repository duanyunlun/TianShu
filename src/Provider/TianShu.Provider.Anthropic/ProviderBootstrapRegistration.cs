using TianShu.Provider.Abstractions;
using TianShu.Provider.Anthropic;

// 将 Anthropic Messages provider adapter 显式注册到共享 bootstrap loader。
// Explicitly registers the Anthropic Messages provider adapter into the shared bootstrap loader.
[assembly: ProviderBootstrapRegistration(typeof(AnthropicMessagesProviderBootstrap))]
