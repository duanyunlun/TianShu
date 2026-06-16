using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Sessions;

/// <summary>
/// 会话模式，表达当前工作时段的主导交互方式。
/// Session mode describing the dominant interaction style of the current work session.
/// </summary>
public enum SessionMode
{
    Interactive = 0,
    Planning = 1,
    Review = 2,
    Automation = 3,
}

/// <summary>
/// 配置栈引用，表示会话所绑定的配置与执行画像来源。
/// Config-stack reference representing the configuration and execution profile source bound to a session.
/// </summary>
public sealed record ConfigStackRef(ResourceRef ConfigStack, string? ProfileName = null);

/// <summary>
/// 会话画像，表达会话标题、目标和附加标签。
/// Session profile describing the title, objective, and supplemental labels of a session.
/// </summary>
public sealed record SessionProfile
{
    /// <summary>
    /// 初始化会话画像。
    /// Initializes a session profile.
    /// </summary>
    public SessionProfile(
        string title,
        string? objective = null,
        LabelSet? labels = null,
        MetadataBag? metadata = null)
    {
        Title = IdentifierGuard.AgainstNullOrWhiteSpace(title, nameof(title));
        Objective = objective;
        Labels = labels ?? LabelSet.Empty;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string Title { get; }

    public string? Objective { get; }

    public LabelSet Labels { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 会话模型，表示落在某个协作域中的一次工作时段。
/// Session model representing a work session that belongs to a collaboration scope.
/// </summary>
public sealed record Session
{
    /// <summary>
    /// 初始化会话模型。
    /// Initializes a session model.
    /// </summary>
    public Session(
        SessionId id,
        CollaborationSpaceRef collaborationSpace,
        SessionProfile profile,
        SessionMode mode,
        ConfigStackRef? configStack = null,
        IReadOnlyList<ParticipantRef>? activeParticipants = null,
        bool isClosed = false)
    {
        Id = id;
        CollaborationSpace = collaborationSpace ?? throw new ArgumentNullException(nameof(collaborationSpace));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Mode = mode;
        ConfigStack = configStack;
        ActiveParticipants = activeParticipants ?? Array.Empty<ParticipantRef>();
        IsClosed = isClosed;
    }

    public SessionId Id { get; }

    public CollaborationSpaceRef CollaborationSpace { get; }

    public SessionProfile Profile { get; }

    public SessionMode Mode { get; }

    public ConfigStackRef? ConfigStack { get; }

    public IReadOnlyList<ParticipantRef> ActiveParticipants { get; }

    public bool IsClosed { get; }
}
