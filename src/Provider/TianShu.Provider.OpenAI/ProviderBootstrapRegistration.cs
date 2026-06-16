using TianShu.Provider.Abstractions;
using TianShu.Provider.OpenAI;

// 将 OpenAI provider 的 southbound bootstrap 显式注册到共享 bootstrap loader。
// Explicitly registers the OpenAI provider southbound bootstrap into the shared bootstrap loader.
[assembly: ProviderBootstrapRegistration(typeof(OpenAiProviderBootstrap))]
