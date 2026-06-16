using TianShu.Contracts.Primitives;
using TianShu.Contracts.Projections;

namespace TianShu.ProjectionStores;

/// <summary>
/// 投影视图快照键，用于唯一标识某个作用域下的当前物化视图。
/// Projection snapshot key that uniquely identifies the current materialized view for a scope.
/// </summary>
public readonly record struct ProjectionSnapshotKey
{
    /// <summary>
    /// 初始化投影视图快照键。
    /// Initializes a projection snapshot key.
    /// </summary>
    public ProjectionSnapshotKey(ProjectionScopeKind scopeKind, string scopeKey)
    {
        ScopeKind = scopeKind;
        ScopeKey = IdentifierGuard.AgainstNullOrWhiteSpace(scopeKey, nameof(scopeKey));
    }

    /// <summary>
    /// 投影作用域种类。
    /// Projection scope kind.
    /// </summary>
    public ProjectionScopeKind ScopeKind { get; }

    /// <summary>
    /// 投影作用域键。
    /// Projection scope key.
    /// </summary>
    public string ScopeKey { get; }
}
