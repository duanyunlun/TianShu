using TianShu.Contracts.Interactions;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Conversations;

/// <summary>
/// 控制平面后续跟进模式。
/// Control-plane follow-up mode.
/// </summary>
public enum ControlPlaneFollowUpMode
{
    Queue = 0,
    Steer = 1,
    Interrupt = 2,
}

/// <summary>
/// 控制平面待处理 follow-up 变更类型。
/// Control-plane pending follow-up mutation kind.
/// </summary>
public enum ControlPlanePendingFollowUpMutationKind
{
    PromoteToSteer = 0,
    Drop = 1,
}

/// <summary>
/// 控制平面提交轮次命令。
/// Control-plane command that submits a turn.
/// </summary>
public sealed record ControlPlaneSubmitTurnCommand
{
    /// <summary>
    /// 归一化后的交互包络。
    /// Normalized interaction envelope.
    /// </summary>
    public InteractionEnvelope? Envelope { get; init; }

    /// <summary>
    /// 输入项集合。
    /// Input item collection.
    /// </summary>
    public IReadOnlyList<ControlPlaneInputItem> Inputs { get; init; } = Array.Empty<ControlPlaneInputItem>();

    /// <summary>
    /// 附带历史消息。
    /// Supplemental history messages.
    /// </summary>
    public IReadOnlyList<ControlPlaneConversationMessage> History { get; init; } = Array.Empty<ControlPlaneConversationMessage>();
}

/// <summary>
/// 控制平面提交跟进消息命令。
/// Control-plane command that submits a follow-up.
/// </summary>
public sealed record ControlPlaneSubmitFollowUpCommand
{
    public InteractionEnvelope? Envelope { get; init; }

    public IReadOnlyList<ControlPlaneInputItem> Inputs { get; init; } = Array.Empty<ControlPlaneInputItem>();

    public ControlPlaneFollowUpMode Mode { get; init; }

    public string? CorrelationId { get; init; }
}

/// <summary>
/// 控制平面待处理 follow-up 变更命令。
/// Control-plane command that mutates a pending follow-up.
/// </summary>
public sealed record ControlPlaneMutatePendingFollowUpCommand
{
    public ThreadId? ThreadId { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public ControlPlanePendingFollowUpMutationKind Kind { get; init; }
}

/// <summary>
/// 控制平面启动线程命令。
/// Control-plane command that starts a thread.
/// </summary>
public sealed record ControlPlaneStartThreadCommand
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
/// 控制平面分叉线程命令。
/// Control-plane command that forks a thread.
/// </summary>
public sealed record ControlPlaneForkThreadCommand
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
/// 控制平面恢复线程命令。
/// Control-plane command that resumes a thread.
/// </summary>
public sealed record ControlPlaneResumeThreadCommand
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
/// 控制平面压缩线程命令。
/// Control-plane command that compacts a thread.
/// </summary>
public sealed record ControlPlaneCompactThreadCommand
{
    public ThreadId ThreadId { get; init; }

    public int KeepRecentTurns { get; init; }
}

/// <summary>
/// 控制平面重命名线程命令。
/// Control-plane command that renames a thread.
/// </summary>
public sealed record ControlPlaneRenameThreadCommand
{
    public ThreadId ThreadId { get; init; }

    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面归档线程命令。
/// Control-plane command that archives a thread.
/// </summary>
public sealed record ControlPlaneArchiveThreadCommand
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 控制平面删除线程命令。
/// Control-plane command that deletes a thread.
/// </summary>
public sealed record ControlPlaneDeleteThreadCommand
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 控制平面清空全部线程命令。
/// Control-plane command that clears all threads.
/// </summary>
public sealed record ControlPlaneClearThreadsCommand;

/// <summary>
/// 控制平面清理线程后台终端命令。
/// Control-plane command that cleans a thread's background terminals.
/// </summary>
public sealed record ControlPlaneCleanBackgroundTerminalsCommand
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 控制平面取消订阅线程命令。
/// Control-plane command that unsubscribes from a thread.
/// </summary>
public sealed record ControlPlaneUnsubscribeThreadCommand
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 控制平面递增线程挂起交互计数命令。
/// Control-plane command that increments a thread's elicitation counter.
/// </summary>
public sealed record ControlPlaneIncrementThreadElicitationCommand
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 控制平面递减线程挂起交互计数命令。
/// Control-plane command that decrements a thread's elicitation counter.
/// </summary>
public sealed record ControlPlaneDecrementThreadElicitationCommand
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 控制平面取消归档线程命令。
/// Control-plane command that unarchives a thread.
/// </summary>
public sealed record ControlPlaneUnarchiveThreadCommand
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 控制平面更新线程 Git 元数据命令。
/// Control-plane command that updates thread Git metadata.
/// </summary>
public sealed record ControlPlaneUpdateThreadMetadataCommand
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
/// 控制平面回滚线程命令。
/// Control-plane command that rolls back a thread.
/// </summary>
public sealed record ControlPlaneRollbackThreadCommand
{
    public ThreadId ThreadId { get; init; }

    public int NumTurns { get; init; }
}

/// <summary>
/// 控制平面启动模糊文件搜索会话命令。
/// Control-plane command that starts a fuzzy-file-search session.
/// </summary>
public sealed record ControlPlaneStartFuzzyFileSearchSessionCommand
{
    public string SessionId { get; init; } = string.Empty;

    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 控制平面更新模糊文件搜索会话命令。
/// Control-plane command that updates a fuzzy-file-search session.
/// </summary>
public sealed record ControlPlaneUpdateFuzzyFileSearchSessionCommand
{
    public string SessionId { get; init; } = string.Empty;

    public string Query { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面停止模糊文件搜索会话命令。
/// Control-plane command that stops a fuzzy-file-search session.
/// </summary>
public sealed record ControlPlaneStopFuzzyFileSearchSessionCommand
{
    public string SessionId { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面启动实时双工会话命令。
/// Control-plane command that starts a realtime duplex session.
/// </summary>
public sealed record ControlPlaneRealtimeStartCommand
{
    public ThreadId ThreadId { get; init; }

    public string? SessionId { get; init; }

    public string? Prompt { get; init; }
}

/// <summary>
/// 控制平面追加实时文本输入命令。
/// Control-plane command that appends realtime text input.
/// </summary>
public sealed record ControlPlaneRealtimeAppendTextCommand
{
    public ThreadId ThreadId { get; init; }

    public string? SessionId { get; init; }

    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面追加实时音频输入命令。
/// Control-plane command that appends realtime audio input.
/// </summary>
public sealed record ControlPlaneRealtimeAppendAudioCommand
{
    public ThreadId ThreadId { get; init; }

    public string? SessionId { get; init; }

    public ControlPlaneRealtimeAudioInput Audio { get; init; } = new();
}

/// <summary>
/// 控制平面提交实时交接输出命令。
/// Control-plane command that submits realtime handoff output.
/// </summary>
public sealed record ControlPlaneRealtimeHandoffOutputCommand
{
    public ThreadId ThreadId { get; init; }

    public string? SessionId { get; init; }

    public string HandoffId { get; init; } = string.Empty;

    public string Output { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面停止实时双工会话命令。
/// Control-plane command that stops a realtime duplex session.
/// </summary>
public sealed record ControlPlaneRealtimeStopCommand
{
    public ThreadId ThreadId { get; init; }

    public string? SessionId { get; init; }
}
