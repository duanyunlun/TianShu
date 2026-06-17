using System.Reflection;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Provider;
using TianShu.Provider.Abstractions;
using TianShu.Provider.Anthropic;
using TianShu.Provider.Google;
using TianShu.Provider.OpenAI;
using TianShu.Provider.OpenAICompatible;

namespace TianShu.Provider.OpenAI.Tests;

public sealed class ProviderModuleDescriptorTests
{
    [Fact]
    public void BuiltInProviderModules_ShouldExposeGovernedDescriptors()
    {
        var descriptors = new[]
        {
            OpenAiProviderModuleDescriptor.Descriptor,
            OpenAiCompatibleProviderModuleDescriptor.Descriptor,
            AnthropicProviderModuleDescriptor.Descriptor,
            GoogleProviderModuleDescriptor.Descriptor,
        };

        Assert.All(descriptors, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.ProviderId));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.DisplayName));
            Assert.NotNull(descriptor.Endpoint);
            Assert.NotEmpty(descriptor.Models);
            Assert.NotEmpty(descriptor.Permission.Scopes);
            Assert.False(descriptor.Permission.RequiresHumanGate);
            Assert.Equal(SideEffectLevel.ExternalNetwork, descriptor.SideEffects.Level);
            Assert.Contains("network", descriptor.SideEffects.AffectedResources);
            Assert.True(descriptor.SideEffects.RequiresAudit);
            Assert.True(descriptor.Metadata.TryGetValue("moduleKind", out var moduleKind));
            Assert.Equal("provider", moduleKind.GetString());
        });
    }

    [Fact]
    public void ProviderModuleDescriptorFactory_ShouldCreateValidatedAccessManifest()
    {
        var manifest = ProviderModuleDescriptorFactory.CreateAccessManifest(
            OpenAiProviderModuleDescriptor.Descriptor,
            "openai_responses");

        var result = ProviderModuleAccessValidator.Validate(
            manifest,
            OpenAiProviderModuleDescriptor.Descriptor,
            "default");

        Assert.True(result.IsValid);
        Assert.NotNull(result.Access);
        Assert.Equal("openai", result.Access!.Manifest.ProviderId);
        Assert.Equal("openai_responses", result.Access.ProtocolBinding.WireApi);
        Assert.NotEmpty(result.Access.ModelRouteSet.Candidates);
        Assert.Contains(result.Access.ErrorSpecs, static spec => spec.Code == "rate_limited");
    }

    [Fact]
    public void ProviderModule_InvokeAsync_ShouldOnlyAcceptProviderInvocationRequest()
    {
        var method = typeof(IProviderModule).GetMethod(nameof(IProviderModule.InvokeAsync));

        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(ProviderInvocationRequest), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.DoesNotContain(parameters, static parameter => parameter.ParameterType == typeof(ProviderInvocationContext));
    }

    [Fact]
    public void ProviderProjects_ShouldNotReferenceKernelControlPlaneOrExecutionRuntimeImplementations()
    {
        var repoRoot = FindRepoRoot();
        var projectFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Provider"), "*.csproj", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(projectFiles);
        foreach (var projectFile in projectFiles)
        {
            var source = File.ReadAllText(projectFile);
            Assert.DoesNotContain("TianShu.Kernel.csproj", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.Kernel.Adaptive.csproj", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.ControlPlane.csproj", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.Execution.Runtime.csproj", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProviderModulePublicSurface_ShouldNotExposeKernelPlanningTypes()
    {
        var exportedTypes = typeof(IProviderModule).Assembly.GetExportedTypes()
            .Where(static type => type.Namespace?.StartsWith("TianShu.Provider.Abstractions", StringComparison.Ordinal) == true)
            .ToArray();

        foreach (var type in exportedTypes)
        {
            foreach (var memberType in EnumeratePublicMemberTypes(type))
            {
                Assert.DoesNotContain("StageGraph", memberType.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("ModelRoutePolicy", memberType.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("GovernanceEnvelope", memberType.Name, StringComparison.Ordinal);
            }
        }
    }

    private static IEnumerable<Type> EnumeratePublicMemberTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return Unwrap(parameter.ParameterType);
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            yield return Unwrap(property.PropertyType);
        }

        foreach (var method in type.GetMethods(flags))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            yield return Unwrap(method.ReturnType);
            foreach (var parameter in method.GetParameters())
            {
                yield return Unwrap(parameter.ParameterType);
            }
        }
    }

    private static Type Unwrap(Type type)
    {
        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Select(Unwrap).FirstOrDefault(static nested => nested.Name.Contains("StageGraph", StringComparison.Ordinal)) ?? type.GetGenericTypeDefinition();
        }

        if (type.HasElementType)
        {
            return Unwrap(type.GetElementType()!);
        }

        return type;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TianShu.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
