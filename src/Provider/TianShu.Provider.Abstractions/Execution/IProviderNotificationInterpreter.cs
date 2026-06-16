using TianShu.Contracts.Provider;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider 原始通知解释器边界。
/// Typed interpreter boundary for provider-originated raw notifications.
/// </summary>
public interface IProviderNotificationInterpreter
{
    /// <summary>
    /// 解释 `rawResponseItem/completed` 一类 southbound 完成通知。
    /// Interprets southbound completion notifications such as `rawResponseItem/completed`.
    /// </summary>
    ProviderNotificationProjection? InterpretRawResponseItemCompleted(ProviderRawResponseItemCompletedNotification notification);

    /// <summary>
    /// 解释 `error` 一类 southbound 失败通知。
    /// Interprets southbound failure notifications such as `error`.
    /// </summary>
    ProviderNotificationProjection? InterpretError(ProviderErrorNotification notification);

    /// <summary>
    /// 解释 `item/*` 一类 southbound item 通知。
    /// Interprets southbound `item/*` notifications.
    /// </summary>
    ProviderNotificationProjection? InterpretItem(ProviderItemNotification notification);
}

/// <summary>
/// Provider 通知解释结果，携带 runtime 投影所需的公共上下文与 provider typed 事件工厂。
/// Provider notification interpretation result carrying projection context plus a provider-event factory.
/// </summary>
public sealed class ProviderNotificationProjection
{
    private readonly Func<ProviderNotificationProjectionInput, IReadOnlyList<ProviderStreamEvent>> eventFactory;

    /// <summary>
    /// 初始化通知解释结果。
    /// Initializes a notification projection result.
    /// </summary>
    public ProviderNotificationProjection(
        string? itemId,
        string? status,
        string? phase,
        Func<ProviderNotificationProjectionInput, IReadOnlyList<ProviderStreamEvent>> eventFactory,
        long? summaryIndex = null,
        long? contentIndex = null,
        string? callId = null,
        string? toolName = null,
        string? serverName = null)
    {
        this.eventFactory = eventFactory ?? throw new ArgumentNullException(nameof(eventFactory));
        ItemId = itemId;
        Status = status;
        Phase = phase;
        SummaryIndex = summaryIndex;
        ContentIndex = contentIndex;
        CallId = callId;
        ToolName = toolName;
        ServerName = serverName;
    }

    /// <summary>
    /// 对应的 item 标识。
    /// Associated item identifier.
    /// </summary>
    public string? ItemId { get; }

    /// <summary>
    /// 对应 southbound 状态。
    /// Associated southbound status.
    /// </summary>
    public string? Status { get; }

    /// <summary>
    /// 对应 southbound 阶段。
    /// Associated southbound phase.
    /// </summary>
    public string? Phase { get; }

    /// <summary>
    /// 可选推理摘要索引。
    /// Optional reasoning summary index.
    /// </summary>
    public long? SummaryIndex { get; }

    /// <summary>
    /// 可选内容索引。
    /// Optional content index.
    /// </summary>
    public long? ContentIndex { get; }

    /// <summary>
    /// 可选工具调用标识。
    /// Optional tool call identifier.
    /// </summary>
    public string? CallId { get; }

    /// <summary>
    /// 可选工具名称。
    /// Optional tool name.
    /// </summary>
    public string? ToolName { get; }

    /// <summary>
    /// 可选服务端名称。
    /// Optional server name.
    /// </summary>
    public string? ServerName { get; }

    /// <summary>
    /// 生成 provider typed 事件序列。
    /// Creates the provider-typed event sequence.
    /// </summary>
    public IReadOnlyList<ProviderStreamEvent> CreateEvents(ProviderNotificationProjectionInput? input = null)
        => eventFactory(input ?? new ProviderNotificationProjectionInput());
}

/// <summary>
/// 解释结果的附加输入。
/// Supplemental input passed into the interpreted projection.
/// </summary>
public sealed record ProviderNotificationProjectionInput(string? PreferredText = null);

/// <summary>
/// Provider 通知中的 item 快照。
/// Item snapshot carried by a provider notification.
/// </summary>
public sealed record ProviderNotificationItem(
    string? Id,
    string? Type,
    string? Status,
    string? Phase,
    string? Name,
    string? ToolName,
    string? CallId,
    string? Text,
    string? OutputText,
    string? Delta,
    string? Output,
    string? Arguments,
    string? Input);

/// <summary>
/// Provider 通知中的错误快照。
/// Error snapshot carried by a provider notification.
/// </summary>
public sealed record ProviderNotificationError(string? Message, string? AdditionalDetails);

/// <summary>
/// `rawResponseItem/completed` 的 typed 输入模型。
/// Typed input model for `rawResponseItem/completed`.
/// </summary>
public sealed record ProviderRawResponseItemCompletedNotification(
    string Method,
    string? ThreadId,
    string? TurnId,
    ProviderNotificationItem? Item);

/// <summary>
/// `error` 通知的 typed 输入模型。
/// Typed input model for `error` notifications.
/// </summary>
public sealed record ProviderErrorNotification(
    string Method,
    string? ThreadId,
    string? TurnId,
    string? Message,
    ProviderNotificationError? Error,
    bool? WillRetry);

/// <summary>
/// `item/*` 通知的 typed 输入模型。
/// Typed input model for `item/*` notifications.
/// </summary>
public sealed record ProviderItemNotification(
    string Method,
    string? ThreadId,
    string? TurnId,
    string? ItemId,
    string? Type,
    string? Status,
    string? Phase,
    string? ToolName,
    string? Name,
    string? CallId,
    string? ToolCallId,
    string? Delta,
    string? Output,
    string? Arguments,
    string? Input,
    bool? RequiresApproval,
    bool? ApprovalRequired,
    bool? ApprovalStateRequired,
    string? Message,
    long? SummaryIndex,
    long? ContentIndex,
    string? ProcessId,
    string? Stdin,
    ProviderNotificationItem? Item);
