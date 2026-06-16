using TianShu.Contracts.Provider;
using TianShu.Contracts.Modules;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Google;

public static class GoogleProviderModuleDescriptor
{
    public static ProviderDescriptor Descriptor { get; } = ProviderModuleDescriptorFactory.Create(
        "google",
        "Google",
        ProviderProtocolKind.GoogleGenerative,
        "https://generativelanguage.googleapis.com",
        "GOOGLE_API_KEY",
        new ProviderCapabilityProfile(
            SupportsStreaming: true,
            SupportsTools: true,
            SupportsReasoning: true,
            SupportsJsonSchema: true),
        [new TianShu.Contracts.Provider.ProviderModelDescriptor("google-generative-default", family: "google")],
        ProviderWireApi.GoogleGenerative);

    public static ModuleDescriptor ModuleDescriptor { get; } = ProviderModuleDescriptorFactory.CreateModuleDescriptor(
        Descriptor,
        implementationBinding: new ModuleImplementationBinding("TianShu.Provider.Google", nameof(GoogleProviderModuleDescriptor)));
}
