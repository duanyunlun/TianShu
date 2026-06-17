using TianShu.Contracts.Agents;
using TianShu.Contracts.Kernel;

namespace TianShu.RuntimeComposition;

/// <summary>
/// sub-agent 预算 admission 决策；拒绝时不递增任何预算计数。
/// Sub-agent budget admission decision; denied decisions do not increment any budget counters.
/// </summary>
public sealed record SubAgentBudgetAdmissionDecision(
    bool Admitted,
    string? FailureCode,
    string? FailureMessage,
    KernelBudget? AllocatedBudget);

/// <summary>
/// sub-agent 整树预算 ledger，供 P29.3 fanout 调度器在启动子 run 前执行资源 admission。
/// Whole-tree sub-agent budget ledger used by the P29.3 fan-out scheduler for resource admission before child runs start.
/// </summary>
public interface ISubAgentTreeBudgetLedger
{
    SubAgentBudgetAdmissionDecision TryAllocateForChild(
        SubAgentLineage parentLineage,
        SubAgentTreeBudget treeBudget,
        SubAgentBudgetSplit split,
        int plannedSubTaskCount,
        KernelBudget? requestedBudgetOverride = null);
}

/// <summary>
/// 默认 sub-agent 预算 ledger；预算分配单调递增，不因子 run 结束而自动回收。
/// Default sub-agent budget ledger; allocations are monotonic and are not automatically reclaimed when child runs end.
/// </summary>
public sealed class SubAgentTreeBudgetLedger : ISubAgentTreeBudgetLedger
{
    private readonly object gate = new();
    private int allocatedSubTasks;
    private KernelBudget allocatedBudget = KernelBudget.Zero;

    public SubAgentBudgetAdmissionDecision TryAllocateForChild(
        SubAgentLineage parentLineage,
        SubAgentTreeBudget treeBudget,
        SubAgentBudgetSplit split,
        int plannedSubTaskCount,
        KernelBudget? requestedBudgetOverride = null)
    {
        ArgumentNullException.ThrowIfNull(parentLineage);
        ArgumentNullException.ThrowIfNull(treeBudget);
        ArgumentNullException.ThrowIfNull(split);

        lock (gate)
        {
            if (plannedSubTaskCount < 1)
            {
                return Deny("subagent.fanout_item_count_invalid", "sub-agent fanout 计划子任务数必须为正。");
            }

            if (plannedSubTaskCount > treeBudget.MaxSubTasks)
            {
                return Deny("subagent.fanout_item_count_exceeded", "sub-agent fanout 计划子任务数超过整树上限。");
            }

            if (parentLineage.Depth + 1 > treeBudget.MaxDepth)
            {
                return Deny("subagent.spawn_depth_exceeded", "sub-agent spawn 深度超过预算闸门。");
            }

            if (allocatedSubTasks + 1 > treeBudget.MaxSubTasks)
            {
                return Deny("subagent.subtask_budget_exhausted", "sub-agent 整树子任务数量预算已耗尽。");
            }

            var remainingBudget = Subtract(treeBudget.RootBudget, allocatedBudget);
            if (treeBudget.MaxCost is { } maxCost)
            {
                remainingBudget = new KernelBudget(
                    remainingBudget.TokenBudget,
                    remainingBudget.TimeBudgetMs,
                    Math.Min(remainingBudget.CostBudget, Math.Max(0, maxCost - allocatedBudget.CostBudget)),
                    remainingBudget.RetryBudget,
                    remainingBudget.ToolCallBudget);
            }

            var allocation = ResolveAllocation(split, plannedSubTaskCount, requestedBudgetOverride, remainingBudget, treeBudget.MaxBudgetPerAgent);
            if (!allocation.Admitted)
            {
                return allocation;
            }

            var childBudget = allocation.AllocatedBudget!;
            if (!HasAnyPositiveBudget(childBudget))
            {
                return Deny("subagent.child_budget_empty", "sub-agent 子预算必须至少包含一个正预算维度。");
            }

            if (!FitsWithin(childBudget, remainingBudget))
            {
                return Deny("subagent.tree_budget_exhausted", "sub-agent 子预算超过父级剩余整树预算。");
            }

            allocatedSubTasks++;
            allocatedBudget = Add(allocatedBudget, childBudget);
            return new SubAgentBudgetAdmissionDecision(true, null, null, childBudget);
        }
    }

    private static SubAgentBudgetAdmissionDecision ResolveAllocation(
        SubAgentBudgetSplit split,
        int plannedSubTaskCount,
        KernelBudget? requestedBudgetOverride,
        KernelBudget remainingBudget,
        KernelBudget? maxBudgetPerAgent)
    {
        KernelBudget childBudget;
        switch (split.Mode)
        {
            case SubAgentBudgetSplitMode.EqualShare:
                childBudget = Divide(remainingBudget, plannedSubTaskCount);
                break;
            case SubAgentBudgetSplitMode.ExplicitPerItem:
                if (requestedBudgetOverride is null)
                {
                    return Deny("subagent.child_budget_missing", "显式子预算模式必须提供 item 级预算。");
                }

                childBudget = requestedBudgetOverride;
                break;
            case SubAgentBudgetSplitMode.ConservativeMinimum:
                childBudget = requestedBudgetOverride is null
                    ? Divide(remainingBudget, plannedSubTaskCount)
                    : MinBudget(Divide(remainingBudget, plannedSubTaskCount), requestedBudgetOverride);
                break;
            default:
                return Deny("subagent.budget_split_mode_invalid", "sub-agent 预算拆分模式无效。");
        }

        childBudget = ApplyTreeCeiling(childBudget, maxBudgetPerAgent);
        childBudget = ApplySplitCeiling(childBudget, split);
        return new SubAgentBudgetAdmissionDecision(true, null, null, childBudget);
    }

    private static KernelBudget ApplyTreeCeiling(KernelBudget budget, KernelBudget? ceiling)
        => ceiling is null
            ? budget
            : new KernelBudget(
                MinPositiveCeiling(budget.TokenBudget, ceiling.TokenBudget),
                MinPositiveCeiling(budget.TimeBudgetMs, ceiling.TimeBudgetMs),
                MinPositiveCeiling(budget.CostBudget, ceiling.CostBudget),
                MinPositiveCeiling(budget.RetryBudget, ceiling.RetryBudget),
                MinPositiveCeiling(budget.ToolCallBudget, ceiling.ToolCallBudget));

    private static KernelBudget ApplySplitCeiling(KernelBudget budget, SubAgentBudgetSplit split)
        => new(
            split.MaxTokensPerAgent is { } tokenCeiling ? Math.Min(budget.TokenBudget, tokenCeiling) : budget.TokenBudget,
            split.MaxTimePerAgent is { } timeCeiling ? Math.Min(budget.TimeBudgetMs, checked((long)timeCeiling.TotalMilliseconds)) : budget.TimeBudgetMs,
            split.MaxCostPerAgent is { } costCeiling ? Math.Min(budget.CostBudget, costCeiling) : budget.CostBudget,
            split.MaxRetriesPerAgent is { } retryCeiling ? Math.Min(budget.RetryBudget, retryCeiling) : budget.RetryBudget,
            split.MaxToolCallsPerAgent is { } toolCeiling ? Math.Min(budget.ToolCallBudget, toolCeiling) : budget.ToolCallBudget);

    private static KernelBudget Add(KernelBudget left, KernelBudget right)
        => new(
            checked(left.TokenBudget + right.TokenBudget),
            checked(left.TimeBudgetMs + right.TimeBudgetMs),
            left.CostBudget + right.CostBudget,
            checked(left.RetryBudget + right.RetryBudget),
            checked(left.ToolCallBudget + right.ToolCallBudget));

    private static KernelBudget Subtract(KernelBudget left, KernelBudget right)
        => new(
            Math.Max(0, left.TokenBudget - right.TokenBudget),
            Math.Max(0, left.TimeBudgetMs - right.TimeBudgetMs),
            Math.Max(0, left.CostBudget - right.CostBudget),
            Math.Max(0, left.RetryBudget - right.RetryBudget),
            Math.Max(0, left.ToolCallBudget - right.ToolCallBudget));

    private static KernelBudget Divide(KernelBudget budget, int divisor)
        => new(
            budget.TokenBudget / divisor,
            budget.TimeBudgetMs / divisor,
            budget.CostBudget / divisor,
            budget.RetryBudget / divisor,
            budget.ToolCallBudget / divisor);

    private static KernelBudget MinBudget(KernelBudget left, KernelBudget right)
        => new(
            Math.Min(left.TokenBudget, right.TokenBudget),
            Math.Min(left.TimeBudgetMs, right.TimeBudgetMs),
            Math.Min(left.CostBudget, right.CostBudget),
            Math.Min(left.RetryBudget, right.RetryBudget),
            Math.Min(left.ToolCallBudget, right.ToolCallBudget));

    private static bool FitsWithin(KernelBudget budget, KernelBudget ceiling)
        => budget.TokenBudget <= ceiling.TokenBudget
           && budget.TimeBudgetMs <= ceiling.TimeBudgetMs
           && budget.CostBudget <= ceiling.CostBudget
           && budget.RetryBudget <= ceiling.RetryBudget
           && budget.ToolCallBudget <= ceiling.ToolCallBudget;

    private static bool HasAnyPositiveBudget(KernelBudget budget)
        => budget.TokenBudget > 0
           || budget.TimeBudgetMs > 0
           || budget.CostBudget > 0
           || budget.RetryBudget > 0
           || budget.ToolCallBudget > 0;

    private static int MinPositiveCeiling(int value, int ceiling)
        => ceiling > 0 ? Math.Min(value, ceiling) : value;

    private static long MinPositiveCeiling(long value, long ceiling)
        => ceiling > 0 ? Math.Min(value, ceiling) : value;

    private static decimal MinPositiveCeiling(decimal value, decimal ceiling)
        => ceiling > 0 ? Math.Min(value, ceiling) : value;

    private static SubAgentBudgetAdmissionDecision Deny(string code, string message)
        => new(false, code, message, null);
}
