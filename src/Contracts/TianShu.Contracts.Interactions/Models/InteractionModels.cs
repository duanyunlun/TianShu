using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Interactions;

/// <summary>
/// 交互来源种类，描述一次交互是从哪个上行通道进入控制平面的。
/// Kind of interaction source describing which northbound channel introduced an interaction into the control plane.
/// </summary>
public enum InteractionSourceKind
{
    Host = 0,
    Approval = 1,
    Workflow = 2,
    ScheduledJob = 3,
    ExternalEvent = 4,
    Automation = 5,
}

/// <summary>
/// 交互来源，描述交互来自哪个宿主或系统入口。
/// Interaction source describing which host or system entry point produced the interaction.
/// </summary>
public sealed record InteractionSource
{
    /// <summary>
    /// 初始化交互来源。
    /// Initializes an interaction source.
    /// </summary>
    public InteractionSource(InteractionSourceKind kind, string surface)
    {
        Kind = kind;
        Surface = IdentifierGuard.AgainstNullOrWhiteSpace(surface, nameof(surface));
    }

    public InteractionSourceKind Kind { get; }

    public string Surface { get; }
}

/// <summary>
/// 交互目标，表示该交互希望落到哪个协作空间、线程或工作流。
/// Interaction target describing which collaboration space, thread, or workflow the interaction should land in.
/// </summary>
public sealed record InteractionTarget(
    CollaborationSpaceId? CollaborationSpaceId = null,
    ThreadId? ThreadId = null,
    WorkflowId? WorkflowId = null);

/// <summary>
/// 交互路由提示，表达 UI 或控制平面可选的路由偏好。
/// Interaction routing hint expressing optional UI or control-plane routing preferences.
/// </summary>
public sealed record InteractionRoutingHint(
    string? Intent = null,
    string? Surface = null,
    bool PreferForeground = false);

/// <summary>
/// 文本字节区间，标注一段文本中的结构化片段范围。
/// Byte range over text used to mark a structured fragment inside textual input.
/// </summary>
public sealed record InteractionByteRange
{
    /// <summary>
    /// 初始化文本字节区间。
    /// Initializes a text byte range.
    /// </summary>
    public InteractionByteRange(int start, int end)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "起始位置不能为负。");
        }

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "结束位置不能小于起始位置。");
        }

        Start = start;
        End = end;
    }

    public int Start { get; }

    public int End { get; }
}

/// <summary>
/// 文本交互元素，用于对文本内部片段做语义标注。
/// Text interaction element used to add semantic annotations to a text fragment.
/// </summary>
public sealed record TextInteractionElement(InteractionByteRange ByteRange, string? Placeholder = null);

/// <summary>
/// 统一交互项基类。
/// Unified base type for interaction items.
/// </summary>
public abstract record InteractionItem(string Kind);

/// <summary>
/// 文本交互项。
/// Text interaction item.
/// </summary>
public sealed record TextInteractionItem(
    string Text,
    IReadOnlyList<TextInteractionElement>? Elements = null)
    : InteractionItem("text")
{
    public string Text { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Text, nameof(Text));

    public IReadOnlyList<TextInteractionElement> Elements { get; } = Elements ?? Array.Empty<TextInteractionElement>();
}

/// <summary>
/// 远程图像交互项。
/// Remote-image interaction item.
/// </summary>
public sealed record ImageInteractionItem(string Url)
    : InteractionItem("image")
{
    public string Url { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Url, nameof(Url));
}

/// <summary>
/// 本地图像交互项。
/// Local-image interaction item.
/// </summary>
public sealed record LocalImageInteractionItem(string Path)
    : InteractionItem("local_image")
{
    public string Path { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Path, nameof(Path));
}

/// <summary>
/// 技能交互项，用于把技能或能力面带入输入包络。
/// Skill interaction item used to bring a skill or capability surface into the input envelope.
/// </summary>
public sealed record SkillInteractionItem(string Name, string Path)
    : InteractionItem("skill")
{
    public string Name { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Name, nameof(Name));

    public string Path { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Path, nameof(Path));
}

/// <summary>
/// 提及交互项，用于在输入中携带显式提及对象。
/// Mention interaction item used to carry explicit mention targets in the input.
/// </summary>
public sealed record MentionInteractionItem(string Name, string Path)
    : InteractionItem("mention")
{
    public string Name { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Name, nameof(Name));

    public string Path { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Path, nameof(Path));
}

/// <summary>
/// 结构化交互项，用于承接审批、补录、按钮点击等非对话文本宿主输入。
/// Structured interaction item used for host inputs such as approvals, user-input submissions, and button clicks.
/// </summary>
public sealed record StructuredInteractionItem(string SemanticKind, StructuredValue Payload)
    : InteractionItem(SemanticKind)
{
    public string SemanticKind { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(SemanticKind, nameof(SemanticKind));

    public StructuredValue Payload { get; } = Payload ?? throw new ArgumentNullException(nameof(Payload));
}

/// <summary>
/// 交互包络，表示控制平面接收的统一交互单元。
/// Interaction envelope representing the normalized interaction unit accepted by the control plane.
/// </summary>
public sealed record InteractionEnvelope
{
    /// <summary>
    /// 初始化交互包络并保证至少存在一个输入项。
    /// Initializes the interaction envelope and guarantees that at least one input item exists.
    /// </summary>
    public InteractionEnvelope(
        InteractionEnvelopeId id,
        InteractionSource source,
        IReadOnlyList<InteractionItem> items,
        InteractionTarget? target = null,
        InteractionRoutingHint? routingHint = null,
        ParticipantId? initiatedByParticipantId = null,
        DateTimeOffset? createdAt = null)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("交互包至少需要一个输入项。", nameof(items));
        }

        Id = id;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Items = items;
        Target = target;
        RoutingHint = routingHint;
        InitiatedByParticipantId = initiatedByParticipantId;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public InteractionEnvelopeId Id { get; }

    public InteractionSource Source { get; }

    public IReadOnlyList<InteractionItem> Items { get; }

    public InteractionTarget? Target { get; }

    public InteractionRoutingHint? RoutingHint { get; }

    public ParticipantId? InitiatedByParticipantId { get; }

    public DateTimeOffset CreatedAt { get; }
}
