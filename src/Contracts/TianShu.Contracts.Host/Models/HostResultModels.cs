using TianShu.Contracts.Primitives;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;

namespace TianShu.Contracts.Host;

/// <summary>
/// 宿主命令结果，表示治理类宿主命令的统一返回包络。
/// Unified result envelope for governance-oriented host commands.
/// </summary>
public sealed record HostCommandResult
{
    /// <summary>
    /// 命令是否被控制平面接受。
    /// Whether the command was accepted by the control plane.
    /// </summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// 结果说明。
    /// Result message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 关联调用标识。
    /// Related call identifier.
    /// </summary>
    public CallId? CallId { get; init; }
}

/// <summary>
/// 宿主轮次提交结果。
/// Host turn-submission result.
/// </summary>
public sealed record HostTurnSubmissionResult
{
    /// <summary>
    /// 是否接受本次提交。
    /// Whether the submission was accepted.
    /// </summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// 结果说明。
    /// Result message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 新建或命中的轮次标识。
    /// Created or matched turn identifier.
    /// </summary>
    public TurnId? TurnId { get; init; }

    /// <summary>
    /// 轮次状态摘要。
    /// Turn status summary.
    /// </summary>
    public string? TurnStatus { get; init; }

    /// <summary>
    /// 跟进关联标识。
    /// Follow-up correlation identifier.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// 请求的跟进模式。
    /// Requested follow-up mode.
    /// </summary>
    public HostFollowUpMode? RequestedMode { get; init; }

    /// <summary>
    /// 实际生效的跟进模式。
    /// Effective follow-up mode.
    /// </summary>
    public HostFollowUpMode? EffectiveMode { get; init; }

    /// <summary>
    /// 关联线程标识。
    /// Related thread identifier.
    /// </summary>
    public ThreadId? ThreadId { get; init; }
}

/// <summary>
/// 宿主输出事件，统一包络宿主通知与只读视图更新。
/// Unified host output envelope for notifications and read-only view updates.
/// </summary>
public sealed record HostOutputEvent
{
    /// <summary>
    /// 初始化宿主输出事件。
    /// Initializes a host output event.
    /// </summary>
    public HostOutputEvent(
        HostNotification? notification = null,
        HostViewUpdate? viewUpdate = null)
    {
        if ((notification is null) == (viewUpdate is null))
        {
            throw new ArgumentException("宿主输出事件必须且只能包含一种载荷。");
        }

        Notification = notification;
        ViewUpdate = viewUpdate;
    }

    /// <summary>
    /// 宿主通知载荷。
    /// Host notification payload.
    /// </summary>
    public HostNotification? Notification { get; }

    /// <summary>
    /// 宿主只读视图更新载荷。
    /// Host read-only view update payload.
    /// </summary>
    public HostViewUpdate? ViewUpdate { get; }
}

/// <summary>
/// 宿主线程列表结果。
/// Host thread-list result.
/// </summary>
public sealed record HostThreadListResult
{
    public IReadOnlyList<ControlPlaneThreadSummary> Threads { get; init; } = Array.Empty<ControlPlaneThreadSummary>();

    public string? NextCursor { get; init; }
}

/// <summary>
/// 宿主已加载线程列表结果。
/// Host loaded-thread-list result.
/// </summary>
public sealed record HostLoadedThreadListResult
{
    public IReadOnlyList<ThreadId> ThreadIds { get; init; } = Array.Empty<ThreadId>();

    public string? NextCursor { get; init; }
}

/// <summary>
/// 宿主线程摘要结果。
/// Host thread-summary result.
/// </summary>
public sealed record HostThreadSummaryResult
{
    public ControlPlaneThreadSummary? Thread { get; init; }
}

/// <summary>
/// 宿主线程快照结果。
/// Host thread-snapshot result.
/// </summary>
public sealed record HostThreadSnapshotResult
{
    public ControlPlaneThreadSnapshot? Snapshot { get; init; }
}

/// <summary>
/// 宿主线程明细结果。
/// Host thread-detail result.
/// </summary>
public sealed record HostThreadOperationResult
{
    public ControlPlaneThreadDetail? Thread { get; init; }
}

/// <summary>
/// 宿主会话摘要工件结果。
/// Host conversation-summary artifact result.
/// </summary>
public sealed record HostConversationArtifactResult
{
    public ControlPlaneConversationArtifact? Artifact { get; init; }
}

/// <summary>
/// 宿主远端 Git Diff 工件结果。
/// Host git-diff-to-remote artifact result.
/// </summary>
public sealed record HostGitDiffArtifactResult
{
    public ControlPlaneGitDiffArtifact Artifact { get; init; } = new();
}

/// <summary>
/// 宿主能力目录结果。
/// Host capability-catalog result.
/// </summary>
public sealed record HostCapabilityCatalogResult
{
    public CapabilityCatalogSnapshot Catalog { get; init; } = new();
}

/// <summary>
/// 宿主引擎绑定解析结果。
/// Host engine-binding resolution result.
/// </summary>
public sealed record HostResolvedEngineBindingResult
{
    public ResolvedEngineBinding Resolution { get; init; } = new(null);
}

/// <summary>
/// 宿主代理列表结果。
/// Host agent-list result.
/// </summary>
public sealed record HostAgentListResult
{
    public IReadOnlyList<ControlPlaneAgentDescriptor> Agents { get; init; } = Array.Empty<ControlPlaneAgentDescriptor>();

    public string? NextCursor { get; init; }
}

/// <summary>
/// 宿主线程取消订阅结果。
/// Host thread-unsubscribe result.
/// </summary>
public sealed record HostThreadUnsubscribeResult
{
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// 宿主执行追踪结果。
/// Host execution-trace result.
/// </summary>
public sealed record HostExecutionTraceResult
{
    public ExecutionTrace? Trace { get; init; }
}

/// <summary>
/// 宿主执行尝试摘要列表结果。
/// Host execution-attempt-summary-list result.
/// </summary>
public sealed record HostAttemptSummaryListResult
{
    public IReadOnlyList<AttemptSummary> Attempts { get; init; } = Array.Empty<AttemptSummary>();
}

/// <summary>
/// 宿主反馈上传结果。
/// Host feedback-upload result.
/// </summary>
public sealed record HostFeedbackUploadResult
{
    public string? ThreadId { get; init; }
}
