namespace TianShu.Provider.Abstractions;

/// <summary>
/// 按 wire API 解析 responses transport retry strategy 的 provider-neutral 注册表。
/// Provider-neutral registry that resolves responses transport retry strategies by wire API.
/// </summary>
public static class ProviderResponsesTransportRetryStrategies
{
    public static IProviderResponsesTransportRetryStrategy Resolve(string? providerWireApi, string source)
    {
        var normalized = ProviderWireApi.NormalizeOrThrow(providerWireApi, source);
        if (normalized is null)
        {
            normalized = ProviderWireApi.Responses;
        }

        return ProviderResponsesComponentBootstraps
            .Resolve(normalized, source)
            .CreateTransportRetryStrategy();
    }

    private static IReadOnlyDictionary<string, IProviderResponsesTransportRetryStrategy> BuildStrategies()
        => ProviderResponsesComponentBootstraps.BuildComponents(
            static bootstrap => bootstrap.CreateTransportRetryStrategy());
}
