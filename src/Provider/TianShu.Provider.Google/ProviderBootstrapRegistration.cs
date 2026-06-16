using TianShu.Provider.Abstractions;
using TianShu.Provider.Google;

// 将 Google Generative provider adapter 显式注册到共享 bootstrap loader。
// Explicitly registers the Google Generative provider adapter into the shared bootstrap loader.
[assembly: ProviderBootstrapRegistration(typeof(GoogleGenerativeProviderBootstrap))]
