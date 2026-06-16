using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction.Commands;

internal sealed record ChatCommandExecutionContext(
    Action<string> RecordExecutedInput,
    Action<string> WriteUserMessage,
    Func<string, CancellationToken, Task<bool>> ExecuteSlashCommandAsync,
    Func<string, CancellationToken, Task<bool>> TryExecutePlainThreadLifecycleCommandAsync,
    Func<string, CancellationToken, Task<bool>> TryExecuteShellCommandAsync,
    Func<string, ControlPlaneFollowUpMode?, CancellationToken, Task<bool>> TryExecuteRunningFollowUpAsync,
    Func<string, CancellationToken, Task<bool>> TryExecuteRestoredDraftAsync,
    Func<string, CancellationToken, Task> ExecuteNewTurnAsync);
