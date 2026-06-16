namespace TianShu.Provider.Abstractions;

/// <summary>
/// 按 wire API 解析 responses tool surface builder 的 provider-neutral 注册表。
/// Provider-neutral registry that resolves responses tool surface builders by wire API.
/// </summary>
public static class ProviderResponsesToolSurfaceBuilders
{
    public static IProviderResponsesToolSurfaceBuilder Resolve(string? providerWireApi, string source)
    {
        var normalized = ProviderWireApi.NormalizeOrThrow(providerWireApi, source);
        if (normalized is null)
        {
            normalized = ProviderWireApi.Responses;
        }

        return ProviderResponsesComponentBootstraps
            .Resolve(normalized, source)
            .CreateToolSurfaceBuilder();
    }

    private static IReadOnlyDictionary<string, IProviderResponsesToolSurfaceBuilder> BuildBuilders()
        => ProviderResponsesComponentBootstraps.BuildComponents(
            static bootstrap => bootstrap.CreateToolSurfaceBuilder());
}
