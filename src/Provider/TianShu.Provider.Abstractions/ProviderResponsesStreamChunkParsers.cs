namespace TianShu.Provider.Abstractions;

/// <summary>
/// Resolves provider responses stream chunk parsers from registered provider bootstraps.
/// 从已注册 provider bootstrap 中解析 responses stream chunk parser。
/// </summary>
public static class ProviderResponsesStreamChunkParsers
{
    public static IProviderResponsesStreamChunkParser Resolve(string? providerWireApi)
        => ProviderResponsesComponentBootstraps.Resolve(providerWireApi, "provider responses stream chunk parser")
            .CreateStreamChunkParser();
}
