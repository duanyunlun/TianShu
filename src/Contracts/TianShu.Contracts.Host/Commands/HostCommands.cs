using TianShu.Contracts.Interactions;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Host;

/// <summary>
/// 提交宿主轮次命令。
/// Command that submits a host turn.
/// </summary>
public sealed record HostSubmitTurn
{
    /// <summary>
    /// 初始化宿主轮次命令。
    /// Initializes a host turn command.
    /// </summary>
    public HostSubmitTurn(
        HostInteractionEnvelope envelope,
        IReadOnlyList<ControlPlaneConversationMessage>? history = null)
    {
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        History = history ?? Array.Empty<ControlPlaneConversationMessage>();
    }

    /// <summary>
    /// 宿主交互包络。
    /// Host interaction envelope.
    /// </summary>
    public HostInteractionEnvelope Envelope { get; }

    /// <summary>
    /// 补充历史消息。
    /// Supplemental history messages.
    /// </summary>
    public IReadOnlyList<ControlPlaneConversationMessage> History { get; }
}

/// <summary>
/// 宿主后续跟进模式。
/// Host follow-up mode.
/// </summary>
public enum HostFollowUpMode
{
    Queue = 0,
    Steer = 1,
    Interrupt = 2,
}

/// <summary>
/// 提交宿主跟进消息命令。
/// Command that submits a host follow-up.
/// </summary>
public sealed record HostSubmitFollowUp
{
    /// <summary>
    /// 初始化宿主跟进消息命令。
    /// Initializes a host follow-up command.
    /// </summary>
    public HostSubmitFollowUp(
        HostInteractionEnvelope envelope,
        HostFollowUpMode mode = HostFollowUpMode.Queue,
        string? correlationId = null)
    {
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        Mode = mode;
        CorrelationId = correlationId;
    }

    /// <summary>
    /// 宿主交互包络。
    /// Host interaction envelope.
    /// </summary>
    public HostInteractionEnvelope Envelope { get; }

    /// <summary>
    /// 跟进模式。
    /// Follow-up mode.
    /// </summary>
    public HostFollowUpMode Mode { get; }

    /// <summary>
    /// 可选关联标识。
    /// Optional correlation identifier.
    /// </summary>
    public string? CorrelationId { get; }
}

/// <summary>
/// 解析审批命令。
/// Command that resolves an approval.
/// </summary>
public sealed record HostResolveApproval
{
    /// <summary>
    /// 初始化宿主审批解析命令。
    /// Initializes a host approval-resolution command.
    /// </summary>
    public HostResolveApproval(
        CallId callId,
        string decision,
        ApprovalId? approvalId = null,
        IReadOnlyList<string>? commandPrefix = null,
        string? networkHost = null,
        string? networkAction = null,
        string? note = null,
        ParticipantId? resolvedByParticipantId = null,
        HostContext? context = null)
    {
        CallId = callId;
        Decision = IdentifierGuard.AgainstNullOrWhiteSpace(decision, nameof(decision));
        ApprovalId = approvalId;
        CommandPrefix = commandPrefix ?? Array.Empty<string>();
        NetworkHost = networkHost;
        NetworkAction = networkAction;
        Note = note;
        ResolvedByParticipantId = resolvedByParticipantId;
        Context = context;
    }

    /// <summary>
    /// checkpoint 所属调用标识。
    /// Checkpoint call identifier.
    /// </summary>
    public CallId CallId { get; }

    /// <summary>
    /// 宿主侧原始决策 token。
    /// Raw decision token from the host surface.
    /// </summary>
    public string Decision { get; }

    /// <summary>
    /// 可选审批标识，仅用于补充关联关系。
    /// Optional approval identifier used for correlation only.
    /// </summary>
    public ApprovalId? ApprovalId { get; }

    /// <summary>
    /// 可选命令前缀修订。
    /// Optional command-prefix amendment.
    /// </summary>
    public IReadOnlyList<string> CommandPrefix { get; }

    /// <summary>
    /// 可选网络主机修订。
    /// Optional network host amendment.
    /// </summary>
    public string? NetworkHost { get; }

    /// <summary>
    /// 可选网络动作修订。
    /// Optional network action amendment.
    /// </summary>
    public string? NetworkAction { get; }

    /// <summary>
    /// 补充说明。
    /// Additional note.
    /// </summary>
    public string? Note { get; }

    /// <summary>
    /// 解析操作的参与者标识。
    /// Participant identifier that resolved the approval.
    /// </summary>
    public ParticipantId? ResolvedByParticipantId { get; }

    /// <summary>
    /// 可选宿主上下文。
    /// Optional host context.
    /// </summary>
    public HostContext? Context { get; }
}

/// <summary>
/// 授予权限命令。
/// Command that grants a permission request.
/// </summary>
public sealed record HostGrantPermission
{
    /// <summary>
    /// 初始化宿主权限授予命令。
    /// Initializes a host permission-grant command.
    /// </summary>
    public HostGrantPermission(
        CallId callId,
        IReadOnlyDictionary<string, StructuredValue> permissions,
        HostPermissionScope scope = HostPermissionScope.Turn,
        ParticipantId? grantedByParticipantId = null,
        HostContext? context = null)
    {
        if (permissions is null || permissions.Count == 0)
        {
            throw new ArgumentException("权限载荷至少需要一个键值。", nameof(permissions));
        }

        CallId = callId;
        Permissions = permissions;
        Scope = scope;
        GrantedByParticipantId = grantedByParticipantId;
        Context = context;
    }

    /// <summary>
    /// checkpoint 所属调用标识。
    /// Checkpoint call identifier.
    /// </summary>
    public CallId CallId { get; }

    /// <summary>
    /// 权限载荷。
    /// Permission payload.
    /// </summary>
    public IReadOnlyDictionary<string, StructuredValue> Permissions { get; }

    /// <summary>
    /// 授予作用域。
    /// Effective grant scope.
    /// </summary>
    public HostPermissionScope Scope { get; }

    /// <summary>
    /// 执行授予操作的参与者标识。
    /// Participant identifier that granted the permission.
    /// </summary>
    public ParticipantId? GrantedByParticipantId { get; }

    /// <summary>
    /// 可选宿主上下文。
    /// Optional host context.
    /// </summary>
    public HostContext? Context { get; }
}

/// <summary>
/// 提交用户补录输入命令。
/// Command that submits additional user input requested by governance.
/// </summary>
public sealed record HostSubmitUserInput
{
    /// <summary>
    /// 初始化用户补录输入命令。
    /// Initializes a host command for additional user input.
    /// </summary>
    public HostSubmitUserInput(
        CallId callId,
        IReadOnlyDictionary<string, StructuredValue> answers,
        UserInputRequestId? requestId = null,
        ParticipantId? submittedByParticipantId = null,
        HostContext? context = null)
    {
        if (answers is null || answers.Count == 0)
        {
            throw new ArgumentException("补录回答至少需要一个键值。", nameof(answers));
        }

        CallId = callId;
        RequestId = requestId;
        Answers = answers;
        SubmittedByParticipantId = submittedByParticipantId;
        Context = context;
    }

    /// <summary>
    /// checkpoint 所属调用标识。
    /// Checkpoint call identifier.
    /// </summary>
    public CallId CallId { get; }

    /// <summary>
    /// 可选补录请求标识。
    /// Optional user-input request identifier.
    /// </summary>
    public UserInputRequestId? RequestId { get; }

    /// <summary>
    /// 结构化回答载荷。
    /// Structured answer payload.
    /// </summary>
    public IReadOnlyDictionary<string, StructuredValue> Answers { get; }

    /// <summary>
    /// 提交补录回答的参与者标识。
    /// Participant identifier that submitted the requested answers.
    /// </summary>
    public ParticipantId? SubmittedByParticipantId { get; }

    /// <summary>
    /// 可选宿主上下文。
    /// Optional host context.
    /// </summary>
    public HostContext? Context { get; }
}

/// <summary>
/// 恢复宿主线程命令。
/// Command that resumes a host thread.
/// </summary>
public sealed record HostResumeThread
{
    public ThreadId ThreadId { get; init; }

    public IReadOnlyList<StructuredValue>? History { get; init; }

    public string? Path { get; init; }

    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    public string? ServiceTier { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? ApprovalPolicy { get; init; }

    public string? SandboxMode { get; init; }

    public IReadOnlyDictionary<string, StructuredValue>? Configuration { get; init; }

    public string? BaseInstructions { get; init; }

    public string? DeveloperInstructions { get; init; }

    public string? Personality { get; init; }

    public bool PersistExtendedHistory { get; init; }
}

/// <summary>
/// 分叉宿主线程命令。
/// Command that forks a host thread.
/// </summary>
public sealed record HostForkThread
{
    public ThreadId ThreadId { get; init; }

    public string? Path { get; init; }

    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    public string? ServiceTier { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? ApprovalPolicy { get; init; }

    public string? SandboxMode { get; init; }

    public IReadOnlyDictionary<string, StructuredValue>? Configuration { get; init; }

    public string? BaseInstructions { get; init; }

    public string? DeveloperInstructions { get; init; }

    public bool Ephemeral { get; init; }

    public bool PersistExtendedHistory { get; init; }
}

/// <summary>
/// 启动宿主线程命令。
/// Command that starts a host thread.
/// </summary>
public sealed record HostStartThread
{
    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    public string? ServiceTier { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? ApprovalPolicy { get; init; }

    public string? SandboxMode { get; init; }

    public IReadOnlyDictionary<string, StructuredValue>? Configuration { get; init; }

    public string? ServiceName { get; init; }

    public string? BaseInstructions { get; init; }

    public string? DeveloperInstructions { get; init; }

    public string? Personality { get; init; }

    public bool? Ephemeral { get; init; }

    public IReadOnlyList<ControlPlaneDynamicToolSpec>? DynamicTools { get; init; }

    public string? MockExperimentalField { get; init; }

    public bool PersistExtendedHistory { get; init; }

    public bool? ExperimentalRawEvents { get; init; }
}

/// <summary>
/// 中断宿主当前轮次命令。
/// Command that interrupts the current host turn.
/// </summary>
public sealed record HostInterruptTurn;

/// <summary>
/// 重命名宿主线程命令。
/// Command that renames a host thread.
/// </summary>
public sealed record HostRenameThread
{
    public ThreadId ThreadId { get; init; }

    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// 归档宿主线程命令。
/// Command that archives a host thread.
/// </summary>
public sealed record HostArchiveThread
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 删除宿主线程命令。
/// Command that deletes a host thread.
/// </summary>
public sealed record HostDeleteThread
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 更新宿主线程元数据命令。
/// Command that updates host thread metadata.
/// </summary>
public sealed record HostUpdateThreadMetadata
{
    public ThreadId ThreadId { get; init; }

    public bool HasGitSha { get; init; }

    public string? GitSha { get; init; }

    public bool HasGitBranch { get; init; }

    public string? GitBranch { get; init; }

    public bool HasGitOriginUrl { get; init; }

    public string? GitOriginUrl { get; init; }
}

/// <summary>
/// 取消归档宿主线程命令。
/// Command that unarchives a host thread.
/// </summary>
public sealed record HostUnarchiveThread
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 回滚宿主线程命令。
/// Command that rolls back a host thread.
/// </summary>
public sealed record HostRollbackThread
{
    public ThreadId ThreadId { get; init; }

    public int NumTurns { get; init; }
}

/// <summary>
/// 压缩宿主线程命令。
/// Command that compacts a host thread.
/// </summary>
public sealed record HostCompactThread
{
    public ThreadId ThreadId { get; init; }

    public int KeepRecentTurns { get; init; }
}

/// <summary>
/// 清理宿主线程后台终端命令。
/// Command that cleans a host thread's background terminals.
/// </summary>
public sealed record HostCleanBackgroundTerminals
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 取消订阅宿主线程命令。
/// Command that unsubscribes from a host thread.
/// </summary>
public sealed record HostUnsubscribeThread
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 上传宿主反馈命令。
/// Command that uploads host feedback into the diagnostics plane.
/// </summary>
public sealed record HostUploadFeedback
{
    public string Classification { get; init; } = string.Empty;

    public bool IncludeLogs { get; init; }

    public string? ThreadId { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<string> ExtraLogFiles { get; init; } = Array.Empty<string>();
}
