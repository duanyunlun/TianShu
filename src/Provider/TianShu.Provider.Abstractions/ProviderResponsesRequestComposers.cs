namespace TianShu.Provider.Abstractions;

/// <summary>
/// 按 wire API 解析 responses request composer 的 provider-neutral 注册表。
/// Provider-neutral registry that resolves responses request composers by wire API.
/// </summary>
public static class ProviderResponsesRequestComposers
{
    public static IProviderResponsesRequestComposer Resolve(string? providerWireApi, string source)
    {
        var normalized = ProviderWireApi.NormalizeOrThrow(providerWireApi, source);
        if (normalized is null)
        {
            normalized = ProviderWireApi.Responses;
        }

        return ProviderResponsesComponentBootstraps
            .Resolve(normalized, source)
            .CreateRequestComposer();
    }

    private static IReadOnlyDictionary<string, IProviderResponsesRequestComposer> BuildComposers()
        => ProviderResponsesComponentBootstraps.BuildComponents(
            static bootstrap => bootstrap.CreateRequestComposer());
}
