using TianShu.Cli.Interaction.Rendering;
using TianShu.Cli.Terminal;
using TianShu.ControlPlane.Abstractions.Conversations;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Commands.Threads;

/// <summary>
/// Resolves and applies startup resume/fork thread selections.
/// 解析并执行启动阶段的 resume/fork 线程选择。
/// </summary>
internal sealed class StartupThreadSelector
{
    public async Task<StartupThreadActionOutcome> ExecuteAsync(
        IConversationControlPlane conversations,
        ChatCommandOptions options,
        StartupThreadSelectorContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        if (options.StartupThreadAction == ChatStartupThreadActionKind.None)
        {
            return StartupThreadActionOutcome.Succeeded;
        }

        var (status, resolvedThreadId) = await ResolveSelectionAsync(conversations, options, context, cancellationToken).ConfigureAwait(false);
        if (status != StartupThreadActionOutcome.Succeeded || string.IsNullOrWhiteSpace(resolvedThreadId))
        {
            return status;
        }

        if (options.StartupThreadAction == ChatStartupThreadActionKind.Resume)
        {
            return await ResumeThreadAsync(conversations, resolvedThreadId, context, cancellationToken).ConfigureAwait(false);
        }

        return await ForkThreadAsync(conversations, resolvedThreadId, context, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<(StartupThreadActionOutcome Status, string? ThreadId)> ResolveSelectionAsync(
        IConversationControlPlane conversations,
        ChatCommandOptions options,
        StartupThreadSelectorContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        var matchCurrentCwd = !options.StartupThreadShowAll;
        var actionDisplayName = options.StartupThreadAction == ChatStartupThreadActionKind.Resume ? "恢复" : "分叉";

        if (!string.IsNullOrWhiteSpace(options.StartupThreadTarget))
        {
            var threads = await ListThreadsAsync(conversations, limit: 100, matchCurrentCwd, context, cancellationToken).ConfigureAwait(false);
            if (threads.Threads.Count == 0)
            {
                context.WriteLine($"未找到可用于{actionDisplayName}的线程。", true);
                return (StartupThreadActionOutcome.Failed, null);
            }

            var directMatch = threads.Threads.FirstOrDefault(thread =>
                string.Equals(thread.ThreadId.Value, options.StartupThreadTarget, StringComparison.Ordinal));
            if (directMatch is not null)
            {
                return (StartupThreadActionOutcome.Succeeded, directMatch.ThreadId.Value);
            }

            var nameMatches = threads.Threads
                .Where(thread => string.Equals(thread.Name, options.StartupThreadTarget, StringComparison.Ordinal))
                .ToArray();
            if (nameMatches.Length == 1)
            {
                return (StartupThreadActionOutcome.Succeeded, nameMatches[0].ThreadId.Value);
            }

            if (nameMatches.Length > 1)
            {
                context.WriteLine($"找到多个同名线程：{options.StartupThreadTarget}。请改用 threadId。", true);
                return (StartupThreadActionOutcome.Failed, null);
            }

            context.WriteLine($"未找到线程：{options.StartupThreadTarget}", true);
            return (StartupThreadActionOutcome.Failed, null);
        }

        if (options.StartupThreadUseLast)
        {
            var threads = await ListThreadsAsync(conversations, limit: 1, matchCurrentCwd, context, cancellationToken).ConfigureAwait(false);
            if (threads.Threads.Count == 0)
            {
                context.WriteLine($"未找到可用于{actionDisplayName}的线程。", true);
                return (StartupThreadActionOutcome.Failed, null);
            }

            return (StartupThreadActionOutcome.Succeeded, threads.Threads[0].ThreadId.Value);
        }

        var pickerThreads = await ListThreadsAsync(conversations, limit: 20, matchCurrentCwd, context, cancellationToken).ConfigureAwait(false);
        if (pickerThreads.Threads.Count == 0)
        {
            context.WriteLine($"未找到可用于{actionDisplayName}的线程。", true);
            return (StartupThreadActionOutcome.Failed, null);
        }

        if (context.ShouldUseTerminalThreadPicker()
            && (await context.SelectThreadWithTerminalAsync(
                    pickerThreads.Threads,
                    options.StartupThreadShowAll,
                    $"选择要{actionDisplayName}的线程",
                    cancellationToken).ConfigureAwait(false)) is { } selectedThread)
        {
            return (StartupThreadActionOutcome.Succeeded, selectedThread.ThreadId.Value);
        }

        context.WriteLine($"请选择要{actionDisplayName}的线程：", false);
        for (var index = 0; index < pickerThreads.Threads.Count; index++)
        {
            var thread = pickerThreads.Threads[index];
            context.WriteLine($"{index + 1}. {SelectionPickerRowRenderer.BuildStartupThreadPickerRow(thread, options.StartupThreadShowAll)}", false);
        }

        while (true)
        {
            context.WriteLine("输入编号后回车，直接回车取消。", false);
            var input = await context.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(input))
            {
                context.WriteLine("已取消线程选择。", false);
                return (StartupThreadActionOutcome.Cancelled, null);
            }

            if (int.TryParse(input, out var selectedIndex)
                && selectedIndex >= 1
                && selectedIndex <= pickerThreads.Threads.Count)
            {
                return (StartupThreadActionOutcome.Succeeded, pickerThreads.Threads[selectedIndex - 1].ThreadId.Value);
            }

            context.WriteLine($"无效编号：{input}", true);
        }
    }

    internal static async Task<ControlPlaneThreadSummary?> TrySelectThreadWithTianShuTerminalAsync(
        IReadOnlyList<ControlPlaneThreadSummary> threads,
        bool includeCwd,
        string title,
        CancellationToken cancellationToken,
        Func<IDisposable>? beginExclusiveFrameScope = null)
    {
        if (threads.Count == 0)
        {
            return null;
        }

        var rows = threads.Select(thread => SelectionPickerRowRenderer.BuildThreadListRow(thread, includeCwd)).ToArray();
        int? selectedIndex;
        try
        {
            selectedIndex = await new TerminalSelectionPicker(beginExclusiveFrameScope)
                .SelectAsync(rows, title, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            selectedIndex = null;
        }

        if (selectedIndex is null || selectedIndex < 0 || selectedIndex >= threads.Count)
        {
            return null;
        }

        return threads[selectedIndex.Value];
    }

    private static async Task<StartupThreadActionOutcome> ResumeThreadAsync(
        IConversationControlPlane conversations,
        string threadId,
        StartupThreadSelectorContext context,
        CancellationToken cancellationToken)
    {
        var resumed = await conversations.ResumeThreadAsync(
                new ControlPlaneResumeThreadCommand
                {
                    ThreadId = new ThreadId(threadId),
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (resumed is null)
        {
            context.WriteLine($"恢复线程失败：{threadId}", true);
            return StartupThreadActionOutcome.Failed;
        }

        context.ConsumeResumedThreadState(resumed, cancellationToken);
        context.WriteLine($"已恢复线程：{resumed.Thread.ThreadId.Value}（种子历史 {resumed.SeedHistory.Count}，回合数 {resumed.Turns.Count}）", false);
        if (resumed.PendingInteractiveRequests.Count > 0)
        {
            var counts = context.GetPendingInteractiveCounts();
            context.WriteLine($"已回放待处理交互：审批 {counts.Approvals}，权限 {counts.Permissions}，补录 {counts.UserInputs}。", false);
        }

        return StartupThreadActionOutcome.Succeeded;
    }

    private static async Task<StartupThreadActionOutcome> ForkThreadAsync(
        IConversationControlPlane conversations,
        string threadId,
        StartupThreadSelectorContext context,
        CancellationToken cancellationToken)
    {
        var forked = await conversations.ForkThreadAsync(
                new ControlPlaneForkThreadCommand
                {
                    ThreadId = new ThreadId(threadId),
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (forked is null)
        {
            context.WriteLine($"分叉线程失败：{threadId}", true);
            return StartupThreadActionOutcome.Failed;
        }

        context.WriteLine($"已分叉线程：{forked.ThreadId.Value}", false);
        return StartupThreadActionOutcome.Succeeded;
    }

    private static Task<ControlPlaneThreadListResult> ListThreadsAsync(
        IConversationControlPlane conversations,
        int limit,
        bool matchCurrentCwd,
        StartupThreadSelectorContext context,
        CancellationToken cancellationToken)
        => conversations.ListThreadsAsync(
            new ControlPlaneThreadListQuery
            {
                Limit = limit,
                Archived = false,
                WorkingDirectory = matchCurrentCwd ? context.GetCurrentDirectory() : null,
                SortKey = "updated_at",
            },
            cancellationToken);
}
