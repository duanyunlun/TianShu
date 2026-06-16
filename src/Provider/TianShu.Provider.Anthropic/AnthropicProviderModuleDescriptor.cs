using TianShu.Contracts.Provider;
using TianShu.Contracts.Modules;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Anthropic;

public static class AnthropicProviderModuleDescriptor
{
    public static ProviderDescriptor Descriptor { get; } = ProviderModuleDescriptorFactory.Create(
        "anthropic",
        "Anthropic",
        ProviderProtocolKind.AnthropicMessages,
        "https://api.anthropic.com",
        "ANTHROPIC_API_KEY",
        new ProviderCapabilityProfile(
            SupportsStreaming: true,
            SupportsTools: true,
            SupportsReasoning: true),
        [new TianShu.Contracts.Provider.ProviderModelDescriptor("anthropic-default", family: "anthropic")],
        ProviderWireApi.AnthropicMessages);

    public static ModuleDescriptor ModuleDescriptor { get; } = ProviderModuleDescriptorFactory.CreateModuleDescriptor(
        Descriptor,
        implementationBinding: new ModuleImplementationBinding("TianShu.Provider.Anthropic", nameof(AnthropicProviderModuleDescriptor)));
}
