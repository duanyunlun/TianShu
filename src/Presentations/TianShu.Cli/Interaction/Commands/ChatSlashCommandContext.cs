using TianShu.Contracts.Governance;

namespace TianShu.Cli.Interaction.Commands;

/// <summary>
/// Typed callback surface used by the chat slash command dispatcher.
/// Chat slash command dispatcher 使用的强类型宿主回调面。
/// </summary>
internal sealed record ChatSlashCommandContext(
    Action PrintHelp,
    Func<CancellationToken, Task> InterruptAsync,
    Func<string, CancellationToken, Task> FollowUpAsync,
    Func<CancellationToken, Task> InitAsync,
    Action Draft,
    Func<CancellationToken, Task> SendRestoredAsync,
    Action DropRestored,
    Func<string, ControlPlaneApprovalDecision, CancellationToken, Task> ApprovalAsync,
    Func<string, CancellationToken, Task> PermissionsAsync,
    Func<string, CancellationToken, Task> UserInputAsync,
    Func<string, CancellationToken, Task> ThreadsAsync,
    Func<string, CancellationToken, Task> ThreadLifecycleAsync,
    Func<string, CancellationToken, Task> ModelAsync,
    Func<string, CancellationToken, Task> ConfigAsync,
    Func<string, CancellationToken, Task> ReloadAsync,
    Func<CancellationToken, Task> NewThreadAsync,
    Func<string, CancellationToken, Task> ForkAsync,
    Func<string, CancellationToken, Task> ArchiveAsync,
    Func<string, CancellationToken, Task> RenameAsync,
    Func<string, CancellationToken, Task> ResumeAsync,
    Func<string, CancellationToken, Task> MemoryAsync,
    Func<string, CancellationToken, Task> RpcAsync,
    Func<CancellationToken, Task> StateAsync,
    Func<string, CancellationToken, Task> WaitAsync,
    Func<string, CancellationToken, Task> WaitEventAsync,
    Func<string, CancellationToken, Task> WaitNextToolCallAsync,
    Func<string, CancellationToken, Task> WaitCompleteAsync,
    Action<string, bool> WriteLine);
