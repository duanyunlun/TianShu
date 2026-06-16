using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Interactions;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelArtifactsAppHostContext(
    string? Cwd,
    string? PersistedConfigText,
    InteractionEnvelopeRef? InteractionEnvelope = null);

internal sealed class KernelArtifactsAppHostRuntime
{
    private readonly KernelRolloutRecorder rolloutRecorder;
    private readonly Func<string?, KernelArtifactsRuntimeOptions> resolveArtifactsRuntimeOptions;
    private readonly KernelArtifactsRuntimeManager artifactsRuntimeManager = new();

    public KernelArtifactsAppHostRuntime(
        KernelRolloutRecorder rolloutRecorder,
        Func<string?, KernelArtifactsRuntimeOptions> resolveArtifactsRuntimeOptions)
    {
        this.rolloutRecorder = rolloutRecorder;
        this.resolveArtifactsRuntimeOptions = resolveArtifactsRuntimeOptions;
    }

    public async Task<KernelArtifactsExecutionResult> ExecuteAsync(
        string threadId,
        string turnId,
        KernelArtifactsAppHostContext context,
        KernelArtifactsExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var executionRequest = KernelExecutionEnvelopeFactory.CreateArtifactsRequest(threadId, turnId, context.Cwd, request, context.InteractionEnvelope);
        await rolloutRecorder.AppendExecutionRequestAsync(threadId, executionRequest, cancellationToken).ConfigureAwait(false);
        await rolloutRecorder.AppendExecutionEventAsync(
            threadId,
            KernelExecutionEnvelopeFactory.CreateStartedEvent(executionRequest, "artifacts execution started"),
            cancellationToken).ConfigureAwait(false);

        try
        {
            if (!KernelArtifactsRuntimeHelpers.ResolveConfiguredArtifactEnabled(context.PersistedConfigText))
            {
                var disabledResult = new KernelArtifactsExecutionResult(false, "artifacts is disabled by feature flag");
                await rolloutRecorder.AppendExecutionEventAsync(
                    threadId,
                    KernelExecutionEnvelopeFactory.CreateFailedEvent(
                        executionRequest,
                        "artifacts execution failed",
                        KernelExecutionEnvelopeFactory.CreateArtifactsData(disabledResult)),
                    cancellationToken).ConfigureAwait(false);
                return disabledResult;
            }

            var output = await artifactsRuntimeManager
                .ExecuteBuildAsync(
                    request,
                    resolveArtifactsRuntimeOptions(context.Cwd),
                    context.Cwd ?? Directory.GetCurrentDirectory(),
                    cancellationToken)
                .ConfigureAwait(false);
            var result = new KernelArtifactsExecutionResult(output.Success, KernelArtifactsRuntimeManager.FormatOutput(output));
            await rolloutRecorder.AppendExecutionEventAsync(
                threadId,
                result.Success
                    ? KernelExecutionEnvelopeFactory.CreateCompletedEvent(
                        executionRequest,
                        "artifacts execution completed",
                        KernelExecutionEnvelopeFactory.CreateArtifactsData(result))
                    : KernelExecutionEnvelopeFactory.CreateFailedEvent(
                        executionRequest,
                        "artifacts execution failed",
                        KernelExecutionEnvelopeFactory.CreateArtifactsData(result)),
                cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await rolloutRecorder.AppendExecutionEventAsync(
                threadId,
                KernelExecutionEnvelopeFactory.CreateFailedEvent(
                    executionRequest,
                    ex.Message,
                    KernelExecutionEnvelopeFactory.CreateFailureData(ex.Message)),
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
