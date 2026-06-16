using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Collaboration;

/// <summary>
/// 协作空间画像，描述该协作域的目标、标签和附加元数据。
/// Collaboration-space profile describing the purpose, labels, and supplemental metadata of a collaboration scope.
/// </summary>
public sealed record CollaborationSpaceProfile
{
    /// <summary>
    /// 初始化协作空间画像。
    /// Initializes a collaboration-space profile.
    /// </summary>
    public CollaborationSpaceProfile(string purpose, LabelSet? labels = null, MetadataBag? metadata = null)
    {
        Purpose = IdentifierGuard.AgainstNullOrWhiteSpace(purpose, nameof(purpose));
        Labels = labels ?? LabelSet.Empty;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string Purpose { get; }

    public LabelSet Labels { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 协作空间默认设置，用于承载该空间的默认工作区和执行配置偏好。
/// Collaboration defaults that carry the preferred workspace and execution profile for a space.
/// </summary>
public sealed record CollaborationDefaultSet(string? DefaultWorkspace = null, string? DefaultExecutionProfile = null)
{
    /// <summary>
    /// 空默认设置。
    /// Empty default set.
    /// </summary>
    public static CollaborationDefaultSet Empty { get; } = new();
}

/// <summary>
/// 协作空间治理策略引用。
/// Reference to a collaboration-level governance policy.
/// </summary>
public sealed record CollaborationPolicyRef
{
    /// <summary>
    /// 初始化治理策略引用。
    /// Initializes a collaboration policy reference.
    /// </summary>
    public CollaborationPolicyRef(string policyKey)
    {
        PolicyKey = IdentifierGuard.AgainstNullOrWhiteSpace(policyKey, nameof(policyKey));
    }

    public string PolicyKey { get; }
}

/// <summary>
/// 协作空间模型，表示高于 Session 的长期协作域。
/// Collaboration-space model representing a long-lived collaboration scope above sessions.
/// </summary>
public sealed record CollaborationSpace
{
    /// <summary>
    /// 初始化协作空间并校验关键标识和展示字段。
    /// Initializes the collaboration space while validating key identity and display fields.
    /// </summary>
    public CollaborationSpace(
        CollaborationSpaceId id,
        string key,
        string displayName,
        CollaborationSpaceProfile profile,
        CollaborationDefaultSet defaults,
        CollaborationPolicyRef? policyRef = null,
        bool isArchived = false)
    {
        Id = id;
        Key = IdentifierGuard.AgainstNullOrWhiteSpace(key, nameof(key));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        PolicyRef = policyRef;
        IsArchived = isArchived;
    }

    public CollaborationSpaceId Id { get; }

    public string Key { get; }

    public string DisplayName { get; }

    public CollaborationSpaceProfile Profile { get; }

    public CollaborationDefaultSet Defaults { get; }

    public CollaborationPolicyRef? PolicyRef { get; }

    public bool IsArchived { get; }
}
