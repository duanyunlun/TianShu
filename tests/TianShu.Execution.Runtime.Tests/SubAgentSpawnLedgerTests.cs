using TianShu.Contracts.Agents;
using TianShu.Contracts.Kernel;
using TianShu.RuntimeComposition;

namespace TianShu.Execution.Runtime.Tests;

public sealed class SubAgentSpawnLedgerTests
{
    [Fact]
    public void TryAdmitSpawn_ShouldDenyWhenDepthWouldExceedQuota()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = new SubAgentLineage("run-root", "run-child", "run-root", depth: 1, siblingIndex: 0, "ledger-001");

        var decision = ledger.TryAdmitSpawn(parent, new SubAgentSpawnQuota(1, 8, 32, 0));

        Assert.False(decision.Admitted);
        Assert.Equal("subagent.spawn_depth_exceeded", decision.FailureCode);
        Assert.Null(decision.ChildLineage);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldDenyWhenFanoutWouldExceedQuota()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();

        var decision = ledger.TryAdmitSpawn(parent, new SubAgentSpawnQuota(1, 0, 32, 0));

        Assert.False(decision.Admitted);
        Assert.Equal("subagent.fanout_exceeded", decision.FailureCode);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldDenyWhenTreeNodeBudgetWouldExceedQuota()
    {
        var ledger = new SubAgentSpawnLedger(initialTreeNodes: 1);
        var parent = CreateRootLineage();

        var decision = ledger.TryAdmitSpawn(parent, new SubAgentSpawnQuota(1, 8, 1, 0));

        Assert.False(decision.Admitted);
        Assert.Equal("subagent.tree_node_budget_exhausted", decision.FailureCode);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldNotUseConcurrencyGateWhenQuotaDisablesIt()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();
        var quota = new SubAgentSpawnQuota(1, 3, 4, 0);

        var first = ledger.TryAdmitSpawn(parent, quota);
        var second = ledger.TryAdmitSpawn(parent, quota);
        var third = ledger.TryAdmitSpawn(parent, quota);

        Assert.True(first.Admitted);
        Assert.True(second.Admitted);
        Assert.True(third.Admitted);
        Assert.NotEqual("subagent.concurrency_exceeded", first.FailureCode);
        Assert.NotEqual("subagent.concurrency_exceeded", second.FailureCode);
        Assert.NotEqual("subagent.concurrency_exceeded", third.FailureCode);
    }

    [Fact]
    public void OnChildTerminated_ShouldNotReclaimMonotonicFanout()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();
        var quota = new SubAgentSpawnQuota(1, 1, 32, 1);

        var first = ledger.TryAdmitSpawn(parent, quota);
        Assert.True(first.Admitted);
        ledger.OnChildTerminated(first.ChildLineage!.CurrentRunId);

        var second = ledger.TryAdmitSpawn(parent, quota);

        Assert.False(second.Admitted);
        Assert.Equal("subagent.fanout_exceeded", second.FailureCode);
    }

    [Fact]
    public void OnChildTerminated_ShouldNotReclaimMonotonicTreeNodes()
    {
        var ledger = new SubAgentSpawnLedger(initialTreeNodes: 1);
        var parent = CreateRootLineage();
        var quota = new SubAgentSpawnQuota(1, 8, 2, 1);

        var first = ledger.TryAdmitSpawn(parent, quota);
        Assert.True(first.Admitted);
        ledger.OnChildTerminated(first.ChildLineage!.CurrentRunId);

        var second = ledger.TryAdmitSpawn(parent, quota);

        Assert.False(second.Admitted);
        Assert.Equal("subagent.tree_node_budget_exhausted", second.FailureCode);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldEnforceAndReclaimConcurrentAgentsWhenEnabled()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();
        var quota = new SubAgentSpawnQuota(1, 8, 32, 1);

        var first = ledger.TryAdmitSpawn(parent, quota);
        var second = ledger.TryAdmitSpawn(parent, quota);

        Assert.True(first.Admitted);
        Assert.False(second.Admitted);
        Assert.Equal("subagent.concurrency_exceeded", second.FailureCode);

        ledger.OnChildTerminated(first.ChildLineage!.CurrentRunId);
        var third = ledger.TryAdmitSpawn(parent, quota);

        Assert.True(third.Admitted);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldDeriveDeterministicChildRunIdFromParentAndSiblingIndex()
    {
        var firstLedger = new SubAgentSpawnLedger();
        var secondLedger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();
        var quota = new SubAgentSpawnQuota(1, 8, 32, 0);

        var first = firstLedger.TryAdmitSpawn(parent, quota);
        var second = secondLedger.TryAdmitSpawn(parent, quota);
        var nextSibling = firstLedger.TryAdmitSpawn(parent, quota);

        Assert.True(first.Admitted);
        Assert.True(second.Admitted);
        Assert.True(nextSibling.Admitted);
        Assert.Equal(first.ChildLineage!.CurrentRunId, second.ChildLineage!.CurrentRunId);
        Assert.NotEqual(first.ChildLineage.CurrentRunId, nextSibling.ChildLineage!.CurrentRunId);
        Assert.Equal(0, first.ChildLineage.SiblingIndex);
        Assert.Equal(1, nextSibling.ChildLineage.SiblingIndex);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldNotMutateLedgerWhenStructuralGateDeniesForkBombAttempt()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();

        var deniedByFanout = ledger.TryAdmitSpawn(parent, new SubAgentSpawnQuota(1, 0, 32, 1));
        var admittedAfterDeniedAttempt = ledger.TryAdmitSpawn(parent, new SubAgentSpawnQuota(1, 1, 32, 1));
        ledger.OnChildTerminated(admittedAfterDeniedAttempt.ChildLineage!.CurrentRunId);
        var deniedByMonotonicFanout = ledger.TryAdmitSpawn(parent, new SubAgentSpawnQuota(1, 1, 32, 1));

        Assert.False(deniedByFanout.Admitted);
        Assert.Equal("subagent.fanout_exceeded", deniedByFanout.FailureCode);
        Assert.True(admittedAfterDeniedAttempt.Admitted);
        Assert.Equal(0, admittedAfterDeniedAttempt.ChildLineage.SiblingIndex);
        Assert.False(deniedByMonotonicFanout.Admitted);
        Assert.Equal("subagent.fanout_exceeded", deniedByMonotonicFanout.FailureCode);
    }

    [Fact]
    public void TryAllocateForChild_ShouldSplitGlobalBudgetByPlannedSubTasks()
    {
        var ledger = new SubAgentTreeBudgetLedger();
        var treeBudget = new SubAgentTreeBudget(
            new KernelBudget(tokenBudget: 900, timeBudgetMs: 9000, costBudget: 9, retryBudget: 3, toolCallBudget: 6),
            maxSubTasks: 3,
            maxDepth: 2,
            maxConcurrentAgents: 0);

        var decision = ledger.TryAllocateForChild(
            CreateRootLineage(),
            treeBudget,
            new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare),
            plannedSubTaskCount: 3);

        Assert.True(decision.Admitted);
        Assert.Equal(300, decision.AllocatedBudget!.TokenBudget);
        Assert.Equal(3000, decision.AllocatedBudget.TimeBudgetMs);
        Assert.Equal(3, decision.AllocatedBudget.CostBudget);
        Assert.Equal(1, decision.AllocatedBudget.RetryBudget);
        Assert.Equal(2, decision.AllocatedBudget.ToolCallBudget);
    }

    [Fact]
    public void TryAllocateForChild_ShouldApplyPerAgentCeilings()
    {
        var ledger = new SubAgentTreeBudgetLedger();
        var treeBudget = new SubAgentTreeBudget(
            new KernelBudget(tokenBudget: 1000, timeBudgetMs: 10000, costBudget: 10, retryBudget: 4, toolCallBudget: 8),
            maxSubTasks: 2,
            maxDepth: 2,
            maxConcurrentAgents: 0,
            maxBudgetPerAgent: new KernelBudget(tokenBudget: 250, timeBudgetMs: 2500, costBudget: 2, retryBudget: 1, toolCallBudget: 3));
        var split = new SubAgentBudgetSplit(
            SubAgentBudgetSplitMode.EqualShare,
            maxTokensPerAgent: 200,
            maxCostPerAgent: 1.5m,
            maxTimePerAgent: TimeSpan.FromMilliseconds(2000),
            maxToolCallsPerAgent: 2,
            maxRetriesPerAgent: 1);

        var decision = ledger.TryAllocateForChild(CreateRootLineage(), treeBudget, split, plannedSubTaskCount: 2);

        Assert.True(decision.Admitted);
        Assert.Equal(200, decision.AllocatedBudget!.TokenBudget);
        Assert.Equal(2000, decision.AllocatedBudget.TimeBudgetMs);
        Assert.Equal(1.5m, decision.AllocatedBudget.CostBudget);
        Assert.Equal(1, decision.AllocatedBudget.RetryBudget);
        Assert.Equal(2, decision.AllocatedBudget.ToolCallBudget);
    }

    [Fact]
    public void TryAllocateForChild_ShouldDenyWhenPlannedSubTaskCountExceedsGate()
    {
        var ledger = new SubAgentTreeBudgetLedger();

        var decision = ledger.TryAllocateForChild(
            CreateRootLineage(),
            CreateTreeBudget(maxSubTasks: 2),
            new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare),
            plannedSubTaskCount: 3);

        Assert.False(decision.Admitted);
        Assert.Equal("subagent.fanout_item_count_exceeded", decision.FailureCode);
    }

    [Fact]
    public void TryAllocateForChild_ShouldDenyWhenCumulativeSubTaskBudgetIsExhausted()
    {
        var ledger = new SubAgentTreeBudgetLedger();
        var treeBudget = CreateTreeBudget(maxSubTasks: 1);
        var split = new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare);

        var first = ledger.TryAllocateForChild(CreateRootLineage(), treeBudget, split, plannedSubTaskCount: 1);
        var second = ledger.TryAllocateForChild(CreateRootLineage(), treeBudget, split, plannedSubTaskCount: 1);

        Assert.True(first.Admitted);
        Assert.False(second.Admitted);
        Assert.Equal("subagent.subtask_budget_exhausted", second.FailureCode);
    }

    [Fact]
    public void TryAllocateForChild_ShouldDenyWhenDepthWouldExceedBudgetGate()
    {
        var ledger = new SubAgentTreeBudgetLedger();
        var parent = new SubAgentLineage("run-root", "run-child", "run-root", depth: 1, siblingIndex: 0, "ledger-001");

        var decision = ledger.TryAllocateForChild(
            parent,
            CreateTreeBudget(maxDepth: 1),
            new SubAgentBudgetSplit(SubAgentBudgetSplitMode.EqualShare),
            plannedSubTaskCount: 1);

        Assert.False(decision.Admitted);
        Assert.Equal("subagent.spawn_depth_exceeded", decision.FailureCode);
    }

    [Fact]
    public void TryAllocateForChild_ShouldDenyWhenExplicitBudgetExceedsRemainingGlobalBudget()
    {
        var ledger = new SubAgentTreeBudgetLedger();

        var decision = ledger.TryAllocateForChild(
            CreateRootLineage(),
            CreateTreeBudget(),
            new SubAgentBudgetSplit(SubAgentBudgetSplitMode.ExplicitPerItem),
            plannedSubTaskCount: 1,
            requestedBudgetOverride: new KernelBudget(tokenBudget: 2000, timeBudgetMs: 1000, toolCallBudget: 1));

        Assert.False(decision.Admitted);
        Assert.Equal("subagent.tree_budget_exhausted", decision.FailureCode);
    }

    [Fact]
    public void TryAllocateForChild_ShouldDenyWhenExplicitBudgetIsMissing()
    {
        var ledger = new SubAgentTreeBudgetLedger();

        var decision = ledger.TryAllocateForChild(
            CreateRootLineage(),
            CreateTreeBudget(),
            new SubAgentBudgetSplit(SubAgentBudgetSplitMode.ExplicitPerItem),
            plannedSubTaskCount: 1);

        Assert.False(decision.Admitted);
        Assert.Equal("subagent.child_budget_missing", decision.FailureCode);
    }

    private static SubAgentLineage CreateRootLineage()
        => new("run-root", "run-root", null, depth: 0, siblingIndex: 0, "ledger-001");

    private static SubAgentTreeBudget CreateTreeBudget(int maxSubTasks = 4, int maxDepth = 2)
        => new(
            new KernelBudget(tokenBudget: 1000, timeBudgetMs: 10000, costBudget: 10, retryBudget: 4, toolCallBudget: 8),
            maxSubTasks,
            maxDepth,
            maxConcurrentAgents: 0);
}
