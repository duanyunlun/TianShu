using TianShu.Contracts.Host;
using TianShu.Contracts.Conversations;

namespace TianShu.HostGateway;

/// <summary>
/// Host Gateway 正式统一入口，只暴露宿主 operation、view update 和 snapshot surface。
/// Formal unified Host Gateway entry that exposes only host operations, view updates, and snapshot surfaces.
/// </summary>
public interface IHostGateway
{
    /// <summary>
    /// 调用一个宿主操作，并交由 Control Plane 归一化和处理。
    /// Invokes one host operation and delegates normalization and handling to Control Plane.
    /// </summary>
    ValueTask<HostOperationResult> InvokeAsync(HostOperationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 订阅宿主可消费的只读视图更新。
    /// Subscribes to host-consumable read-only view updates.
    /// </summary>
    IAsyncEnumerable<HostViewUpdate> SubscribeAsync(HostSubscriptionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 读取宿主可消费的只读快照。
    /// Reads a host-consumable read-only snapshot.
    /// </summary>
    ValueTask<HostSnapshot> SnapshotAsync(HostSnapshotRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 宿主网关，负责把宿主层 typed 命令接入 TianShu 控制平面。
/// Host gateway that connects host-facing typed commands into the TianShu control plane.
/// </summary>
public interface ITianShuHostGateway : IHostGateway
{
    /// <summary>
    /// 提交一条宿主轮次输入。
    /// Submits a host turn into the control plane.
    /// </summary>
    Task<HostTurnSubmissionResult> SubmitTurnAsync(HostSubmitTurn command, CancellationToken cancellationToken);

    /// <summary>
    /// 提交一条宿主跟进输入。
    /// Submits a host follow-up into the control plane.
    /// </summary>
    Task<HostTurnSubmissionResult> SubmitFollowUpAsync(HostSubmitFollowUp command, CancellationToken cancellationToken);

    /// <summary>
    /// 解析一条审批请求。
    /// Resolves an approval request.
    /// </summary>
    Task<HostCommandResult> ResolveApprovalAsync(HostResolveApproval command, CancellationToken cancellationToken);

    /// <summary>
    /// 授予一条权限请求。
    /// Grants a permission request.
    /// </summary>
    Task<HostCommandResult> GrantPermissionAsync(HostGrantPermission command, CancellationToken cancellationToken);

    /// <summary>
    /// 提交一条补录回答。
    /// Submits a requested user-input answer payload.
    /// </summary>
    Task<HostCommandResult> SubmitUserInputAsync(HostSubmitUserInput command, CancellationToken cancellationToken);

    /// <summary>
    /// 查询线程列表。
    /// Lists threads for the host surface.
    /// </summary>
    Task<HostThreadListResult> ListThreadsAsync(HostListThreadsQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 查询已加载线程列表。
    /// Lists loaded threads for the host surface.
    /// </summary>
    Task<HostLoadedThreadListResult> ListLoadedThreadsAsync(HostListLoadedThreadsQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 启动新线程。
    /// Starts a new thread for the host surface.
    /// </summary>
    Task<HostThreadSummaryResult> StartThreadAsync(HostStartThread command, CancellationToken cancellationToken);

    /// <summary>
    /// 恢复线程。
    /// Resumes a thread for the host surface.
    /// </summary>
    Task<HostThreadSnapshotResult> ResumeThreadAsync(HostResumeThread command, CancellationToken cancellationToken);

    /// <summary>
    /// 分叉线程。
    /// Forks a thread for the host surface.
    /// </summary>
    Task<HostThreadSummaryResult> ForkThreadAsync(HostForkThread command, CancellationToken cancellationToken);

    /// <summary>
    /// 读取线程详情。
    /// Reads thread details for the host surface.
    /// </summary>
    Task<HostThreadOperationResult> ReadThreadAsync(HostReadThreadQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 重命名线程。
    /// Renames a thread for the host surface.
    /// </summary>
    Task<HostCommandResult> RenameThreadAsync(HostRenameThread command, CancellationToken cancellationToken);

    /// <summary>
    /// 归档线程。
    /// Archives a thread for the host surface.
    /// </summary>
    Task<HostCommandResult> ArchiveThreadAsync(HostArchiveThread command, CancellationToken cancellationToken);

    /// <summary>
    /// 删除线程。
    /// Deletes a thread for the host surface.
    /// </summary>
    Task<HostCommandResult> DeleteThreadAsync(HostDeleteThread command, CancellationToken cancellationToken);

    /// <summary>
    /// 中断当前轮次。
    /// Interrupts the current turn.
    /// </summary>
    Task<HostCommandResult> InterruptTurnAsync(HostInterruptTurn command, CancellationToken cancellationToken);

    /// <summary>
    /// 更新线程元数据。
    /// Updates thread metadata for the host surface.
    /// </summary>
    Task<HostThreadOperationResult> UpdateThreadMetadataAsync(HostUpdateThreadMetadata command, CancellationToken cancellationToken);

    /// <summary>
    /// 取消归档线程。
    /// Unarchives a thread for the host surface.
    /// </summary>
    Task<HostThreadOperationResult> UnarchiveThreadAsync(HostUnarchiveThread command, CancellationToken cancellationToken);

    /// <summary>
    /// 回滚线程。
    /// Rolls back a thread for the host surface.
    /// </summary>
    Task<HostThreadOperationResult> RollbackThreadAsync(HostRollbackThread command, CancellationToken cancellationToken);

    /// <summary>
    /// 压缩线程。
    /// Compacts a thread for the host surface.
    /// </summary>
    Task<HostCommandResult> CompactThreadAsync(HostCompactThread command, CancellationToken cancellationToken);

    /// <summary>
    /// 清理线程后台终端。
    /// Cleans background terminals for the host surface.
    /// </summary>
    Task<HostCommandResult> CleanBackgroundTerminalsAsync(HostCleanBackgroundTerminals command, CancellationToken cancellationToken);

    /// <summary>
    /// 取消订阅线程。
    /// Unsubscribes a thread for the host surface.
    /// </summary>
    Task<HostThreadUnsubscribeResult> UnsubscribeThreadAsync(HostUnsubscribeThread command, CancellationToken cancellationToken);

    /// <summary>
    /// 读取会话摘要工件。
    /// Reads a conversation-summary artifact.
    /// </summary>
    Task<HostConversationArtifactResult> GetConversationSummaryAsync(HostReadConversationSummaryQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 读取远端 Git Diff 工件。
    /// Reads a git-diff-to-remote artifact.
    /// </summary>
    Task<HostGitDiffArtifactResult> GetGitDiffToRemoteAsync(HostReadGitDiffToRemoteQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 读取能力目录。
    /// Reads the capability catalog for the host surface.
    /// </summary>
    Task<HostCapabilityCatalogResult> GetCapabilityCatalogAsync(HostGetCapabilityCatalogQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 解析引擎绑定。
    /// Resolves an engine binding for the host surface.
    /// </summary>
    Task<HostResolvedEngineBindingResult> ResolveEngineBindingAsync(HostResolveEngineBindingQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 查询代理列表。
    /// Lists agents for the host surface.
    /// </summary>
    Task<HostAgentListResult> ListAgentsAsync(HostListAgentsQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 读取执行追踪。
    /// Reads one execution trace for the host surface.
    /// </summary>
    Task<HostExecutionTraceResult> GetExecutionTraceAsync(HostReadExecutionTraceQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 读取执行尝试摘要列表。
    /// Lists execution attempt summaries for the host surface.
    /// </summary>
    Task<HostAttemptSummaryListResult> ListAttemptSummariesAsync(HostListAttemptSummariesQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 上传宿主反馈。
    /// Uploads host feedback through the diagnostics plane.
    /// </summary>
    Task<HostFeedbackUploadResult> UploadFeedbackAsync(HostUploadFeedback command, CancellationToken cancellationToken);

    /// <summary>
    /// 订阅宿主输出事件。
    /// Subscribes to host output events.
    /// </summary>
    new IAsyncEnumerable<HostOutputEvent> SubscribeAsync(HostSubscriptionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 订阅会话流事件。
    /// Subscribes to conversation stream events.
    /// </summary>
    IAsyncEnumerable<ControlPlaneConversationStreamEvent> SubscribeConversationStreamAsync(
        HostConversationStreamSubscription request,
        CancellationToken cancellationToken);
}
