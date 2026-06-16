using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction.Commands;

internal sealed class ChatCommandLoop(ChatCommandExecutionContext context)
{
    public async Task<bool> ExecuteInputLineAsync(
        string line,
        CancellationToken cancellationToken,
        ControlPlaneFollowUpMode? runningPlainTextMode = null)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        context.RecordExecutedInput(trimmed);

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return await context.ExecuteSlashCommandAsync(trimmed, cancellationToken).ConfigureAwait(false);
        }

        if (await context.TryExecutePlainThreadLifecycleCommandAsync(trimmed, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (await context.TryExecuteShellCommandAsync(trimmed, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (await context.TryExecuteRunningFollowUpAsync(trimmed, runningPlainTextMode, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (await context.TryExecuteRestoredDraftAsync(trimmed, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        context.WriteUserMessage(trimmed);
        await context.ExecuteNewTurnAsync(trimmed, cancellationToken).ConfigureAwait(false);
        return false;
    }
}
