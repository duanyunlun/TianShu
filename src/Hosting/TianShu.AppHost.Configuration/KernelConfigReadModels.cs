namespace TianShu.AppHost.Configuration;

/// <summary>
/// 配置读取中的单层快照。
/// Snapshot of one resolved configuration layer.
/// </summary>
internal sealed record KernelConfigReadLayer(
    object Name,
    string Version,
    Dictionary<string, object?> Config,
    string? DisabledReason = null);

/// <summary>
/// 配置读取的聚合快照。
/// Aggregated snapshot of effective configuration values and ordered layers.
/// </summary>
internal sealed record KernelConfigReadSnapshot(
    Dictionary<string, object?> Config,
    Dictionary<string, object?> Origins,
    object? Layers,
    bool HasPersistentConfig,
    IReadOnlyList<KernelConfigReadLayer> OrderedLayers);
