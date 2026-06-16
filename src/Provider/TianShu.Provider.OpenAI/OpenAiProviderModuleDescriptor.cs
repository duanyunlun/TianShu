using TianShu.Contracts.Provider;
using TianShu.Contracts.Modules;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

public static class OpenAiProviderModuleDescriptor
{
    public static ProviderDescriptor Descriptor { get; } = ProviderModuleDescriptorFactory.Create(
        "openai",
        "OpenAI",
        ProviderProtocolKind.OpenAiResponses,
        "https://api.openai.com",
        "OPENAI_API_KEY",
        new ProviderCapabilityProfile(
            SupportsStreaming: true,
            SupportsTools: true,
            SupportsReasoning: true,
            SupportsJsonSchema: true,
            SupportsWebSockets: true),
        [new TianShu.Contracts.Provider.ProviderModelDescriptor("gpt-5", family: "openai")],
        ProviderWireApi.OpenAiResponses);

    public static ModuleDescriptor ModuleDescriptor { get; } = ProviderModuleDescriptorFactory.CreateModuleDescriptor(
        Descriptor,
        implementationBinding: new ModuleImplementationBinding("TianShu.Provider.OpenAI", nameof(OpenAiProviderModuleDescriptor)));
}
