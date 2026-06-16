namespace TianShu.Contracts.Primitives;

/// <summary>
/// 产物引用快照，用于跨域引用产物而不嵌入完整聚合。
/// Artifact reference snapshot used to cross-reference artifacts without embedding the full aggregate.
/// </summary>
public sealed record ArtifactRef(ArtifactId Id, string? Name = null, string? Kind = null);

/// <summary>
/// 通用资源引用，用于标识非产物类的外部或内部资源。
/// Generic resource reference used to identify non-artifact internal or external resources.
/// </summary>
public sealed record ResourceRef
{
    /// <summary>
    /// 基于资源种类和资源键初始化引用。
    /// Initializes a reference from a resource kind and resource key.
    /// </summary>
    public ResourceRef(string kind, string key)
    {
        Kind = IdentifierGuard.AgainstNullOrWhiteSpace(kind, nameof(kind));
        Key = IdentifierGuard.AgainstNullOrWhiteSpace(key, nameof(key));
    }

    public string Kind { get; }

    public string Key { get; }
}
