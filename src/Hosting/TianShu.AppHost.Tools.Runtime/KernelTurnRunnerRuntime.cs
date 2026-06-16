using TianShu.Execution.Runtime;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// AppHost turn runner runtime，负责组合通用 turn runner 与默认模型 turn implementation。
/// AppHost turn runner runtime that composes the generic turn runner and default model-turn implementation.
/// </summary>
internal sealed class KernelTurnRunnerRuntime
{
    private readonly TurnExecutionRunner<TurnRequestContext> turnExecutionRunner;

    public KernelTurnRunnerRuntime(
        Func<TurnExecutionRunRequest<TurnRequestContext>, CancellationToken, Task> runModelTurnAsync)
    {
        ArgumentNullException.ThrowIfNull(runModelTurnAsync);

        turnExecutionRunner = TurnExecutionRunnerFactory.CreateDefaultModelTurnRunner(runModelTurnAsync);
    }

    public async Task RunAsync(
        string threadId,
        string turnId,
        string userText,
        TurnRequestContext turnContext,
        bool persistExtendedHistory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turnContext);

        var dispatchContext = turnContext.ExecutionDispatchContext
            ?? throw new ArgumentException("Execution dispatch context 不能为空。", nameof(turnContext));
        var request = TurnExecutionRunRequestFactory.Create(
            threadId,
            turnId,
            userText,
            turnContext,
            persistExtendedHistory,
            dispatchContext);
        await turnExecutionRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
