using TianShu.Contracts.Governance;

namespace TianShu.Cli.Interaction.Commands;

/// <summary>
/// Maps slash command kinds to typed dispatcher handlers.
/// 将 slash command kind 映射到强类型 dispatcher handler，避免路由逻辑散落在 switch 中。
/// </summary>
internal sealed class ChatSlashCommandHandlerRegistry
{
    public static ChatSlashCommandHandlerRegistry Default { get; } = new();

    private readonly IReadOnlyDictionary<SlashCommandKind, ChatSlashCommandHandler> handlers;

    private ChatSlashCommandHandlerRegistry()
    {
        handlers = BuildHandlers();
    }

    public bool TryGetHandler(SlashCommandKind kind, out ChatSlashCommandHandler handler)
    {
        if (handlers.TryGetValue(kind, out var found))
        {
            handler = found;
            return true;
        }

        handler = null!;
        return false;
    }

    public IReadOnlyCollection<SlashCommandKind> RegisteredKinds => handlers.Keys.ToArray();

    private static Dictionary<SlashCommandKind, ChatSlashCommandHandler> BuildHandlers()
        => new()
        {
            [SlashCommandKind.Help] = new(SlashCommandKind.Help, static (rest, context, cancellationToken) =>
            {
                context.PrintHelp();
                return ValueTask.FromResult(false);
            }),
            [SlashCommandKind.Exit] = new(SlashCommandKind.Exit, static (rest, context, cancellationToken) => ValueTask.FromResult(true)),
            [SlashCommandKind.Interrupt] = new(SlashCommandKind.Interrupt, static async (rest, context, cancellationToken) =>
            {
                await context.InterruptAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }),
            [SlashCommandKind.FollowUp] = new(SlashCommandKind.FollowUp, static async (rest, context, cancellationToken) =>
            {
                await context.FollowUpAsync(rest, cancellationToken).ConfigureAwait(false);
                return false;
            }),
            [SlashCommandKind.Init] = new(SlashCommandKind.Init, static async (rest, context, cancellationToken) =>
            {
                await context.InitAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }),
            [SlashCommandKind.Draft] = new(SlashCommandKind.Draft, static (rest, context, cancellationToken) =>
            {
                context.Draft();
                return ValueTask.FromResult(false);
            }),
            [SlashCommandKind.SendRestored] = new(SlashCommandKind.SendRestored, static async (rest, context, cancellationToken) =>
            {
                await context.SendRestoredAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }),
            [SlashCommandKind.DropRestored] = new(SlashCommandKind.DropRestored, static (rest, context, cancellationToken) =>
            {
                context.DropRestored();
                return ValueTask.FromResult(false);
            }),
            [SlashCommandKind.Approve] = Approval(SlashCommandKind.Approve, ControlPlaneApprovalDecision.Approve),
            [SlashCommandKind.ApproveSession] = Approval(SlashCommandKind.ApproveSession, ControlPlaneApprovalDecision.ApproveForSession),
            [SlashCommandKind.ApproveAlways] = Approval(SlashCommandKind.ApproveAlways, ControlPlaneApprovalDecision.ApproveAndRemember),
            [SlashCommandKind.Reject] = Approval(SlashCommandKind.Reject, ControlPlaneApprovalDecision.Decline),
            [SlashCommandKind.CancelApproval] = Approval(SlashCommandKind.CancelApproval, ControlPlaneApprovalDecision.Cancel),
            [SlashCommandKind.Permissions] = RestCommand(SlashCommandKind.Permissions, static (context, rest, cancellationToken) => context.PermissionsAsync(rest, cancellationToken)),
            [SlashCommandKind.Input] = RestCommand(SlashCommandKind.Input, static (context, rest, cancellationToken) => context.UserInputAsync(rest, cancellationToken)),
            [SlashCommandKind.Threads] = RestCommand(SlashCommandKind.Threads, static (context, rest, cancellationToken) => context.ThreadsAsync(rest, cancellationToken)),
            [SlashCommandKind.Thread] = RestCommand(SlashCommandKind.Thread, static (context, rest, cancellationToken) => context.ThreadLifecycleAsync(rest, cancellationToken)),
            [SlashCommandKind.Model] = RestCommand(SlashCommandKind.Model, static (context, rest, cancellationToken) => context.ModelAsync(rest, cancellationToken)),
            [SlashCommandKind.Config] = RestCommand(SlashCommandKind.Config, static (context, rest, cancellationToken) => context.ConfigAsync(rest, cancellationToken)),
            [SlashCommandKind.Reload] = RestCommand(SlashCommandKind.Reload, static (context, rest, cancellationToken) => context.ReloadAsync(rest, cancellationToken)),
            [SlashCommandKind.New] = new(SlashCommandKind.New, static async (rest, context, cancellationToken) =>
            {
                await context.NewThreadAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }),
            [SlashCommandKind.Fork] = RestCommand(SlashCommandKind.Fork, static (context, rest, cancellationToken) => context.ForkAsync(rest, cancellationToken)),
            [SlashCommandKind.Archive] = RestCommand(SlashCommandKind.Archive, static (context, rest, cancellationToken) => context.ArchiveAsync(rest, cancellationToken)),
            [SlashCommandKind.Rename] = RestCommand(SlashCommandKind.Rename, static (context, rest, cancellationToken) => context.RenameAsync(rest, cancellationToken)),
            [SlashCommandKind.Resume] = RestCommand(SlashCommandKind.Resume, static (context, rest, cancellationToken) => context.ResumeAsync(rest, cancellationToken)),
            [SlashCommandKind.Memory] = RestCommand(SlashCommandKind.Memory, static (context, rest, cancellationToken) => context.MemoryAsync(rest, cancellationToken)),
            [SlashCommandKind.Rpc] = RestCommand(SlashCommandKind.Rpc, static (context, rest, cancellationToken) => context.RpcAsync(rest, cancellationToken)),
            [SlashCommandKind.State] = new(SlashCommandKind.State, static async (rest, context, cancellationToken) =>
            {
                await context.StateAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }),
            [SlashCommandKind.Wait] = RestCommand(SlashCommandKind.Wait, static (context, rest, cancellationToken) => context.WaitAsync(rest, cancellationToken)),
            [SlashCommandKind.WaitEvent] = RestCommand(SlashCommandKind.WaitEvent, static (context, rest, cancellationToken) => context.WaitEventAsync(rest, cancellationToken)),
            [SlashCommandKind.WaitNextToolCall] = RestCommand(SlashCommandKind.WaitNextToolCall, static (context, rest, cancellationToken) => context.WaitNextToolCallAsync(rest, cancellationToken)),
            [SlashCommandKind.WaitComplete] = RestCommand(SlashCommandKind.WaitComplete, static (context, rest, cancellationToken) => context.WaitCompleteAsync(rest, cancellationToken)),
        };

    private static ChatSlashCommandHandler Approval(
        SlashCommandKind kind,
        ControlPlaneApprovalDecision decision)
        => new(kind, async (rest, context, cancellationToken) =>
        {
            await context.ApprovalAsync(rest, decision, cancellationToken).ConfigureAwait(false);
            return false;
        });

    private static ChatSlashCommandHandler RestCommand(
        SlashCommandKind kind,
        Func<ChatSlashCommandContext, string, CancellationToken, Task> execute)
        => new(kind, async (rest, context, cancellationToken) =>
        {
            await execute(context, rest, cancellationToken).ConfigureAwait(false);
            return false;
        });
}

internal sealed record ChatSlashCommandHandler(
    SlashCommandKind Kind,
    Func<string, ChatSlashCommandContext, CancellationToken, ValueTask<bool>> ExecuteAsync);
