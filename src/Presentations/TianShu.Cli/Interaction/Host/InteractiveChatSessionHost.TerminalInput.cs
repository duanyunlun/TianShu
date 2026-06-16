using TianShu.Contracts.Conversations;
using TianShu.Execution.Runtime;
using TianShu.Cli.Terminal;
using TianShu.Cli;

namespace TianShu.Cli.Interaction.Host;

internal sealed partial class InteractiveChatSessionHost
{
    private async Task<bool> RunTianShuTerminalChatCoreAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        ProbePermissionRequestScript? permissionScript,
        ProbeUserInputScript? userInputScript,
        CancellationToken cancellationToken)
    {
        TerminalChatInputLoopContext? inputLoopContext = null;
        inputLoopContext = new TerminalChatInputLoopContext
        {
            Options = options,
            GetInputHistoryScopeKey = () => Normalize(GetCurrentSessionThreadId()) ?? Normalize(runtime.ActiveThreadId),
            LoadInputHistory = inputHistoryStore.Load,
            BuildPrompt = () => restoredFollowUps.BuildPrompt("> "),
            RenderPrompt = (composer, renderer, prompt, popupLines, placeholder) =>
                terminalHost.RenderPrompt(composer, renderer, prompt, popupLines, placeholder),
            CompleteInputLine = (renderer, addVisualSpacerAfter, submittedText) =>
                terminalHost.CompleteInputLine(
                    renderer,
                    addVisualSpacerAfter,
                    clearSubmittedInput: !string.IsNullOrWhiteSpace(submittedText)),
            MoveQueuedFollowUpSelection = delta =>
            {
                lock (consoleGate)
                {
                    var changed = queuedFollowUpDockStore.MoveSelection(delta);
                    if (changed)
                    {
                        RefreshAndRestoreInlineTailPrompt(runtime, options);
                    }

                    return changed;
                }
            },
            PromoteSelectedQueuedFollowUpAsync = token => PromoteSelectedQueuedFollowUpAsync(runtime, options, token),
            SubmitLineAsync = async (line, intent, token) =>
            {
                var runningMode = intent switch
                {
                    TerminalSubmitIntent.Queue => ControlPlaneFollowUpMode.Queue,
                    TerminalSubmitIntent.Steer => ControlPlaneFollowUpMode.Steer,
                    _ => (ControlPlaneFollowUpMode?)null,
                };
                return await ExecuteInputLineAsync(
                        runtime,
                        options,
                        permissionScript,
                        userInputScript,
                        line,
                        token,
                        runningPlainTextMode: runningMode)
                    .ConfigureAwait(false);
            },
            RecordSubmittedInput = (line, intent) => inputHistoryStore.Append(
                Normalize(GetCurrentSessionThreadId()) ?? Normalize(runtime.ActiveThreadId),
                line,
                intent),
            ResetTerminal = () =>
            {
                lock (consoleGate)
                {
                    queuedFollowUpDockStore.Clear();
                    terminalHost.Reset();
                }
            },
        };

        readBlockingConfirmationAsync = token => terminalInputLoop.ReadConfirmationAsync(inputLoopContext, token);
        try
        {
            var result = await terminalInputLoop.RunAsync(inputLoopContext, cancellationToken).ConfigureAwait(false);
            return result.ShouldExit;
        }
        finally
        {
            inputNotice = null;
            readBlockingConfirmationAsync = null;
        }
    }

    private async Task<bool> PromoteSelectedQueuedFollowUpAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        CancellationToken cancellationToken)
    {
        var entry = queuedFollowUpDockStore.TryGetSelected();
        if (entry is null)
        {
            return false;
        }

        await followUpCommandHandler.HandleFollowUpAsync(
                CreateFollowUpCommandContext(runtime, options),
                $"promote {entry.Index}",
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
