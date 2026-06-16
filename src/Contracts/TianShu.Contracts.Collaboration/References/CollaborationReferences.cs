using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Collaboration;

/// <summary>
/// 协作空间引用快照，用于跨域传递最小身份信息。
/// Collaboration-space reference snapshot used to carry minimal identity across domains.
/// </summary>
public sealed record CollaborationSpaceRef(CollaborationSpaceId Id, string Key, string DisplayName)
{
    /// <summary>
    /// 从完整协作空间对象生成最小引用。
    /// Creates a minimal reference from a full collaboration-space object.
    /// </summary>
    public static CollaborationSpaceRef From(CollaborationSpace space)
    {
        ArgumentNullException.ThrowIfNull(space);
        return new CollaborationSpaceRef(space.Id, space.Key, space.DisplayName);
    }
}
