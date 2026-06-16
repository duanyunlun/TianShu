namespace TianShu.Configuration;

/// <summary>
/// 解析后的 TianShu 配置层来源类型。
/// Source kinds for resolved TianShu configuration layers.
/// </summary>
public enum ResolvedTianShuConfigLayerSourceKind
{
    System,
    UserModule,
    UserModelRouteSet,
    UserModelProtocolRuleSet,
    User,
    UserProviderInstance,
    Project,
    SessionFlags,
    LegacyManagedConfig,
}

/// <summary>
/// 解析后的单层 TianShu 配置元数据。
/// Metadata for a resolved TianShu configuration layer.
/// </summary>
public sealed class ResolvedTianShuConfigLayer
{
    public ResolvedTianShuConfigLayerSourceKind SourceKind { get; init; }

    public string? Path { get; init; }

    public string? DirectoryPath { get; init; }

    public bool FileExists { get; init; }

    public bool IsEmpty { get; init; }

    public string? DisabledReason { get; init; }

    public bool IsDisabled => !string.IsNullOrWhiteSpace(DisabledReason);
}
