using System.Security.Cryptography;
using System.Text;
using TianShu.Contracts.Agents;

namespace TianShu.RuntimeComposition;

/// <summary>
/// sub-agent spawn admission 决策；拒绝时不递增任何 ledger 计数。
/// Sub-agent spawn admission decision; denied decisions do not increment any ledger counters.
/// </summary>
public sealed record SubAgentSpawnDecision(
    bool Admitted,
    string? FailureCode,
    string? FailureMessage,
    SubAgentLineage? ChildLineage);

/// <summary>
/// 树级 sub-agent spawn 记账器。
/// Tree-level ledger for sub-agent spawn accounting.
/// </summary>
public interface ISubAgentSpawnLedger
{
    SubAgentSpawnDecision TryAdmitSpawn(SubAgentLineage parentLineage, SubAgentSpawnQuota quota);

    void OnChildTerminated(string childRunId);
}

/// <summary>
/// 默认 sub-agent spawn ledger；累计计数只增不减，并发计数仅在 quota 启用时回收。
/// Default sub-agent spawn ledger; cumulative counters are monotonic and concurrent counters are recoverable only when quota enables them.
/// </summary>
public sealed class SubAgentSpawnLedger : ISubAgentSpawnLedger
{
    private readonly object gate = new();
    private readonly Dictionary<string, int> cumulativeFanoutByRunId = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeChildRunIds = new(StringComparer.Ordinal);
    private int cumulativeTreeNodes;

    public SubAgentSpawnLedger(int initialTreeNodes = 1)
    {
        if (initialTreeNodes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(initialTreeNodes), "sub-agent 树至少包含根 run。");
        }

        cumulativeTreeNodes = initialTreeNodes;
    }

    public SubAgentSpawnDecision TryAdmitSpawn(SubAgentLineage parentLineage, SubAgentSpawnQuota quota)
    {
        ArgumentNullException.ThrowIfNull(parentLineage);
        ArgumentNullException.ThrowIfNull(quota);

        lock (gate)
        {
            if (parentLineage.Depth + 1 > quota.MaxSpawnDepth)
            {
                return Deny("subagent.spawn_depth_exceeded", "sub-agent spawn 深度超过结构闸门。");
            }

            var currentFanout = cumulativeFanoutByRunId.TryGetValue(parentLineage.CurrentRunId, out var fanout)
                ? fanout
                : 0;
            if (currentFanout + 1 > quota.MaxFanoutPerAgent)
            {
                return Deny("subagent.fanout_exceeded", "sub-agent 单 agent 累计扇出超过结构闸门。");
            }

            if (cumulativeTreeNodes + 1 > quota.MaxTreeNodes)
            {
                return Deny("subagent.tree_node_budget_exhausted", "sub-agent 树累计节点数超过结构闸门。");
            }

            if (quota.MaxConcurrentAgents > 0 && activeChildRunIds.Count + 1 > quota.MaxConcurrentAgents)
            {
                return Deny("subagent.concurrency_exceeded", "sub-agent 活跃并发数超过结构闸门。");
            }

            var childRunId = CreateDeterministicChildRunId(parentLineage, currentFanout);
            var childLineage = parentLineage.Descend(childRunId, currentFanout);
            cumulativeFanoutByRunId[parentLineage.CurrentRunId] = currentFanout + 1;
            cumulativeTreeNodes++;
            if (quota.MaxConcurrentAgents > 0)
            {
                activeChildRunIds.Add(childRunId);
            }

            return new SubAgentSpawnDecision(true, null, null, childLineage);
        }
    }

    public void OnChildTerminated(string childRunId)
    {
        if (string.IsNullOrWhiteSpace(childRunId))
        {
            return;
        }

        lock (gate)
        {
            activeChildRunIds.Remove(childRunId);
        }
    }

    private static SubAgentSpawnDecision Deny(string code, string message)
        => new(false, code, message, null);

    private static string CreateDeterministicChildRunId(SubAgentLineage parentLineage, int siblingIndex)
    {
        var seed = $"{parentLineage.LedgerRef}|{parentLineage.RootRunId}|{parentLineage.CurrentRunId}|{siblingIndex}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        return $"subagent-run-{hash[..16]}";
    }
}
