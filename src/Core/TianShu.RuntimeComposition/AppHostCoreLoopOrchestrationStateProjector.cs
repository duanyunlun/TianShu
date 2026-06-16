using TianShu.Execution.Runtime;
using TianShu.Kernel;

namespace TianShu.RuntimeComposition;

/// <summary>
/// AppHost Core Loop 编排状态投影器，负责把宿主线程状态转换为控制层编排输入。
/// AppHost core-loop orchestration state projector that converts host thread state into control-plane orchestration input.
/// </summary>
internal sealed class AppHostCoreLoopOrchestrationStateProjector
{
    public SessionOrchestrationInput ProjectInput(
        string normalizedThreadId,
        string correlationId,
        string? requestedStageId,
        KernelThreadOrchestrationStateRecord? orchestrationState,
        SessionObservedState? observedState = null)
    {
        var projection = KernelThreadOrchestrationInputProjectionFactory.Project(orchestrationState);
        return SessionOrchestrationInputFactory.Create(
            normalizedThreadId,
            correlationId,
            currentStageId: projection.CurrentStageId,
            latestCheckpointStageId: projection.LatestCheckpointStageId,
            requestedStageId: requestedStageId,
            checkpoints: projection.Checkpoints,
            contextLedgerSegments: projection.ContextLedgerSegments,
            observedState: observedState);
    }
}
