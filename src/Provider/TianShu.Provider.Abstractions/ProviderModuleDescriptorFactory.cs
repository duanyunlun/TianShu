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
        var normalizedProviderId = Normalize(descriptor.ProviderId);
        var configurationSchema = new ModuleSchemaRef($"provider.{normalizedProviderId}.configuration");
        var requiredConfiguration = new List<ModuleConfigurationRequirement>
        {
            new(
                $"provider.{normalizedProviderId}.endpoint",
                $"{descriptor.DisplayName} endpoint",
                valueSchema: configurationSchema,
                required: true,
                secret: false),
        };
        if (!string.IsNullOrWhiteSpace(descriptor.Endpoint?.ApiKeyEnvironmentVariable))
        {
            requiredConfiguration.Add(new ModuleConfigurationRequirement(
                $"provider.{normalizedProviderId}.apiKeyEnvironmentVariable",
                $"{descriptor.DisplayName} API key environment variable",
                required: true,
                secret: true,
                description: "Stores the environment variable name, not the secret value."));
        }

        return new ModuleDescriptor(
            descriptor.ProviderId,
            ModuleKind.Provider,
            descriptor.DisplayName,
            version: "1.0",
            capabilities: [capability],
            configurationSchema: configurationSchema,
            permission: descriptor.Permission,
            sideEffects: descriptor.SideEffects,
            audit: new ModuleAuditProfile(
                required: descriptor.SideEffects.RequiresAudit,
                eventKinds: [$"provider.{normalizedProviderId}.invoked"],
                redactSensitiveValues: true),
            trustLevel: trustLevel,
            requiredConfiguration: requiredConfiguration,
            runtimeDependencies:
            [
                new ModuleRuntimeDependency(
                    descriptor.ProviderId,
                    $"{descriptor.DisplayName} provider binding",
                    ModuleRuntimeDependencyKind.DotNetAssembly,
                    required: true)
            ],
            minimumTianShuVersion: "0.6.0",
            health: health,
            implementationBinding: implementationBinding,
            metadata: descriptor.Metadata);
    }

    public static ProviderModuleManifest CreateAccessManifest(
        ProviderDescriptor descriptor,
        string wireApi,
        string minimumTianShuVersion = "0.6.0",
        string routeSetId = "default",
        IReadOnlyList<ProviderErrorSpec>? errorSpecs = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(wireApi);
        ArgumentException.ThrowIfNullOrWhiteSpace(minimumTianShuVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeSetId);
        if (descriptor.Endpoint is null)
        {
            throw new InvalidOperationException("ProviderDescriptor 缺少 endpoint，不能生成公开 Provider manifest。");
        }

        if (descriptor.Models.Count == 0)
        {
            throw new InvalidOperationException("ProviderDescriptor 缺少模型列表，不能生成 model route set。");
        }

        var candidates = descriptor.Models
            .Select(model => new ProviderModelRouteCandidate(descriptor.ProviderId, model.Name, wireApi))
            .ToArray();

        return new ProviderModuleManifest(
            descriptor.ProviderId,
            descriptor.DisplayName,
            version: "1.0",
            minimumTianShuVersion,
            protocolBindings:
            [
                new ProviderProtocolBinding(
                    wireApi,
                    descriptor.ProtocolKind,
                    descriptor.Capabilities,
                    enabled: true),
            ],
            modelRouteSets:
            [
                new ProviderModelRouteSet(routeSetId, candidates, defaultModel: candidates[0].Model),
            ],
            endpoint: descriptor.Endpoint,
            errorSpecs: errorSpecs ?? CreateDefaultErrorSpecs(),
            diagnostics: [$"provider.{Normalize(descriptor.ProviderId)}.access"]);
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

    private static IReadOnlyList<ProviderErrorSpec> CreateDefaultErrorSpecs()
        =>
        [
            new("authentication_failed", ProviderErrorKind.Authentication, retryable: false, safeForUser: true, remediation: "Check the provider API key environment variable."),
            new("rate_limited", ProviderErrorKind.RateLimited, retryable: true, safeForUser: true, remediation: "Retry with backoff or choose another authorized route."),
            new("provider_unavailable", ProviderErrorKind.ProviderUnavailable, retryable: true, safeForUser: true, remediation: "Retry later or choose another authorized provider route."),
            new("protocol_violation", ProviderErrorKind.ProtocolViolation, retryable: false, safeForUser: false, remediation: "Inspect provider diagnostics and wire-api binding."),
        ];
}
