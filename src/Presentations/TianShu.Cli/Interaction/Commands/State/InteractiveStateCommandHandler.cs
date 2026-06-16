using System.Text.Json;
using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Sessions;
using TianShu.Execution.Runtime;

namespace TianShu.Cli.Interaction.Commands.State;

/// <summary>
/// Handles the interactive /state command and its diagnostic JSON projection.
/// 处理交互式 /state 命令及其诊断 JSON 投影。
/// </summary>
internal sealed class InteractiveStateCommandHandler
{
    public async Task HandleStateCommandAsync(
        IExecutionRuntime runtime,
        InteractiveStateCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(context);

        var sessionSnapshot = await CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, cancellationToken).ConfigureAwait(false);
        context.ApplySessionSnapshot(sessionSnapshot);
        var pendingSnapshot = context.PendingRequests.Snapshot;
        var restoredFollowUps = context.RestoredFollowUps;
        context.WriteLine(JsonSerializer.Serialize(new
        {
            threadId = sessionSnapshot.ActiveThreadId?.Value,
            hasActiveTurn = sessionSnapshot.HasActiveTurn,
            pendingApprovalCallIds = pendingSnapshot.ApprovalCallIds,
            pendingPermissionCallIds = pendingSnapshot.PermissionCallIds,
            pendingUserInputCallIds = pendingSnapshot.UserInputCallIds,
            restoredPendingFollowUpCorrelations = restoredFollowUps.GetCorrelations(),
            restoredComposerDraftPreview = restoredFollowUps.ComposerDraft?.PreviewText,
            restoredComposerDraftMode = restoredFollowUps.ComposerDraft?.Mode.ToString(),
            restoredComposerDraftCorrelationId = restoredFollowUps.ComposerDraft?.CorrelationId,
            pendingRestoredFollowUpDispatchPreview = restoredFollowUps.PendingDispatch?.Draft.PreviewText,
            pendingRestoredFollowUpDispatchMode = restoredFollowUps.PendingDispatch?.Draft.Mode.ToString(),
            pendingRestoredFollowUpDispatchCorrelationId = restoredFollowUps.PendingDispatch?.Draft.CorrelationId,
            pendingRestoredFollowUpDispatchTurnId = restoredFollowUps.PendingDispatch?.TurnId,
            queuedRestoredFollowUpCount = restoredFollowUps.QueuedCount,
            approveAll = context.Options.ApproveAll,
            permissionsJson = context.PermissionScript?.SourcePath,
            userInputScript = context.UserInputScript?.SourcePath,
        }, context.JsonOptions), false);
    }
}

internal sealed record InteractiveStateCommandContext(
    ChatCommandOptions Options,
    PendingInteractiveRequestStore PendingRequests,
    RestoredFollowUpCoordinator RestoredFollowUps,
    ProbePermissionRequestScript? PermissionScript,
    ProbeUserInputScript? UserInputScript,
    JsonSerializerOptions JsonOptions,
    Action<ControlPlaneSessionSnapshot> ApplySessionSnapshot,
    Action<string, bool> WriteLine);
