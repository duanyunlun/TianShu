namespace TianShu.Contracts.Catalog;

/// <summary>
/// 能力目录已刷新事件。
/// Event emitted when the capability catalog has been refreshed.
/// </summary>
public sealed record CatalogRefreshed(DateTimeOffset RefreshedAt);

/// <summary>
/// Provider 已绑定事件。
/// Event emitted when a provider profile has been bound.
/// </summary>
public sealed record ProviderBound(string ProviderKey);
