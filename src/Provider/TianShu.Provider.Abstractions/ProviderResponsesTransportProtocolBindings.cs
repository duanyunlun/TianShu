namespace TianShu.Provider.Abstractions;

/// <summary>
/// 按 wire API 解析 responses transport protocol binding 的 provider-neutral 注册表。
/// Provider-neutral registry that resolves responses transport protocol bindings by wire API.
/// </summary>
public static class ProviderResponsesTransportProtocolBindings
{
    public static IProviderResponsesTransportProtocolBinding Resolve(string? providerWireApi, string source)
    {
        var normalized = ProviderWireApi.NormalizeOrThrow(providerWireApi, source);
        if (normalized is null)
        {
            normalized = ProviderWireApi.Responses;
        }

        return ProviderResponsesComponentBootstraps
            .Resolve(normalized, source)
            .CreateTransportProtocolBinding();
    }

    private static IReadOnlyDictionary<string, IProviderResponsesTransportProtocolBinding> BuildBindings()
        => ProviderResponsesComponentBootstraps.BuildComponents(
            static bootstrap => bootstrap.CreateTransportProtocolBinding());
}
