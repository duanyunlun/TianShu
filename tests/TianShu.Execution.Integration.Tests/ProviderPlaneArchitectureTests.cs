using System.IO;
using System.Reflection;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Integration.Tests;

public sealed class ProviderPlaneArchitectureTests
{
    [Fact]
    public void ProviderModelCatalogs_CoreSource_ShouldDelegateDefaultAdapterAndRuntimeStateCreationToRegistry()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "ProviderModelCatalogs.cs"));

        Assert.Contains("ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId()", source, StringComparison.Ordinal);
        Assert.Contains("ProviderRuntimeBootstrapRegistry.CreateRuntimeState(adapterId).Bootstrap.CreateModelCatalog()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderRuntimeBootstrapRegistry.DefaultProtocolAdapterId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderRuntimeBootstrapRegistry.Resolve(adapterId).CreateModelCatalog()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderAbstractions_PublicSurface_ShouldNotExposeHostControlPlaneOrProviderSpecificTypes()
    {
        var forbiddenNamespacePrefixes = new[]
        {
            "TianShu.Contracts.Host",
            "TianShu.Contracts.Governance",
            "TianShu.Contracts.Interactions",
            "TianShu.ControlPlane",
            "TianShu.Provider.OpenAI",
            "OpenWorkbench.",
        };

        var exportedTypes = typeof(IProviderRuntimeBootstrap).Assembly
            .GetExportedTypes()
            .Where(static type => string.Equals(type.Namespace, "TianShu.Provider.Abstractions", StringComparison.Ordinal))
            .ToArray();

        var offenders = exportedTypes
            .SelectMany(GetPublicSignatureTypes)
            .SelectMany(ExpandType)
            .Where(static type => type.Namespace is not null)
            .Where(type => forbiddenNamespacePrefixes.Any(prefix => type.Namespace!.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(static type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Provider.Abstractions public surface 不应暴露 host/control-plane/provider-specific 类型；当前违规类型：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void ProviderAbstractions_PublicSurface_ShouldNotPromoteProviderImplementationOrOpenWorkbenchNaming()
    {
        var exportedTypes = typeof(IProviderRuntimeBootstrap).Assembly
            .GetExportedTypes()
            .Where(static type => string.Equals(type.Namespace, "TianShu.Provider.Abstractions", StringComparison.Ordinal))
            .Select(static type => type.FullName ?? type.Name)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

        var forbiddenTokens = new[]
        {
            "OpenWorkbench",
            "ProtocolAdapter",
            "ResponsesRequestComposer",
            "ResponsesToolSurfaceBuilder",
            "ResponsesTransportProtocolBinding",
            "ResponsesTransportRetryStrategy",
            "ProviderBootstrap",
            "ModelCatalog",
        };

        var offenders = exportedTypes
            .Where(name =>
                name.Contains("OpenWorkbench", StringComparison.Ordinal)
                || (name.Contains("OpenAi", StringComparison.Ordinal)
                    && forbiddenTokens.Any(token => name.Contains(token, StringComparison.Ordinal))))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Provider.Abstractions public surface 不应暴露 provider 实现型或 OpenWorkbench 命名；当前违规类型：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void ProviderPlane_ShouldNotRetainOpenWorkbenchProviderDirectories()
    {
        var repoRoot = FindRepoRoot();
        var forbiddenDirectories = new[]
        {
            Path.Combine(repoRoot, "src", "Provider", "OpenWorkbench.Provider.OpenAI"),
            Path.Combine(repoRoot, "src", "Provider", "OpenWorkbench.Provider.OpenAI.Tests"),
            Path.Combine(repoRoot, "src", "Provider", "OpenWorkbench.Provider.Anthropic"),
            Path.Combine(repoRoot, "src", "Provider", "OpenWorkbench.Provider.Anthropic.Tests"),
        };

        Assert.All(
            forbiddenDirectories,
            directory => Assert.False(
                Directory.Exists(directory),
                $"非正式 provider 目录不应继续保留：{Path.GetRelativePath(repoRoot, directory)}"));
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return property.PropertyType;
        }
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            var elementType = type.GetElementType();
            if (elementType is not null)
            {
                foreach (var expanded in ExpandType(elementType))
                {
                    yield return expanded;
                }
            }

            yield break;
        }

        yield return type;

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var expanded in ExpandType(argument))
            {
                yield return expanded;
            }
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 TianShu.sln。");
    }
}
