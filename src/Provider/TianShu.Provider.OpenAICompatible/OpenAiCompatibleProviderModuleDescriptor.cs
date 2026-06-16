using TianShu.Contracts.Provider;
using TianShu.Contracts.Modules;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAICompatible;

public static class OpenAiCompatibleProviderModuleDescriptor
{
    public static ProviderDescriptor Descriptor { get; } = ProviderModuleDescriptorFactory.Create(
        "openai-compatible",
        "OpenAI Compatible",
        ProviderProtocolKind.OpenAiChatCompletions,
        "https://api.openai.com",
        "OPENAI_COMPATIBLE_API_KEY",
        new ProviderCapabilityProfile(
            SupportsStreaming: true,
            SupportsTools: true,
            SupportsReasoning: true,
            SupportsJsonSchema: true),
        [new TianShu.Contracts.Provider.ProviderModelDescriptor("openai-compatible-default", family: "openai-compatible")],
        ProviderWireApi.OpenAiChatCompletions);

    public static ModuleDescriptor ModuleDescriptor { get; } = ProviderModuleDescriptorFactory.CreateModuleDescriptor(
        Descriptor,
        implementationBinding: new ModuleImplementationBinding("TianShu.Provider.OpenAICompatible", nameof(OpenAiCompatibleProviderModuleDescriptor)));
}
