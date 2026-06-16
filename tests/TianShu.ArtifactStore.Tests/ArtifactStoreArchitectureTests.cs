using System.Reflection;
using TianShu.ArtifactStore;

namespace TianShu.ArtifactStore.Tests;

public sealed class ArtifactStoreArchitectureTests
{
    [Fact]
    public void ArtifactStoreProjectionMaterializer_ShouldRemainInternal()
    {
        var type = typeof(IArtifactStore).Assembly.GetType("TianShu.ArtifactStore.ArtifactStoreProjectionMaterializer");

        Assert.NotNull(type);
        Assert.False(type!.IsPublic);
        Assert.False(type.IsNestedPublic);
    }

    [Fact]
    public void ArtifactStorePublicSurface_ShouldExposeOnlyArtifactTypedContracts()
    {
        var exportedTypes = typeof(IArtifactStore).Assembly
            .GetExportedTypes()
            .Where(static type => string.Equals(type.Namespace, "TianShu.ArtifactStore", StringComparison.Ordinal))
            .ToArray();

        var forbiddenNamespacePrefixes = new[]
        {
            "TianShu.Contracts.Host",
            "TianShu.Contracts.Conversations",
            "TianShu.Contracts.Governance",
            "TianShu.Contracts.Interactions",
            "TianShu.Contracts.Projections",
            "TianShu.ControlPlane",
        };

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
            $"ArtifactStore public surface 不应直接暴露 host/control-plane/projection payload 类型；当前违规类型：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void ArtifactStorePublicSurface_ShouldNotPromoteProjectionPayloadModels()
    {
        var exportedTypes = typeof(IArtifactStore).Assembly
            .GetExportedTypes()
            .Where(static type => string.Equals(type.Namespace, "TianShu.ArtifactStore", StringComparison.Ordinal))
            .ToArray();

        var offenders = exportedTypes
            .SelectMany(GetPublicSignatureTypes)
            .SelectMany(ExpandType)
            .Where(type => (type.FullName ?? type.Name).Contains("ProjectionPayload", StringComparison.Ordinal))
            .Select(static type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"ArtifactStore public API 不应直接暴露 projection payload 模型；当前违规类型：{string.Join(", ", offenders)}");
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
}
