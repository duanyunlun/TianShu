using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using TianShu.AppHost.State;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelSubagentNotificationAppHostRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly Func<string, bool, CancellationToken, Task<JsonNode?>> getAgentStatusNodeAsync;
    private readonly ConcurrentDictionary<string, byte> subagentCompletionWatchers = new(StringComparer.Ordinal);

    public KernelSubagentNotificationAppHostRuntime(
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        Func<string, bool, CancellationToken, Task<JsonNode?>> getAgentStatusNodeAsync)
    {
        this.threadStore = threadStore;
        this.threadManager = threadManager;
        this.getAgentStatusNodeAsync = getAgentStatusNodeAsync;
    }

    public string? FormatEnvironmentContextSubagents(string parentThreadId)
    {
        var normalizedParentThreadId = Normalize(parentThreadId);
        if (string.IsNullOrWhiteSpace(normalizedParentThreadId))
        {
            return null;
        }

        var agents = new List<string>();
        foreach (var threadId in threadManager.GetLoadedThreadIds())
        {
            if (!threadManager.TryGetThread(threadId, out var runtimeThread) || runtimeThread is null)
            {
                continue;
            }

            var subAgentSource = runtimeThread.Session.SessionSource?.SubAgentSource;
            if (subAgentSource is not
                {
                    Kind: KernelSubAgentSourceKind.ThreadSpawn,
                    ParentThreadId: { Length: > 0 } agentParentThreadId,
                }
                || !string.Equals(agentParentThreadId, normalizedParentThreadId, StringComparison.Ordinal))
            {
                continue;
            }

            agents.Add(FormatSubagentContextLine(
                threadId,
                runtimeThread.Record.AgentNickname ?? subAgentSource.AgentNickname));
        }

        return agents.Count == 0
            ? null
            : string.Join(Environment.NewLine, agents.OrderBy(static line => line, StringComparer.Ordinal));
    }

    public void MaybeStartSubagentCompletionWatcher(string childThreadId, KernelSessionSource? sessionSource)
    {
        var parentThreadId = sessionSource?.SubAgentSource is
        {
            Kind: KernelSubAgentSourceKind.ThreadSpawn,
            ParentThreadId: { Length: > 0 } parentId,
        }
            ? parentId
            : null;
        if (string.IsNullOrWhiteSpace(parentThreadId))
        {
            return;
        }

        if (!subagentCompletionWatchers.TryAdd(childThreadId, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var status = await WaitForFinalAgentStatusAsync(childThreadId).ConfigureAwait(false);
                if (!KernelToolRuntimeAgentHelpers.IsFinalAgentStatus(status))
                {
                    return;
                }

                await AppendSubagentNotificationAsync(parentThreadId!, childThreadId, status, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 子代理通知回流只用于补齐后续上下文，不应干扰主链路。
            }
            finally
            {
                subagentCompletionWatchers.TryRemove(childThreadId, out _);
            }
        });
    }

    private async Task<JsonNode?> WaitForFinalAgentStatusAsync(string childThreadId)
    {
        while (true)
        {
            var status = await getAgentStatusNodeAsync(
                childThreadId,
                false,
                CancellationToken.None).ConfigureAwait(false);
            if (KernelToolRuntimeAgentHelpers.IsFinalAgentStatus(status))
            {
                return status;
            }

            await Task.Delay(KernelToolRuntimeAgentHelpers.WaitPollInterval, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task AppendSubagentNotificationAsync(
        string parentThreadId,
        string childThreadId,
        JsonNode? status,
        CancellationToken cancellationToken)
    {
        var parentRecord = await threadStore.GetThreadAsync(parentThreadId, cancellationToken).ConfigureAwait(false);
        if (parentRecord is null || parentRecord.IsArchived)
        {
            return;
        }

        var message = KernelSubagentNotificationUtilities.Format(childThreadId, status);
        if (parentRecord.SeedHistory.Any(item => string.Equals(
                Normalize(item.Content),
                message,
                StringComparison.Ordinal)))
        {
            return;
        }

        var historyItem = new KernelConversationHistoryItem
        {
            Role = "user",
            Content = message,
        };

        _ = await threadStore.AppendSeedHistoryItemAsync(parentThreadId, historyItem, cancellationToken).ConfigureAwait(false);
    }

    private static string FormatSubagentContextLine(string agentId, string? agentNickname)
    {
        var normalizedAgentId = Normalize(agentId);
        if (string.IsNullOrWhiteSpace(normalizedAgentId))
        {
            return string.Empty;
        }

        var normalizedNickname = Normalize(agentNickname);
        return string.IsNullOrWhiteSpace(normalizedNickname)
            ? $"- {normalizedAgentId}"
            : $"- {normalizedAgentId}: {normalizedNickname}";
    }

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
