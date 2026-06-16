using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;

namespace TianShu.Provider.Abstractions;

public static class ProviderModuleDescriptorFactory
{
    public static ProviderDescriptor Create(
        string providerId,
        string displayName,
        ProviderProtocolKind protocolKind,
        string defaultBaseUrl,
        string? apiKeyEnvironmentVariable,
        ProviderCapabilityProfile capabilities,
        IReadOnlyList<TianShu.Contracts.Provider.ProviderModelDescriptor>? models = null,
        string? wireApi = null)
        => new(
            providerId,
            displayName,
            protocolKind,
            capabilities,
            models,
            new ProviderEndpointDescriptor(providerId, protocolKind, defaultBaseUrl, apiKeyEnvironmentVariable),
            new PermissionEnvelope(
                scopes: [$"provider.{Normalize(providerId)}.invoke"],
                requiresHumanGate: false,
                reason: "Provider calls must originate from Execution Runtime ProviderInvocationRequest."),
            new SideEffectProfile(
                SideEffectLevel.ExternalNetwork,
                affectedResources: [$"provider:{Normalize(providerId)}", "network"],
                reversible: false,
                requiresAudit: true),
            new MetadataBag(new Dictionary<string, StructuredValue>
            {
                ["moduleKind"] = StructuredValue.FromString("provider"),
                ["wireApi"] = StructuredValue.FromString(wireApi ?? protocolKind.ToString()),
            }));

    public static ModuleDescriptor CreateModuleDescriptor(
        ProviderDescriptor descriptor,
        ModuleTrustLevel trustLevel = ModuleTrustLevel.BuiltIn,
        ModuleHealthProbe? health = null,
        ModuleImplementationBinding? implementationBinding = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var capability = new ModuleCapabilityDescriptor(
            $"provider.{Normalize(descriptor.ProviderId)}.invoke",
            $"{descriptor.DisplayName} invocation",
            inputSchema: new ModuleSchemaRef($"provider.{Normalize(descriptor.ProviderId)}.invocation.input"),
            outputSchema: new ModuleSchemaRef($"provider.{Normalize(descriptor.ProviderId)}.stream.output"),
            permission: descriptor.Permission,
            sideEffects: descriptor.SideEffects,
            metadata: descriptor.Metadata);

        return new ModuleDescriptor(
            descriptor.ProviderId,
            ModuleKind.Provider,
            descriptor.DisplayName,
            version: "1.0",
            capabilities: [capability],
            configurationSchema: new ModuleSchemaRef($"provider.{Normalize(descriptor.ProviderId)}.configuration"),
            permission: descriptor.Permission,
            sideEffects: descriptor.SideEffects,
            audit: new ModuleAuditProfile(
                required: descriptor.SideEffects.RequiresAudit,
                eventKinds: [$"provider.{Normalize(descriptor.ProviderId)}.invoked"],
                redactSensitiveValues: true),
            trustLevel: trustLevel,
            health: health,
            implementationBinding: implementationBinding,
            metadata: descriptor.Metadata);
    }

    private static string Normalize(string value)
    {
        var chars = value.Select(static character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '.').ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("..", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
        }

        return normalized.Trim('.');
    }
}
