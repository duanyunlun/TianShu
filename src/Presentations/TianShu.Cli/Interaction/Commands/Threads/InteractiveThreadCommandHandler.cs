using TianShu.ControlPlane.Abstractions.Conversations;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Commands.Threads;

/// <summary>
/// Handles interactive thread lifecycle slash commands.
/// 处理交互式线程生命周期 slash command。
/// </summary>
internal sealed class InteractiveThreadCommandHandler
{
    public async Task HandleNewThreadAsync(
        IConversationControlPlane conversations,
        ChatCommandOptions options,
        InteractiveThreadCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        var thread = await conversations.StartThreadAsync(
                new ControlPlaneStartThreadCommand
                {
                    Model = Normalize(options.RuntimeModel),
                    ModelProvider = Normalize(options.RuntimeModelProvider),
                    WorkingDirectory = options.WorkingDirectory,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (thread is not null)
        {
            context.ActivateThread(thread.ThreadId);
        }

        context.WriteLine(thread is null ? "创建线程失败。" : $"已创建线程：{thread.ThreadId.Value}", thread is null);
    }

    public async Task HandleForkThreadAsync(
        IConversationControlPlane conversations,
        string rest,
        InteractiveThreadCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(context);

        var threadId = Normalize(rest);
        if (threadId is null)
        {
            context.WriteLine("用法：/fork <threadId>", true);
            return;
        }

        var thread = await conversations.ForkThreadAsync(
                new ControlPlaneForkThreadCommand
                {
                    ThreadId = new ThreadId(threadId),
                },
                cancellationToken)
            .ConfigureAwait(false);
        context.WriteLine(thread is null ? "分叉线程失败。" : $"已分叉线程：{thread.ThreadId.Value}", thread is null);
    }

    public async Task HandleArchiveThreadAsync(
        IConversationControlPlane conversations,
        string rest,
        InteractiveThreadCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(context);

        var threadId = Normalize(rest);
        if (threadId is null)
        {
            context.WriteLine("用法：/archive <threadId>", true);
            return;
        }

        var archived = await conversations.ArchiveThreadAsync(
                new ControlPlaneArchiveThreadCommand
                {
                    ThreadId = new ThreadId(threadId),
                },
                cancellationToken)
            .ConfigureAwait(false);
        context.WriteLine(archived ? $"已归档线程：{threadId}" : $"归档线程失败：{threadId}", !archived);
    }

    public async Task HandleThreadLifecycleCommandAsync(
        IConversationControlPlane conversations,
        string rest,
        InteractiveThreadCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(context);

        var subcommand = ReadFirstToken(rest, out var commandRest).ToLowerInvariant();
        switch (subcommand)
        {
            case "delete":
                await HandleDeleteThreadAsync(conversations, commandRest, context, cancellationToken).ConfigureAwait(false);
                return;
            case "clear":
                await HandleClearThreadsAsync(conversations, commandRest, context, cancellationToken).ConfigureAwait(false);
                return;
            default:
                context.WriteLine("用法：/thread delete --thread-id <id> 或 /thread clear", true);
                return;
        }
    }

    public async Task HandleDeleteThreadAsync(
        IConversationControlPlane conversations,
        string rest,
        InteractiveThreadCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(context);

        var commandRest = ExtractTrailingConfirmation(rest, out var confirmation);
        var threadId = ReadNamedOption(commandRest, "--thread-id");
        if (threadId is null)
        {
            context.WriteLine("用法：/thread delete --thread-id <id>", true);
            return;
        }

        if (context.HasRunningConversation())
        {
            context.WriteLine("当前仍有运行中的回合，请先 /interrupt 或等待回合结束后再删除线程。", true);
            return;
        }

        if (!await context.ConfirmDestructiveOperationAsync(
                threadId,
                confirmation,
                $"将永久删除线程 {threadId} 及其会话日志。请输入完整 thread id 确认：",
                cancellationToken).ConfigureAwait(false))
        {
            context.WriteLine("已取消删除线程。", false);
            return;
        }

        var affectsCurrentThread = context.IsCurrentThread(threadId);
        if (affectsCurrentThread)
        {
            context.ClearCurrentThreadState();
        }

        var deleted = await conversations.DeleteThreadAsync(
                new ControlPlaneDeleteThreadCommand
                {
                    ThreadId = new ThreadId(threadId),
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectsCurrentThread)
        {
            context.ClearCurrentThreadState();
        }

        if (deleted)
        {
            context.ClearInputHistoryForThread(threadId);
        }

        context.WriteLine(deleted ? $"已删除线程：{threadId}" : $"删除线程失败：{threadId}", !deleted);
    }

    public async Task HandleClearThreadsAsync(
        IConversationControlPlane conversations,
        string rest,
        InteractiveThreadCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(context);

        _ = ExtractTrailingConfirmation(rest, out var confirmation);
        if (context.HasRunningConversation())
        {
            context.WriteLine("当前仍有运行中的回合，请先 /interrupt 或等待回合结束后再清空线程。", true);
            return;
        }

        if (!await context.ConfirmDestructiveOperationAsync(
                "DELETE ALL THREADS",
                confirmation,
                "将永久删除全部线程及全部会话日志，包括当前会话。请输入 DELETE ALL THREADS 确认：",
                cancellationToken).ConfigureAwait(false))
        {
            context.WriteLine("已取消清空线程。", false);
            return;
        }

        context.ClearCurrentThreadState();
        var result = await conversations.ClearThreadsAsync(new ControlPlaneClearThreadsCommand(), cancellationToken).ConfigureAwait(false);
        context.ClearCurrentThreadState();
        context.ClearAllInputHistory();
        context.WriteLine($"已清空线程：{result.DeletedCount} 个。", false);
    }

    public async Task HandleRenameThreadAsync(
        IConversationControlPlane conversations,
        string rest,
        InteractiveThreadCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(context);

        var threadId = ReadFirstToken(rest, out var name);
        if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(name))
        {
            context.WriteLine("用法：/rename <threadId> <name>", true);
            return;
        }

        var renamed = await conversations.RenameThreadAsync(
                new ControlPlaneRenameThreadCommand
                {
                    ThreadId = new ThreadId(threadId),
                    Name = name,
                },
                cancellationToken)
            .ConfigureAwait(false);
        context.WriteLine(renamed ? $"已重命名线程：{threadId}" : $"重命名线程失败：{threadId}", !renamed);
    }

    public async Task HandleResumeThreadAsync(
        string rest,
        InteractiveThreadCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var threadId = Normalize(rest);
        if (threadId is null)
        {
            context.WriteLine("用法：/resume <threadId>", true);
            return;
        }

        _ = await context.ResumeThreadByIdAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    internal static bool TryReadPlainThreadLifecycleCommand(string line, out string rest)
    {
        rest = string.Empty;
        if (!string.Equals(ReadFirstToken(line, out var commandRest), "thread", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var subcommand = ReadFirstToken(commandRest, out _);
        if (!string.Equals(subcommand, "delete", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(subcommand, "clear", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        rest = commandRest;
        return true;
    }

    internal static string ExtractTrailingConfirmation(string rest, out string? confirmation)
    {
        confirmation = null;
        var normalized = rest.Trim();
        var markerIndex = IndexOfCommandOption(normalized, "--confirm");
        if (markerIndex < 0)
        {
            return normalized;
        }

        confirmation = Normalize(normalized[(markerIndex + "--confirm".Length)..]);
        return normalized[..markerIndex].TrimEnd();
    }

    internal static string? ReadNamedOption(string value, string option)
    {
        var tokens = SplitCommandTokens(value);
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (string.Equals(tokens[i], option, StringComparison.OrdinalIgnoreCase))
            {
                return Normalize(tokens[i + 1]);
            }
        }

        return null;
    }

    private static int IndexOfCommandOption(string value, string option)
    {
        var startIndex = 0;
        while (startIndex < value.Length)
        {
            var index = value.IndexOf(option, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return -1;
            }

            var beforeBoundary = index == 0 || char.IsWhiteSpace(value[index - 1]);
            var after = index + option.Length;
            var afterBoundary = after >= value.Length || char.IsWhiteSpace(value[after]);
            if (beforeBoundary && afterBoundary)
            {
                return index;
            }

            startIndex = index + option.Length;
        }

        return -1;
    }

    private static string ReadFirstToken(string text, out string remainder)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            remainder = string.Empty;
            return string.Empty;
        }

        var splitIndex = trimmed.IndexOf(' ');
        if (splitIndex < 0)
        {
            remainder = string.Empty;
            return trimmed;
        }

        remainder = trimmed[(splitIndex + 1)..].Trim();
        return trimmed[..splitIndex];
    }

    private static string[] SplitCommandTokens(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
