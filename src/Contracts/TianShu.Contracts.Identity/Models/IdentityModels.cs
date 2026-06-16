using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Identity;

/// <summary>
/// 身份作用域。
/// Identity scope.
/// </summary>
public enum IdentityScope
{
    User = 0,
    Workspace = 1,
    Team = 2,
    Session = 3,
    Agent = 4,
    Device = 5,
}

/// <summary>
/// 身份同步策略。
/// Identity sync policy.
/// </summary>
public enum IdentitySyncPolicy
{
    Manual = 0,
    OnSignIn = 1,
    Background = 2,
    Disabled = 3,
}

/// <summary>
/// 账户模型。
/// Account model.
/// </summary>
public sealed record Account
{
    /// <summary>
    /// 初始化账户模型。
    /// Initializes an account model.
    /// </summary>
    public Account(AccountId id, string displayName, string? email = null, MetadataBag? metadata = null)
    {
        Id = id;
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Email = email;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public AccountId Id { get; }

    public string DisplayName { get; }

    public string? Email { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 参与者画像，表达运行时参与者的身份资料映射。
/// Participant profile expressing the identity-profile mapping for a runtime participant.
/// </summary>
public sealed record ParticipantProfile
{
    /// <summary>
    /// 初始化参与者画像。
    /// Initializes a participant profile.
    /// </summary>
    public ParticipantProfile(
        ParticipantId participantId,
        string displayName,
        AccountId? accountId = null,
        string? preferredName = null,
        LabelSet? labels = null)
    {
        ParticipantId = participantId;
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        AccountId = accountId;
        PreferredName = preferredName;
        Labels = labels ?? LabelSet.Empty;
    }

    public ParticipantId ParticipantId { get; }

    public string DisplayName { get; }

    public AccountId? AccountId { get; }

    public string? PreferredName { get; }

    public LabelSet Labels { get; }
}

/// <summary>
/// 工作区成员关系。
/// Workspace membership.
/// </summary>
public sealed record WorkspaceMembership
{
    /// <summary>
    /// 初始化工作区成员关系。
    /// Initializes a workspace membership.
    /// </summary>
    public WorkspaceMembership(string workspaceKey, AccountId accountId, string role)
    {
        WorkspaceKey = IdentifierGuard.AgainstNullOrWhiteSpace(workspaceKey, nameof(workspaceKey));
        AccountId = accountId;
        Role = IdentifierGuard.AgainstNullOrWhiteSpace(role, nameof(role));
    }

    public string WorkspaceKey { get; }

    public AccountId AccountId { get; }

    public string Role { get; }
}

/// <summary>
/// 设备绑定模型。
/// Device-binding model.
/// </summary>
public sealed record DeviceBinding
{
    /// <summary>
    /// 初始化设备绑定模型。
    /// Initializes a device-binding model.
    /// </summary>
    public DeviceBinding(
        DeviceId id,
        AccountId accountId,
        string deviceName,
        string platform,
        DateTimeOffset? boundAt = null)
    {
        Id = id;
        AccountId = accountId;
        DeviceName = IdentifierGuard.AgainstNullOrWhiteSpace(deviceName, nameof(deviceName));
        Platform = IdentifierGuard.AgainstNullOrWhiteSpace(platform, nameof(platform));
        BoundAt = boundAt ?? DateTimeOffset.UtcNow;
    }

    public DeviceId Id { get; }

    public AccountId AccountId { get; }

    public string DeviceName { get; }

    public string Platform { get; }

    public DateTimeOffset BoundAt { get; }
}
